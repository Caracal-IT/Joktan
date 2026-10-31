using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography;
using System;
using Joktan.Certificates;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Certificates.Tests;

public class CertificateDefaultsTests
{
    [Fact]
    public void Common_names_and_validity_follow_the_kind()
    {
        var defaults = new CertificateDefaults
        {
            RootCommonName = "root",
            IntermediateCommonName = "intermediate",
            ServerCommonName = "server",
            ClientCommonName = "client",
            AuthorityValidityDays = 100,
            CertificateValidityDays = 10,
        };

        Assert.Equal("root", defaults.CommonNameFor(CertificateKind.RootAuthority));
        Assert.Equal("intermediate", defaults.CommonNameFor(CertificateKind.IntermediateAuthority));
        Assert.Equal("server", defaults.CommonNameFor(CertificateKind.Server));
        Assert.Equal("client", defaults.CommonNameFor(CertificateKind.Client));
        Assert.Equal(100, defaults.ValidityDaysFor(CertificateKind.RootAuthority));
        Assert.Equal(100, defaults.ValidityDaysFor(CertificateKind.IntermediateAuthority));
        Assert.Equal(10, defaults.ValidityDaysFor(CertificateKind.Server));
        Assert.Equal(10, defaults.ValidityDaysFor(CertificateKind.Client));
    }

    [Fact]
    public void Certificates_last_no_longer_than_Apple_allows_by_default()
    {
        Assert.True(new CertificateDefaults().CertificateValidityDays <= 825);
    }

    [Fact]
    public void A_clone_changes_separately()
    {
        var defaults = new CertificateDefaults { Organization = "a" };

        defaults.Clone().Organization = "b";

        Assert.Equal("a", defaults.Organization);
    }
}
