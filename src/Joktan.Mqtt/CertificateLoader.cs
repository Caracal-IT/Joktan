using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Joktan.Mqtt;

/// <summary>
/// Reads the certificate files a client's TLS settings name. Every failure is reported as a
/// <see cref="FormatException"/> whose message can be shown to the user as it is.
/// </summary>
public static class CertificateLoader
{
    /// <summary>Whether a client certificate file is PFX/P12, which holds its own key, rather than PEM.</summary>
    public static bool IsPkcs12(string path)
        => Path.GetExtension(path).ToLowerInvariant() is ".pfx" or ".p12";

    /// <summary>
    /// Reads one or more CA certificates: a PEM file, which may hold several, or a single DER file.
    /// </summary>
    public static X509Certificate2Collection LoadAuthorities(string path)
    {
        var file = Existing(path, "CA certificate");
        var certificates = new X509Certificate2Collection();
        try
        {
            if (File.ReadAllText(file).Contains("-----BEGIN CERTIFICATE-----", StringComparison.Ordinal))
                certificates.ImportFromPemFile(file);
            else
                certificates.Add(X509CertificateLoader.LoadCertificateFromFile(file));
        }
        catch (CryptographicException e)
        {
            throw new FormatException($"The CA certificate {Path.GetFileName(file)} cannot be read: {e.Message}", e);
        }

        if (certificates.Count == 0)
            throw new FormatException($"The CA certificate {Path.GetFileName(file)} holds no certificate");
        return certificates;
    }

    /// <summary>
    /// Reads the client's certificate with its private key. A PFX/P12 file holds both; a PEM
    /// certificate takes its key from <paramref name="keyPath"/>, or from the same file when that
    /// is empty. <paramref name="password"/> opens a PFX/P12 file or an encrypted PEM key.
    /// </summary>
    public static X509Certificate2 LoadClient(string certificatePath, string? keyPath, string? password)
    {
        var certificateFile = Existing(certificatePath, "client certificate");
        var name = Path.GetFileName(certificateFile);
        X509Certificate2 certificate;

        if (IsPkcs12(certificateFile))
        {
            try
            {
                certificate = X509CertificateLoader.LoadPkcs12FromFile(certificateFile, password ?? "");
            }
            catch (CryptographicException e)
            {
                throw new FormatException(string.IsNullOrEmpty(password)
                    ? $"{name} cannot be opened; if it has a password, enter it"
                    : $"{name} cannot be opened: the password is wrong or the file is damaged ({e.Message})", e);
            }
        }
        else
        {
            var keyFile = string.IsNullOrWhiteSpace(keyPath) ? certificateFile : Existing(keyPath, "private key");
            if (string.IsNullOrEmpty(password) && IsEncryptedPem(keyFile))
                throw new FormatException($"The private key in {Path.GetFileName(keyFile)} is encrypted; enter its password");
            try
            {
                using var pem = string.IsNullOrEmpty(password)
                    ? X509Certificate2.CreateFromPemFile(certificateFile, keyFile)
                    : X509Certificate2.CreateFromEncryptedPemFile(certificateFile, password, keyFile);

                // Windows and macOS do not offer a key that exists only in memory during a TLS
                // handshake, so the pair is turned into PKCS #12 and read back.
                certificate = X509CertificateLoader.LoadPkcs12(pem.Export(X509ContentType.Pkcs12), null);
            }
            catch (CryptographicException e)
            {
                throw new FormatException(DescribePemFailure(name, Path.GetFileName(keyFile), keyFile == certificateFile, password, e), e);
            }
            catch (ArgumentException e)
            {
                throw new FormatException($"{name} or its key cannot be read: {e.Message}", e);
            }
        }

        if (!certificate.HasPrivateKey)
            throw new FormatException($"{name} has no private key, so it cannot be used as a client certificate");

        var now = DateTime.Now;
        if (certificate.NotAfter < now)
            throw new FormatException($"The client certificate expired on {certificate.NotAfter:yyyy-MM-dd}");
        if (certificate.NotBefore > now)
            throw new FormatException($"The client certificate is not valid until {certificate.NotBefore:yyyy-MM-dd}");
        return certificate;
    }

    /// <summary>
    /// Checks a broker certificate against the given CA certificates instead of the system's
    /// trusted roots. Returns why it is not trusted, or null.
    /// </summary>
    public static string? CheckChain(X509Certificate2 certificate, X509Certificate2Collection authorities, X509Chain? presented)
    {
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.AddRange(authorities);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;

        // Intermediate certificates the broker sent, and any in the CA file, can complete the chain.
        chain.ChainPolicy.ExtraStore.AddRange(authorities);
        if (presented is not null)
        {
            foreach (var element in presented.ChainElements)
                chain.ChainPolicy.ExtraStore.Add(element.Certificate);
        }

        if (chain.Build(certificate))
            return null;

        var problems = chain.ChainStatus
            .Where(s => s.Status != X509ChainStatusFlags.NoError)
            .Select(s => s.Status switch
            {
                // Windows reports NotSignatureValid when another CA of the same name is the issuer.
                X509ChainStatusFlags.UntrustedRoot or X509ChainStatusFlags.PartialChain or X509ChainStatusFlags.NotSignatureValid
                    => "it is not signed by the CA certificate",
                X509ChainStatusFlags.NotTimeValid => "it has expired or is not valid yet",
                _ => s.StatusInformation.Trim(),
            })
            .Distinct()
            .ToList();
        return problems.Count == 0 ? "its chain could not be checked" : string.Join("; ", problems);
    }

    private static bool IsEncryptedPem(string path)
    {
        try
        {
            var text = File.ReadAllText(path);
            return text.Contains("-----BEGIN ENCRYPTED PRIVATE KEY-----", StringComparison.Ordinal)
                || text.Contains("Proc-Type: 4,ENCRYPTED", StringComparison.Ordinal);
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static string DescribePemFailure(string name, string keyName, bool sameFile, string? password, CryptographicException e)
    {
        if (e.Message.Contains("password", StringComparison.OrdinalIgnoreCase) || e.Message.Contains("decrypt", StringComparison.OrdinalIgnoreCase))
            return string.IsNullOrEmpty(password)
                ? $"The private key in {keyName} is encrypted; enter its password"
                : $"The private key in {keyName} cannot be opened: the password is wrong";

        return sameFile
            ? $"{name} cannot be used: it needs a PEM certificate and its private key, or choose the key file separately ({e.Message})"
            : $"{name} and {keyName} cannot be used together: {e.Message}";
    }

    private static string Existing(string path, string what)
    {
        var file = path.Trim();
        if (!File.Exists(file))
            throw new FormatException($"The {what} file {file} does not exist");
        return file;
    }
}
