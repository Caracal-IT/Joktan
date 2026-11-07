using System;
using Joktan.Agent.Rest;
using Xunit;

namespace Joktan.Agent.Tests.Rest;

public class ApiKeyTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Keeps_only_a_hash_of_the_secret()
    {
        var (key, secret) = ApiKey.Create("Build server", null, Now);

        Assert.StartsWith(ApiKey.KeyPrefix, secret);
        Assert.Equal(ApiKey.KeyPrefix.Length + 64, secret.Length);
        Assert.Equal(ApiKey.HashOf(secret), key.Hash);
        Assert.DoesNotContain(secret, key.Hash);
        Assert.StartsWith(key.Hint, secret);
        Assert.True(key.Hint.Length < 12);
        Assert.Equal("Build server", key.Name);
        Assert.Equal(Now, key.Created);
        Assert.True(key.Enabled);
    }

    [Fact]
    public void Every_key_is_different()
    {
        var (first, a) = ApiKey.Create("A", null, Now);
        var (second, b) = ApiKey.Create("B", null, Now);

        Assert.NotEqual(a, b);
        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public void Matches_its_own_hash_only()
    {
        var (key, secret) = ApiKey.Create("A", null, Now);

        Assert.True(key.Matches(ApiKey.HashOf(secret)));
        Assert.False(key.Matches(ApiKey.HashOf(secret + "x")));
        Assert.False(key.Matches(""));
    }

    [Fact]
    public void Works_to_the_end_of_its_last_day()
    {
        var (key, _) = ApiKey.Create("A", new DateOnly(2026, 11, 30), Now);

        Assert.Equal(ApiKeyState.Active, key.StateOn(new DateOnly(2026, 11, 30)));
        Assert.Equal(ApiKeyState.Expired, key.StateOn(new DateOnly(2026, 12, 1)));
    }

    [Fact]
    public void A_key_without_a_date_never_expires()
    {
        var (key, _) = ApiKey.Create("A", null, Now);

        Assert.Equal(ApiKeyState.Active, key.StateOn(DateOnly.MaxValue));
    }

    [Fact]
    public void A_key_turned_off_is_disabled_whatever_its_date()
    {
        var (key, _) = ApiKey.Create("A", new DateOnly(2026, 1, 1), Now);
        key.Enabled = false;

        Assert.Equal(ApiKeyState.Disabled, key.StateOn(new DateOnly(2027, 1, 1)));
    }
}
