using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Joktan.Certificates;
using Joktan.Containers;
using Joktan.Containers.ViewModels;
using Joktan.Mqtt;
using Joktan.Mqtt.ViewModels;
using Joktan.Resources;

namespace Joktan.ViewModels;

/// <summary>
/// Keeps the clients on Dev Tools / Brokers / MQTT in step with the Mosquitto brokers made on
/// Data / MQTT: each broker gets a client when it is saved, every client linked to a broker
/// follows it when it or a certificate changes, and goes when the broker is removed. Each port of
/// each broker is offered in the MQTT client editor.
/// </summary>
public static class BrokerClients
{
    /// <summary>Keeps the MQTT page's clients in step with the brokers from now on, starting with those there now.</summary>
    public static void Link(MosquittoBrokersViewModel brokers, MqttViewModel mqtt)
    {
        void Sync(IEnumerable<string> add)
        {
            mqtt.SyncBrokers(ChoicesFor(brokers.Brokers, brokers.Certificates, brokers.PasswordOf), add);
            brokers.ConnectionsChanged();
        }

        // The card shows how each broker's client is doing, as it changes.
        brokers.ConnectionOf = broker => mqtt.Clients.FirstOrDefault(c => c.Profile.BrokerId == broker.Id) is { } client
            ? new BrokerConnection(client.StateText, client.IsConnected, client.IsPending, client.IsFailed)
            : null;
        void Watch(MqttClientViewModel client) => client.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MqttClientViewModel.StateText))
                brokers.ConnectionsChanged();
        };
        foreach (var client in mqtt.Clients)
            Watch(client);
        mqtt.Clients.CollectionChanged += (_, e) =>
        {
            foreach (var client in e.NewItems?.OfType<MqttClientViewModel>() ?? [])
                Watch(client);
            brokers.ConnectionsChanged();
        };

        brokers.Saved += broker => Sync([broker.Id]);
        brokers.Removed += _ => Sync([]);
        brokers.Certificates.Changed += () => Sync([]);
        Sync(brokers.Brokers.Select(b => b.Id));
    }

    /// <summary>
    /// Each port of each broker as a way to connect from this computer, in the order plain MQTT,
    /// TLS, mutual TLS, so a broker's first is the one a new client uses.
    /// </summary>
    public static List<BrokerChoice> ChoicesFor(IEnumerable<MosquittoBroker> brokers, CertificateStore certificates, System.Func<MosquittoBroker, string?> passwordOf)
    {
        var choices = new List<BrokerChoice>();
        foreach (var broker in brokers)
        {
            var password = passwordOf(broker) ?? "";
            foreach (var listener in new[] { BrokerListener.Plain, BrokerListener.Tls, BrokerListener.MutualTls })
            {
                if (ProfileFor(broker, certificates, listener) is { } profile)
                    choices.Add(new BrokerChoice(broker.Id, broker.Name, listener, string.Format(Text(listener), broker.Name, profile.Port), profile, password));
            }
        }

        return choices;
    }

    /// <summary>
    /// How a client connects to one of the broker's ports on this computer, or null when it has no
    /// such port. TLS trusts the CAs that signed its certificate; mutual TLS also shows the first
    /// client certificate signed by the CA the broker asks for.
    /// </summary>
    public static ClientProfile? ProfileFor(MosquittoBroker broker, CertificateStore certificates, BrokerListener listener)
    {
        var port = listener switch
        {
            BrokerListener.Plain => broker.Port,
            BrokerListener.Tls => broker.TlsPort,
            _ => broker.MutualTlsPort,
        };
        if (port is not { } number)
            return null;

        var profile = new ClientProfile { Name = broker.Name, Host = "localhost", Port = number.ToString(CultureInfo.InvariantCulture), Username = broker.Username };
        if (listener == BrokerListener.Plain)
            return profile;

        profile.UseTls = true;
        var issuer = certificates.Find(certificates.Find(broker.ServerCertificateId)?.IssuerId);
        if (issuer is not null)
        {
            profile.CaCertificate = issuer.AuthorityPath;
            profile.CaCertificateId = issuer.Id;
        }

        if (listener == BrokerListener.Tls)
            return profile;

        var authority = certificates.Find(broker.ClientAuthorityId) ?? issuer;
        var client = authority is null
            ? null
            : certificates.Entries.FirstOrDefault(e => e.Kind == CertificateKind.Client && e.Certificate is not null && e.Problem is null
                                                       && certificates.IssuersOf(e).Contains(authority));
        if (client is not null)
        {
            profile.ClientCertificate = client.CertificatePath;
            profile.ClientKey = client.KeyPath;
            profile.ClientCertificateId = client.Id;
        }

        return profile;
    }

    private static string Text(BrokerListener listener) => listener switch
    {
        BrokerListener.Plain => Strings.BrokerClients_Plain,
        BrokerListener.Tls => Strings.BrokerClients_Tls,
        _ => Strings.BrokerClients_MutualTls,
    };
}
