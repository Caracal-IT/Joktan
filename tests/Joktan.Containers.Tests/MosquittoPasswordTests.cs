using Xunit;

namespace Joktan.Containers.Tests;

public class MosquittoPasswordTests
{
    // Written by mosquitto_passwd 2.1.2 for alice and "secret".
    private const string FromMosquitto =
        "alice:$7$1000$pg2JFi20LaOIYlirXmgT74xcNWj6sPMd85n9eLKQU7Ykw+0F8lY0WxXn5DU+nenOeX8nqUcH+udzyIaSeBIO1g==$aK8UKFFNuRbA3kd/dceWaELcJByKrXaDEryhRFlJDMFySIalXROFqDvvgfcbUe9aMIVw77pe9nMqCRjZePpZbg==";

    [Fact]
    public void Reads_a_line_mosquitto_passwd_wrote()
    {
        Assert.True(MosquittoPassword.Matches(FromMosquitto, "alice", "secret"));
        Assert.True(MosquittoPassword.Matches(FromMosquitto + "\n", "alice", "secret"));
        Assert.False(MosquittoPassword.Matches(FromMosquitto, "alice", "Secret"));
        Assert.False(MosquittoPassword.Matches(FromMosquitto, "bob", "secret"));
    }

    [Fact]
    public void Writes_lines_in_the_same_form_with_a_salt_of_their_own()
    {
        var line = MosquittoPassword.Line("alice", "secret");

        Assert.StartsWith("alice:$7$1000$", line);
        Assert.True(MosquittoPassword.Matches(line, "alice", "secret"));
        Assert.NotEqual(line, MosquittoPassword.Line("alice", "secret"));
        Assert.DoesNotContain("secret", line);
    }

    [Theory]
    [InlineData("")]
    [InlineData("alice:secret")]
    [InlineData("alice:$6$1000$abc$def")]
    [InlineData("alice:$7$many$abc$def")]
    [InlineData("alice:$7$1000$not base 64$def")]
    [InlineData("alice:$7$0$abc$def")]
    public void Anything_else_does_not_match(string line)
    {
        Assert.False(MosquittoPassword.Matches(line, "alice", "secret"));
    }
}
