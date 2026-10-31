using System;
using System.IO;
using System.Linq;
using Joktan.Certificates.ViewModels;
using Xunit;

namespace Joktan.Certificates.Tests.ViewModels;

public class CertificateNodeViewModelTests : CertificateTestBase
{
    private static CertificateNodeViewModel Node(StoredCertificate entry, StoredCertificate? issuer = null, params string[] usedBy) => new(entry, issuer, usedBy);

    [Fact]
    public void A_root_ca_signs_certificates_and_is_signed_by_itself()
    {
        var node = Node(Root());

        Assert.Equal("root", node.Name);
        Assert.Equal("Root CA", node.KindText);
        Assert.Equal("Itself", node.IssuerName);
        Assert.Equal("No MQTT client", node.UsedBy);
        Assert.True(node.IsAuthority);
        Assert.True(node.CanIssue);
        Assert.True(node.IsRoot);
        Assert.False(node.IsNotRoot);
        Assert.Equal("Signs certificates", node.Usage);
        Assert.Equal("None", node.AlternativeNames);
        Assert.Equal("ECDSA P-256", node.KeyText);
        Assert.Contains("CN=root", node.Subject);
        Assert.Contains("CN=root", node.IssuerSubject);
        Assert.NotEmpty(node.ValidFrom);
        Assert.NotEmpty(node.ValidUntil);
        Assert.NotEmpty(node.SerialNumber);
        Assert.Equal(64, node.Fingerprint.Length);
        Assert.StartsWith("Root CA · until ", node.Summary);
        Assert.Null(node.Problem);
        Assert.False(node.HasProblem);
        Assert.True(node.IsValid);
        Assert.False(node.IsExpired);
        Assert.False(node.IsExpiring);
        Assert.StartsWith("Valid for ", node.StatusText);
        Assert.True(node.IsExpanded);
    }

    [Fact]
    public void A_server_certificate_lists_its_names_and_who_uses_it()
    {
        var root = Root();
        var entry = Server(root);

        var node = Node(entry, root, "one", "two");

        Assert.Equal("root", node.IssuerName);
        Assert.Equal("one, two", node.UsedBy);
        Assert.Equal("TLS server authentication", node.Usage);
        Assert.Equal("localhost, 127.0.0.1", node.AlternativeNames);
        Assert.False(node.IsRoot);
        Assert.True(node.IsNotRoot);
        Assert.Equal(entry.Folder, node.Folder);
        Assert.Equal(entry.CertificatePath, node.CertificatePath);
        Assert.Equal(entry.KeyPath, node.KeyPath);
        Assert.Equal(entry.ChainPath, node.ChainPath);
        Assert.Equal(entry.AuthorityPath, node.AuthorityPath);
        Assert.Equal(["server"], node.SelfAndDescendants().Select(n => n.Name));
    }

    [Fact]
    public void A_client_certificate_without_its_ca_says_so()
    {
        var node = Node(Client(Root()));

        Assert.Equal("A CA that is no longer in the list", node.IssuerName);
        Assert.Equal("TLS client authentication", node.Usage);
        Assert.Equal("None", node.AlternativeNames);
    }

    [Fact]
    public void Says_how_long_it_is_valid()
    {
        var root = Root();
        var now = DateTimeOffset.Now;

        Assert.Equal("Expires today", Node(WithDates(root, now.AddDays(-1), now.AddHours(3))).StatusText);
        Assert.Equal("Expires tomorrow", Node(WithDates(root, now.AddDays(-1), now.AddHours(30))).StatusText);

        var soon = Node(WithDates(root, now.AddDays(-1), now.AddDays(10).AddHours(1)));
        Assert.Equal("Expires in 10 days", soon.StatusText);
        Assert.True(soon.IsExpiring);
        Assert.False(soon.IsValid);
    }

    [Fact]
    public void An_expired_certificate_says_when()
    {
        var now = DateTimeOffset.Now;

        var node = Node(WithDates(Root(), now.AddDays(-10), now.AddDays(-1)));

        Assert.True(node.IsExpired);
        Assert.False(node.IsExpiring);
        Assert.False(node.IsValid);
        Assert.False(node.CanIssue);
        Assert.StartsWith("Expired on ", node.StatusText);
    }

    [Fact]
    public void A_certificate_that_cannot_be_read_has_nothing_to_show()
    {
        var root = Root();
        File.WriteAllText(root.CertificatePath, "not a certificate");

        var node = Node(Reload(root.Id));

        Assert.True(node.HasProblem);
        Assert.NotNull(node.Problem);
        Assert.Equal("Cannot be read", node.StatusText);
        Assert.Equal("Root CA", node.Summary);
        Assert.Equal("", node.Subject);
        Assert.Equal("", node.IssuerSubject);
        Assert.Equal("", node.ValidFrom);
        Assert.Equal("", node.ValidUntil);
        Assert.Equal("", node.KeyText);
        Assert.Equal("", node.SerialNumber);
        Assert.Equal("", node.Fingerprint);
        Assert.Equal("None", node.AlternativeNames);
        Assert.True(node.IsExpired);
    }

    [Fact]
    public void Names_an_rsa_key()
    {
        var now = DateTimeOffset.Now;

        Assert.Equal("RSA 2048", Node(WithDates(Root(), now.AddDays(-1), now.AddDays(100), rsa: true)).KeyText);
    }
}
