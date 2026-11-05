using System;
using Joktan.Certificates;

namespace Joktan.Containers.Tests;

/// <summary>A certificate store of its own with a root CA, a server certificate it signed and a second CA.</summary>
internal sealed class TestCertificates
{
    public TestCertificates(string folder)
    {
        Store = CertificateStore.Load(folder);
        Root = Store.Create(Spec("root", CertificateKind.RootAuthority));
        Server = Store.Create(Spec("localhost", CertificateKind.Server, Root, "localhost"));
        Other = Store.Create(Spec("other ca", CertificateKind.RootAuthority));
        Client = Store.Create(Spec("client", CertificateKind.Client, Root));
    }

    public CertificateStore Store { get; }

    public StoredCertificate Root { get; }

    public StoredCertificate Server { get; }

    public StoredCertificate Other { get; }

    public StoredCertificate Client { get; }

    private static CertificateSpec Spec(string name, CertificateKind kind, StoredCertificate? issuer = null, string names = "") => new()
    {
        Kind = kind,
        Name = name,
        CommonName = name,
        IssuerId = issuer?.Id,
        AlternativeNames = names,
        NotAfter = DateTimeOffset.UtcNow.AddYears(1),
    };
}
