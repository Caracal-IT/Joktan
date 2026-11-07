using System;
using Xunit;

namespace Joktan.Website.Tests;

public class WebsiteSessionsTests
{
    private DateTime _now = new(2030, 1, 1, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_session_lasts_while_it_is_used_and_ends_when_it_is_not()
    {
        var sessions = new WebsiteSessions(() => _now);
        var length = TimeSpan.FromHours(1);
        var (token, expires) = sessions.Start(7, length);
        Assert.Equal(_now + length, expires);
        Assert.NotEqual(token, sessions.Start(7, length).Token);

        _now += TimeSpan.FromMinutes(50);
        Assert.Equal(7, sessions.UserOf(token, length));
        _now += TimeSpan.FromMinutes(50);
        Assert.Equal(7, sessions.UserOf(token, length));
        _now += TimeSpan.FromMinutes(61);
        Assert.Null(sessions.UserOf(token, length));
        Assert.Equal(0, sessions.Count);

        var (other, _) = sessions.Start(8, length);
        sessions.End(other);
        Assert.Null(sessions.UserOf(other, length));
    }

    [Fact]
    public void Too_many_wrong_passwords_lock_a_username_out_until_the_window_passes()
    {
        var sessions = new WebsiteSessions(() => _now);
        for (var i = 0; i < WebsiteSessions.MaxFailures - 1; i++)
            sessions.Failed("Ann");
        Assert.Null(sessions.LockedOut("ann"));
        sessions.Failed("ann");
        Assert.Equal(WebsiteSessions.FailureWindow, sessions.LockedOut("ANN"));

        _now += WebsiteSessions.FailureWindow;
        Assert.Null(sessions.LockedOut("ann"));

        sessions.Failed("bob");
        sessions.Succeeded("bob");
        Assert.Null(sessions.LockedOut("bob"));
    }
}
