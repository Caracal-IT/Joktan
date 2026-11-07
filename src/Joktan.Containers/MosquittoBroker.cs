using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Serialization;
using Joktan.Certificates;
using Joktan.Containers.Resources;

namespace Joktan.Containers;

/// <summary>
/// A Mosquitto broker made on Data / MQTT and written into Joktan's own compose file for
/// brokers, as a service of that name. Kept in containers.json; its password is in the credential
/// store, under <see cref="Account"/>.
/// </summary>
public sealed class MosquittoBroker
{
    /// <summary>The ports inside the container, which the ones chosen are published on.</summary>
    public const int ContainerPort = 1883, ContainerTlsPort = 8883, ContainerMutualTlsPort = 8884;

    public const int MaxNameLength = 40;

    public string Id { get; set; } = NewId();

    /// <summary>The compose service, and the folder its configuration is written to.</summary>
    public string Name { get; set; } = "";

    /// <summary>The port for plain MQTT on the host; null for none.</summary>
    public int? Port { get; set; }

    /// <summary>The port for TLS on the host; null for none.</summary>
    public int? TlsPort { get; set; }

    /// <summary>The port for mutual TLS on the host, where clients show a certificate; null for none.</summary>
    public int? MutualTlsPort { get; set; }

    /// <summary>The one user clients sign in as; empty to let anyone connect without signing in.</summary>
    public string Username { get; set; } = "";

    /// <summary>The server certificate from the Certificates page that the broker shows on its TLS ports.</summary>
    public string? ServerCertificateId { get; set; }

    /// <summary>
    /// The CA from the Certificates page that clients' certificates must be signed by on the mutual
    /// TLS port; null for the CAs that signed the server certificate.
    /// </summary>
    public string? ClientAuthorityId { get; set; }

    /// <summary>What its password is filed under in the credential store.</summary>
    [JsonIgnore]
    public string Account => $"mosquitto:{Id}";

    [JsonIgnore]
    public bool HasLogin => Username.Length > 0;

    [JsonIgnore]
    public bool UsesTls => TlsPort is not null || MutualTlsPort is not null;

    /// <summary>The ports it publishes on the host.</summary>
    [JsonIgnore]
    public IEnumerable<int> HostPorts => new[] { Port, TlsPort, MutualTlsPort }.OfType<int>();

    /// <summary>The ports as on the page, such as "1883 · TLS 8883 · mutual TLS 8884".</summary>
    [JsonIgnore]
    public string PortsText => string.Join(" · ", new[]
    {
        Port is { } plain ? plain.ToString(CultureInfo.InvariantCulture) : null,
        TlsPort is { } tls ? string.Format(Strings.MosquittoBroker_TlsPort, tls) : null,
        MutualTlsPort is { } mutual ? string.Format(Strings.MosquittoBroker_MutualTlsPort, mutual) : null,
    }.OfType<string>());

    public MosquittoBroker Clone() => (MosquittoBroker)MemberwiseClone();

    public static string NewId() => Guid.NewGuid().ToString("N")[..12];

    /// <summary>
    /// What is wrong with a broker's name, or null. It becomes a compose service, a folder and part
    /// of an image's name, so only lower-case letters, digits, dashes and underscores will do.
    /// </summary>
    public static string? CheckName(string name, IEnumerable<string> others)
    {
        name = name.Trim();
        if (name.Length == 0)
            return Strings.MosquittoBroker_EnterAName;
        if (name.Length > MaxNameLength)
            return string.Format(Strings.MosquittoBroker_TheNameCanBeAtMost, MaxNameLength);
        if ((!char.IsAsciiLetterLower(name[0]) && !char.IsAsciiDigit(name[0]))
            || name.Any(c => !char.IsAsciiLetterLower(c) && !char.IsAsciiDigit(c) && c is not ('-' or '_')))
            return Strings.MosquittoBroker_UseLowerCaseLettersDigits;
        if (others.Any(o => string.Equals(o, name, StringComparison.Ordinal)))
            return string.Format(Strings.MosquittoBroker_ABrokerIsAlreadyCalled, name);
        return null;
    }

    /// <summary>Reads a port typed on the page: empty for none, or 1 to 65535. Returns what is wrong, or null.</summary>
    public static string? ParsePort(string text, out int? port)
    {
        port = null;
        text = text.Trim();
        if (text.Length == 0)
            return null;
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value is < 1 or > 65535)
            return string.Format(Strings.MosquittoBroker_IsNotAPort, text);
        port = value;
        return null;
    }

    /// <summary>
    /// What stops the broker from being written, or null: no port, a port used twice or by another
    /// broker, a login with no password, or a certificate that is missing or of the wrong kind.
    /// </summary>
    public string? Check(IEnumerable<MosquittoBroker> others, CertificateStore certificates, bool hasPassword)
    {
        var ports = HostPorts.ToList();
        if (ports.Count == 0)
            return Strings.MosquittoBroker_ChooseAtLeastOnePort;
        if (ports.Distinct().Count() < ports.Count)
            return Strings.MosquittoBroker_EachPortMustBeDifferent;
        foreach (var other in others.Where(o => o.Id != Id))
        {
            if (other.HostPorts.Intersect(ports).FirstOrDefault() is var taken and > 0)
                return string.Format(Strings.MosquittoBroker_PortIsUsedBy, taken, other.Name);
        }

        if (Username.Contains(':'))
            return Strings.MosquittoBroker_TheUsernameCannotHaveAColon;
        if (HasLogin && !hasPassword)
            return Strings.MosquittoBroker_EnterAPasswordForTheUser;

        if (!UsesTls)
            return null;
        if (certificates.Find(ServerCertificateId) is not { } server)
            return Strings.MosquittoBroker_ChooseAServerCertificate;
        if (server.Kind != CertificateKind.Server)
            return string.Format(Strings.MosquittoBroker_IsNotAServerCertificate, server.DisplayName);
        if (server.Problem is { } problem)
            return problem;
        if (MutualTlsPort is not null && ClientAuthorityId is not null)
        {
            if (certificates.Find(ClientAuthorityId) is not { } authority)
                return Strings.MosquittoBroker_TheCAForClientCertificatesIsGone;
            if (!authority.Kind.IsAuthority())
                return string.Format(Strings.MosquittoBroker_IsNotACA, authority.DisplayName);
            if (authority.Certificate is null)
                return authority.Problem ?? Strings.MosquittoBroker_TheCAForClientCertificatesIsGone;
        }

        return null;
    }
}
