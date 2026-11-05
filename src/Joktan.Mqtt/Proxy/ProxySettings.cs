using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Security.Cryptography.X509Certificates;
using Joktan.Core;
using Joktan.Mqtt.Resources;

namespace Joktan.Mqtt.Proxy;

/// <summary>
/// A folder message a proxy intercepts, and the topic it goes to the broker on: empty for its own,
/// or another with the same fields, such as <c>lab/{line}/status</c> for <c>plant/{line}/status</c>.
/// </summary>
public sealed record Interception(FolderMessage Message, string BrokerTopic = "");

/// <summary>How devices sign in to a proxy.</summary>
public enum DeviceLogin
{
    /// <summary>Any device may connect, with any login or none.</summary>
    Anyone,

    /// <summary>With the proxy's own username and password, which need not be the broker's.</summary>
    Own,

    /// <summary>With a login the target broker accepts, tried there as the device connects.</summary>
    Broker,
}

public static class DeviceLoginText
{
    /// <summary>What a DeviceLogin is called on screen.</summary>
    public static string Text(this DeviceLogin value) => value switch
    {
        DeviceLogin.Anyone => Strings.DeviceLogin_Anyone,
        DeviceLogin.Own => Strings.DeviceLogin_Own,
        _ => Strings.DeviceLogin_Broker,
    };
}

/// <summary>How devices connect to a proxy on one of its ports.</summary>
public enum ProxyListener
{
    Plain,
    Tls,
    MutualTls,
}

public static class ProxyListenerText
{
    /// <summary>What a ProxyListener is called on screen.</summary>
    public static string Text(this ProxyListener value) => value switch
    {
        ProxyListener.Plain => Strings.ProxyListener_Plain,
        ProxyListener.Tls => Strings.ProxyListener_Tls,
        _ => Strings.ProxyListener_MutualTls,
    };
}

/// <summary>
/// The ports a proxy listens on, null for none, with the certificate it shows on its TLS ports and
/// the CAs a client's certificate must chain to on its mutual TLS port.
/// </summary>
public sealed record ProxyListeners(int? Port, int? TlsPort, int? MutualTlsPort, bool AllowOtherComputers,
    X509Certificate2? ServerCertificate = null, X509Certificate2Collection? ClientAuthorities = null)
{
    public IEnumerable<(ProxyListener Kind, int Port)> Ports()
    {
        if (Port is { } plain)
            yield return (ProxyListener.Plain, plain);
        if (TlsPort is { } tls)
            yield return (ProxyListener.Tls, tls);
        if (MutualTlsPort is { } mutual)
            yield return (ProxyListener.MutualTls, mutual);
    }
}

/// <summary>One proxy on Dev Tools / Brokers / MQTT Proxy. The target broker's password is in the credential store.</summary>
public sealed class ProxyProfile
{
    public const int DefaultPort = 1885;
    public const int DefaultTlsPort = 8885;
    public const int DefaultMutualTlsPort = 8886;

    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];

    /// <summary>The name on its tab.</summary>
    public string Name { get; set; } = "";

    /// <summary>The port devices connect to with plain MQTT instead of the broker; null for none.</summary>
    public int? ListenPort { get; set; } = DefaultPort;

    /// <summary>The port devices connect to with TLS; null for none.</summary>
    public int? TlsPort { get; set; }

    /// <summary>The port devices connect to with mutual TLS, showing a certificate; null for none.</summary>
    public int? MutualTlsPort { get; set; }

    /// <summary>The server certificate from the Certificates page the proxy shows on its TLS ports.</summary>
    public string? ServerCertificateId { get; set; }

    /// <summary>The CA on the Certificates page devices' certificates must be signed by; null for those that signed the server certificate.</summary>
    public string? ClientAuthorityId { get; set; }

    /// <summary>Every port it listens on.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IEnumerable<int> Ports => new[] { ListenPort, TlsPort, MutualTlsPort }.OfType<int>();

    /// <summary>How devices sign in.</summary>
    [System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<DeviceLogin>))]
    public DeviceLogin DeviceLogin { get; set; } = DeviceLogin.Broker;

    /// <summary>The username devices sign in with, with <see cref="DeviceLogin.Own"/>. Its password is in the credential store, under <see cref="DevicePasswordAccount"/>.</summary>
    public string DeviceUsername { get; set; } = "";

    /// <summary>What the password devices sign in with is filed under.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string DevicePasswordAccount => $"proxy:{Id}";

    /// <summary>Whether devices on other computers may connect; otherwise only this one.</summary>
    public bool AllowOtherComputers { get; set; }

    /// <summary>The folder of messages that can be intercepted, in the same form as a client's folder messages.</summary>
    public string? Folder { get; set; }

    /// <summary>The intercepted messages, by their path inside <see cref="Folder"/>, with forward slashes.</summary>
    public List<string> Selected { get; set; } = [];

    /// <summary>The broker everything is forwarded to, which can follow a broker on Dev Tools / Containers.</summary>
    public ClientProfile Target { get; set; } = new() { Host = "localhost", Port = "1883" };

    /// <summary>The topic each intercepted message goes to the broker on, by its path, where it is not its own.</summary>
    public Dictionary<string, string> BrokerTopics { get; set; } = [];
}

/// <summary>The proxies, kept in <c>mqtt/proxies.json</c> in Joktan's data folder.</summary>
public sealed class ProxySettings
{
    public List<ProxyProfile> Proxies { get; set; } = [];

    /// <summary>The tab shown last, by proxy ID.</summary>
    public string SelectedProxy { get; set; } = "";

    public static string PathIn(string folder) => Path.Combine(folder, "proxies.json");

    public static ProxySettings Load(string? folder = null)
    {
        var settings = new ProxySettings();
        try
        {
            var path = PathIn(folder ?? DataFolder.For("mqtt"));
            if (File.Exists(path))
                settings = JsonSerializer.Deserialize<ProxySettings>(File.ReadAllText(path)) ?? new();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // An unreadable file is treated as no proxies.
        }

        settings.Proxies = (settings.Proxies ?? []).Where(p => p is not null && p.Id.Length > 0).ToList();
        foreach (var proxy in settings.Proxies)
        {
            proxy.Selected = (proxy.Selected ?? []).Where(s => s is not null).ToList();
            proxy.BrokerTopics ??= [];
            proxy.Target ??= new ClientProfile { Host = "localhost", Port = "1883" };
        }

        return settings;
    }

    public void Save(string? folder = null)
    {
        folder ??= DataFolder.For("mqtt");
        Directory.CreateDirectory(folder);
        File.WriteAllText(PathIn(folder), JsonSerializer.Serialize(this, JsonFormats.Indented));
    }
}
