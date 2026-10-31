using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography;
using System;
using Joktan.Certificates;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Certificates.Tests;

public class CertificateSpecTests
{
    private static CertificateSpec Spec(string commonName = "cn", string email = "", string country = "") => new()
    {
        CommonName = commonName,
        Email = email,
        Country = country,
    };

    [Theory]
    [InlineData("name", null)]
    [InlineData(" name ", null)]
    [InlineData("", "Enter a name for the certificate")]
    [InlineData("  ", "Enter a name for the certificate")]
    [InlineData("a\tb", "The name must not contain control characters")]
    [InlineData("TAKEN", "There is already a certificate called TAKEN")]
    public void Checks_a_name(string name, string? expected)
        => Assert.Equal(expected, CertificateSpec.CheckName(name, ["taken"]));

    [Fact]
    public void A_name_may_be_as_long_as_sixty_four_characters()
    {
        Assert.Null(CertificateSpec.CheckName(new string('a', 64), []));
        Assert.NotNull(CertificateSpec.CheckName(new string('a', 65), []));
    }

    [Fact]
    public void A_subject_needs_a_common_name_that_fits()
    {
        Assert.Null(Spec().CheckSubject());
        Assert.Null(Spec(new string('a', 64)).CheckSubject());
        Assert.Equal("Enter a common name (CN)", Spec("  ").CheckSubject());
        Assert.Equal("The common name must not be longer than 64 characters", Spec(new string('a', 65)).CheckSubject());
    }

    [Theory]
    [InlineData("me@example.com", null)]
    [InlineData("", null)]
    [InlineData("nobody", "The email address is not valid")]
    [InlineData("@example.com", "The email address is not valid")]
    [InlineData("a@b@c.com", "The email address is not valid")]
    [InlineData("a@-bad.com", "The email address is not valid")]
    public void Checks_the_email_address(string email, string? expected)
        => Assert.Equal(expected, Spec(email: email).CheckSubject());

    [Theory]
    [InlineData("ZA", null)]
    [InlineData("de", null)]
    [InlineData(" US ", null)]
    [InlineData("", null)]
    [InlineData("ZAF", "The country must be a two-letter code, such as ZA, DE or US")]
    [InlineData("Z1", "The country must be a two-letter code, such as ZA, DE or US")]
    public void Checks_the_country(string country, string? expected)
        => Assert.Equal(expected, CertificateSpec.CheckFields("", "", "", "", country));

    [Theory]
    [InlineData(0, "The organisation must not be longer than 128 characters")]
    [InlineData(1, "The organisational unit must not be longer than 128 characters")]
    [InlineData(2, "The locality must not be longer than 128 characters")]
    [InlineData(3, "The state must not be longer than 128 characters")]
    public void Checks_the_length_of_each_part(int part, string expected)
    {
        var fields = new[] { "", "", "", "" };
        Assert.Null(CertificateSpec.CheckFields(fields[0], fields[1], fields[2], fields[3], ""));

        fields[part] = new string('a', 129);

        Assert.Equal(expected, CertificateSpec.CheckFields(fields[0], fields[1], fields[2], fields[3], ""));
    }

    [Fact]
    public void Splits_alternative_names_by_what_they_are()
    {
        var problem = CertificateSpec.ParseAlternativeNames("Example.COM, 127.0.0.1;::1\nme@example.com\t*.example.com", out var names);

        Assert.Null(problem);
        Assert.Equal(
            [
                new AlternativeName(AlternativeNameType.Dns, "example.com"),
                new AlternativeName(AlternativeNameType.IpAddress, "127.0.0.1"),
                new AlternativeName(AlternativeNameType.IpAddress, "::1"),
                new AlternativeName(AlternativeNameType.Email, "me@example.com"),
                new AlternativeName(AlternativeNameType.Dns, "*.example.com"),
            ],
            names);
    }

    [Fact]
    public void Repeated_alternative_names_count_once()
    {
        CertificateSpec.ParseAlternativeNames("a.com A.COM a.com", out var names);

        Assert.Single(names);
    }

    [Fact]
    public void Numbers_short_of_an_IP_address_count_as_a_host_name()
    {
        CertificateSpec.ParseAlternativeNames("1.2.3", out var names);

        Assert.Equal(AlternativeNameType.Dns, Assert.Single(names).Type);
    }

    [Fact]
    public void No_alternative_names_is_fine()
    {
        Assert.Null(CertificateSpec.ParseAlternativeNames(" , ", out var names));
        Assert.Empty(names);
    }

    [Theory]
    [InlineData("-bad.com", "-bad.com is not a host name, IP address or email address")]
    [InlineData("bad-.com", "bad-.com is not a host name, IP address or email address")]
    [InlineData("a..b", "a..b is not a host name, IP address or email address")]
    [InlineData("*.com", "*.com is not a host name, IP address or email address")]
    [InlineData("a.*.com", "a.*.com is not a host name, IP address or email address")]
    [InlineData("a!b", "a!b is not a host name, IP address or email address")]
    [InlineData("a@b@c", "a@b@c is not a valid email address")]
    public void Says_which_alternative_name_is_wrong(string text, string expected)
        => Assert.Equal(expected, CertificateSpec.ParseAlternativeNames(text, out _));

    [Fact]
    public void Limits_host_names_and_their_labels()
    {
        Assert.Null(CertificateSpec.ParseAlternativeNames(new string('a', 63), out _));
        Assert.NotNull(CertificateSpec.ParseAlternativeNames(new string('a', 64), out _));
        Assert.NotNull(CertificateSpec.ParseAlternativeNames(string.Join('.', Enumerable.Repeat(new string('a', 60), 5)), out _));
    }

    [Fact]
    public void Alternative_names_read_well()
    {
        Assert.Equal("a.com", new AlternativeName(AlternativeNameType.Dns, "a.com").ToString());
        Assert.Equal("IP ::1", new AlternativeName(AlternativeNameType.IpAddress, "::1").ToString());
        Assert.Equal("email a@b", new AlternativeName(AlternativeNameType.Email, "a@b").ToString());
    }
}
