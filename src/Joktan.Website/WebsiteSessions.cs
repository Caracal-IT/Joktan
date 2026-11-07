using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;

namespace Joktan.Website;

/// <summary>
/// Who is signed in to the website, by the token their browser or program holds, and the usernames that
/// have been given the wrong password too often of late. Kept in memory only, so everyone signs in again
/// when Joktan starts.
/// </summary>
public sealed class WebsiteSessions(Func<DateTime>? now = null)
{
    /// <summary>How many wrong passwords in <see cref="FailureWindow"/> stop a username from signing in for a while.</summary>
    public const int MaxFailures = 5;

    public static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(15);

    private readonly Func<DateTime> _now = now ?? (() => DateTime.UtcNow);
    private readonly Dictionary<string, Session> _sessions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<DateTime>> _failures = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    private sealed class Session(int userId, DateTime expires)
    {
        public int UserId { get; } = userId;

        public DateTime Expires { get; set; } = expires;
    }

    /// <summary>Signs a user in, giving the token that stands for them and when it runs out if not used.</summary>
    public (string Token, DateTime Expires) Start(int userId, TimeSpan length)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var expires = _now() + length;
        lock (_gate)
        {
            Prune();
            _sessions[token] = new Session(userId, expires);
        }

        return (token, expires);
    }

    /// <summary>The user a token stands for, keeping it for another <paramref name="length"/>; null when it is not known or ran out.</summary>
    public int? UserOf(string token, TimeSpan length)
    {
        lock (_gate)
        {
            if (!_sessions.TryGetValue(token, out var session))
                return null;
            if (session.Expires <= _now())
            {
                _sessions.Remove(token);
                return null;
            }

            session.Expires = _now() + length;
            return session.UserId;
        }
    }

    public void End(string token)
    {
        lock (_gate)
            _sessions.Remove(token);
    }

    /// <summary>How many are signed in.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                Prune();
                return _sessions.Count;
            }
        }
    }

    /// <summary>How long a username must wait before trying again, after too many wrong passwords; null when it need not.</summary>
    public TimeSpan? LockedOut(string username)
    {
        lock (_gate)
        {
            if (!_failures.TryGetValue(username.Trim(), out var times))
                return null;
            times.RemoveAll(t => t <= _now() - FailureWindow);
            return times.Count >= MaxFailures ? times[0] + FailureWindow - _now() : null;
        }
    }

    public void Failed(string username)
    {
        lock (_gate)
        {
            if (!_failures.TryGetValue(username.Trim(), out var times))
                _failures[username.Trim()] = times = [];
            times.Add(_now());
        }
    }

    public void Succeeded(string username)
    {
        lock (_gate)
            _failures.Remove(username.Trim());
    }

    private void Prune()
    {
        foreach (var token in _sessions.Where(s => s.Value.Expires <= _now()).Select(s => s.Key).ToList())
            _sessions.Remove(token);
    }
}
