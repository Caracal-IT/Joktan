using Xunit;

namespace Joktan.Users.Tests;

public class PasswordHasherTests
{
    [Fact]
    public void A_password_is_kept_as_a_salted_hash_that_only_it_matches()
    {
        var one = PasswordHasher.Hash("correct horse");
        var two = PasswordHasher.Hash("correct horse");
        Assert.NotEqual(one, two);
        Assert.DoesNotContain("correct", one);
        Assert.StartsWith($"pbkdf2-sha256${PasswordHasher.Iterations}$", one);
        Assert.True(PasswordHasher.Verify("correct horse", one));
        Assert.True(PasswordHasher.Verify("correct horse", two));
        Assert.False(PasswordHasher.Verify("Correct horse", one));
        Assert.False(PasswordHasher.Verify("x", ""));
        Assert.False(PasswordHasher.Verify("x", "pbkdf2-sha256$1$not base64$!"));
        Assert.False(PasswordHasher.Verify("x", "md5$1$a$b"));
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("ann@example.com", true)]
    [InlineData("ann@", false)]
    [InlineData("@example.com", false)]
    [InlineData("ann@example", false)]
    [InlineData("a b@example.com", false)]
    [InlineData("ann@@example.com", false)]
    public void Email_addresses_are_checked_and_may_be_left_out(string email, bool valid)
        => Assert.Equal(valid || email.Length == 0, Model.User.CheckEmail(email) is null);
}
