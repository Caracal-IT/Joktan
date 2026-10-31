using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Joktan.Tests.TestSupport;

namespace Joktan.Certificates.Tests.ViewModels;

/// <summary>A certificate store in a folder of its own, for each test.</summary>
public abstract class CertificateTestBase : IDisposable
{
    protected CertificateTestBase()
    {
        Store = CertificateStore.Load(Folder.Path);
    }

    protected TempFolder Folder { get; } = new();

    protected CertificateStore Store { get; private set; }

    protected static CertificateSpec Spec(string name, CertificateKind kind, StoredCertificate? issuer = null, string names = "") => new()
    {
        Kind = kind,
        Name = name,
        CommonName = name,
        IssuerId = issuer?.Id,
        AlternativeNames = names,
        NotAfter = DateTimeOffset.UtcNow.AddYears(1),
    };

    protected StoredCertificate Root(string name = "root") => Store.Create(Spec(name, CertificateKind.RootAuthority));

    protected StoredCertificate Server(StoredCertificate issuer, string name = "server") =>
        Store.Create(Spec(name, CertificateKind.Server, issuer, "localhost, 127.0.0.1"));

    protected StoredCertificate Client(StoredCertificate issuer, string name = "client") =>
        Store.Create(Spec(name, CertificateKind.Client, issuer));

    /// <summary>Replaces an entry's certificate file with one valid for other dates, as the clock cannot be moved.</summary>
    protected StoredCertificate WithDates(StoredCertificate entry, DateTimeOffset notBefore, DateTimeOffset notAfter, bool rsa = false)
    {
        X509Certificate2 certificate;
        if (rsa)
        {
            using var key = RSA.Create(2048);
            certificate = new CertificateRequest($"CN={entry.Name}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
                .CreateSelfSigned(notBefore, notAfter);
        }
        else
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            certificate = new CertificateRequest($"CN={entry.Name}", key, HashAlgorithmName.SHA256)
                .CreateSelfSigned(notBefore, notAfter);
        }

        File.WriteAllText(entry.CertificatePath, certificate.ExportCertificatePem());
        return Reload(entry.Id);
    }

    /// <summary>Reads the store again from its files.</summary>
    protected StoredCertificate Reload(string id)
    {
        Store = CertificateStore.Load(Folder.Path);
        return Store.Find(id)!;
    }

    public void Dispose()
    {
        Folder.Dispose();
        GC.SuppressFinalize(this);
    }
}
