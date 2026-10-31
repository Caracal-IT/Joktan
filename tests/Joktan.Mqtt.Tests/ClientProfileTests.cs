using System.IO;
using System.Linq;
using Joktan.Core;
using Joktan.Mqtt;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Mqtt.Tests;

public class ClientProfileTests
{
    [Fact]
    public void A_new_client_ID_is_short_and_different_each_time()
    {
        var first = ClientProfile.NewClientId();

        Assert.Equal(15, first.Length);
        Assert.StartsWith("joktan-", first);
        Assert.NotEqual(first, ClientProfile.NewClientId());
    }

    [Theory]
    [InlineData("main", null)]
    [InlineData(" Line 1_a-b.c ", null)]
    [InlineData("", "Enter a name for the client")]
    [InlineData("   ", "Enter a name for the client")]
    [InlineData("a/b", "The name may use letters, digits, spaces, _, - and .")]
    [InlineData("OTHER", "There is already a client called OTHER")]
    public void Checks_a_name(string name, string? expected)
        => Assert.Equal(expected, ClientProfile.CheckName(name, ["other"]));

    [Fact]
    public void A_name_may_be_as_long_as_forty_characters()
    {
        Assert.Null(ClientProfile.CheckName(new string('a', 40), []));
        Assert.Equal("The name must not be longer than 40 characters", ClientProfile.CheckName(new string('a', 41), []));
    }

    [Theory]
    [InlineData(false, "cert.pem", false)]
    [InlineData(true, "", false)]
    [InlineData(true, "  ", false)]
    [InlineData(true, "cert.pem", true)]
    public void A_client_certificate_is_only_used_with_TLS(bool useTls, string certificate, bool expected)
    {
        var profile = new ClientProfile { UseTls = useTls, ClientCertificate = certificate };

        Assert.Equal(expected, profile.UsesClientCertificate);
    }

    [Fact]
    public void A_clone_changes_separately()
    {
        var profile = new ClientProfile { Name = "a" };

        var clone = profile.Clone();
        clone.Name = "b";

        Assert.Equal("a", profile.Name);
    }
}
