using Xunit;

namespace Joktan.Certificates.Tests;

public class CertificateKindTests
{
    [Theory]
    [InlineData(CertificateKind.RootAuthority, true, "Root CA")]
    [InlineData(CertificateKind.IntermediateAuthority, true, "Intermediate CA")]
    [InlineData(CertificateKind.Server, false, "Server")]
    [InlineData(CertificateKind.Client, false, "Client")]
    public void Names_each_kind(CertificateKind kind, bool isAuthority, string text)
    {
        Assert.Equal(isAuthority, kind.IsAuthority());
        Assert.Equal(text, kind.Text());
    }

    [Theory]
    [InlineData(KeyAlgorithm.EcdsaP256, "ECDSA P-256")]
    [InlineData(KeyAlgorithm.EcdsaP384, "ECDSA P-384")]
    [InlineData(KeyAlgorithm.Rsa2048, "RSA 2048")]
    [InlineData(KeyAlgorithm.Rsa4096, "RSA 4096")]
    public void Names_each_key_algorithm(KeyAlgorithm algorithm, string text)
        => Assert.Equal(text, algorithm.Text());
}

public class CertificateKindUnknownValueTests
{
    [Fact]
    public void A_value_with_no_text_shows_its_number()
    {
        Assert.Equal("99", ((CertificateKind)99).Text());
        Assert.Equal("99", ((KeyAlgorithm)99).Text());
    }
}
