using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using CommunityToolkit.Mvvm.ComponentModel;
using Joktan.Certificates;

namespace Joktan.ViewModels;

/// <summary>One certificate in the tree, below the CA that signed it.</summary>
public partial class CertificateNodeViewModel : ViewModelBase
{
    /// <summary>A certificate this close to its expiry date is shown as expiring.</summary>
    public const int ExpiringDays = 30;

    public CertificateNodeViewModel(StoredCertificate entry, StoredCertificate? issuer, IReadOnlyList<string> usedBy)
    {
        Entry = entry;
        IssuerName = issuer?.DisplayName ?? (entry.Kind == CertificateKind.RootAuthority ? "Itself" : "A CA that is no longer in the list");
        UsedBy = usedBy.Count == 0 ? "No MQTT client" : string.Join(", ", usedBy);
    }

    public StoredCertificate Entry { get; }

    public ObservableCollection<CertificateNodeViewModel> Children { get; } = [];

    [ObservableProperty]
    public partial bool IsExpanded { get; set; } = true;

    public string Name => Entry.DisplayName;

    public string KindText => Entry.Kind.Text();

    public bool IsAuthority => Entry.Kind.IsAuthority();

    public bool CanIssue => Entry.CanIssue;

    public string IssuerName { get; }

    /// <summary>The MQTT clients that use one of its files, by name.</summary>
    public string UsedBy { get; }

    private X509Certificate2? Certificate => Entry.Certificate;

    public string Subject => Certificate?.Subject ?? "";

    public string IssuerSubject => Certificate?.Issuer ?? "";

    public string ValidFrom => Certificate is null ? "" : $"{Certificate.NotBefore:yyyy-MM-dd HH:mm}";

    public string ValidUntil => Certificate is null ? "" : $"{Certificate.NotAfter:yyyy-MM-dd HH:mm}";

    public bool IsExpired => Certificate is null || Certificate.NotAfter <= DateTime.Now;

    public bool IsExpiring => !IsExpired && Certificate!.NotAfter <= DateTime.Now.AddDays(ExpiringDays);

    public bool IsValid => !IsExpired && !IsExpiring && Entry.Problem is null;

    public bool HasProblem => Entry.Problem is not null || Certificate is null;

    public string StatusText
    {
        get
        {
            if (Certificate is null)
                return "Cannot be read";
            if (IsExpired)
                return $"Expired on {Certificate.NotAfter:yyyy-MM-dd}";
            var days = (int)Math.Floor((Certificate.NotAfter - DateTime.Now).TotalDays);
            return days switch
            {
                0 => "Expires today",
                1 => "Expires tomorrow",
                _ when IsExpiring => $"Expires in {days} days",
                _ => $"Valid for {days} more days",
            };
        }
    }

    /// <summary>A short line beside the name in the tree.</summary>
    public string Summary => Certificate is null ? KindText : $"{KindText} · until {Certificate.NotAfter:yyyy-MM-dd}";

    public string? Problem => Entry.Problem;

    public string KeyText
    {
        get
        {
            if (Certificate is null)
                return "";
            if (Certificate.GetECDsaPublicKey() is { } ec)
            {
                using (ec)
                    return $"ECDSA P-{ec.KeySize}";
            }

            if (Certificate.GetRSAPublicKey() is { } rsa)
            {
                using (rsa)
                    return $"RSA {rsa.KeySize}";
            }

            return Certificate.PublicKey.Oid.FriendlyName ?? "";
        }
    }

    public string AlternativeNames
    {
        get
        {
            var extension = Certificate?.Extensions.OfType<X509SubjectAlternativeNameExtension>().FirstOrDefault();
            if (extension is null)
                return "None";

            var names = extension.EnumerateDnsNames().Concat(extension.EnumerateIPAddresses().Select(a => a.ToString())).ToList();
            return names.Count == 0 ? "None" : string.Join(", ", names);
        }
    }

    public string Usage => Entry.Kind switch
    {
        CertificateKind.RootAuthority or CertificateKind.IntermediateAuthority => "Signs certificates",
        CertificateKind.Server => "TLS server authentication",
        _ => "TLS client authentication",
    };

    public string SerialNumber => Certificate?.SerialNumber ?? "";

    public string Fingerprint => Certificate is null ? "" : Convert.ToHexString(Certificate.GetCertHash(HashAlgorithmName.SHA256));

    public string Folder => Entry.Folder;

    public string CertificatePath => Entry.CertificatePath;

    public string KeyPath => Entry.KeyPath;

    public string ChainPath => Entry.ChainPath;

    public string AuthorityPath => Entry.AuthorityPath;

    public bool IsRoot => Entry.Kind == CertificateKind.RootAuthority;

    public bool IsNotRoot => !IsRoot;

    /// <summary>Every node from this one down.</summary>
    public IEnumerable<CertificateNodeViewModel> SelfAndDescendants()
        => Children.SelectMany(c => c.SelfAndDescendants()).Prepend(this);
}
