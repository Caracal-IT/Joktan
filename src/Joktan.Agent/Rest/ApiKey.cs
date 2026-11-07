using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace Joktan.Agent.Rest;

/// <summary>Whether a key lets its holder in, and if not, why.</summary>
public enum ApiKeyState
{
    Active,
    Disabled,
    Expired,
}

/// <summary>
/// One key for the REST API. Only a hash of the key is kept, so the key itself is shown once, when it
/// is made, and cannot be read back from the settings.
/// </summary>
public sealed class ApiKey
{
    public const string KeyPrefix = "jok_";

    /// <summary>The characters of the key shown in lists, after <see cref="KeyPrefix"/>.</summary>
    private const int HintLength = 6;

    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>The start of the key, so that the user can tell which key a client holds.</summary>
    public string Hint { get; set; } = "";

    /// <summary>The SHA-256 of the key, as lowercase hex.</summary>
    public string Hash { get; set; } = "";

    public DateTimeOffset Created { get; set; }

    /// <summary>The last day the key works on, in the computer's own time zone; null for a key that never expires.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateOnly? Expires { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>Makes a key, returning it with the secret its holder sends.</summary>
    public static (ApiKey Key, string Secret) Create(string name, DateOnly? expires, DateTimeOffset now)
    {
        var secret = KeyPrefix + Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var key = new ApiKey
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            Hint = secret[..(KeyPrefix.Length + HintLength)],
            Hash = HashOf(secret),
            Created = now,
            Expires = expires,
        };
        return (key, secret);
    }

    public static string HashOf(string secret) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret))).ToLowerInvariant();

    /// <summary>Whether this is the key whose hash is given, compared in constant time.</summary>
    public bool Matches(string hash)
        => CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Hash), Encoding.ASCII.GetBytes(hash));

    /// <param name="today">The date in the computer's own time zone.</param>
    public ApiKeyState StateOn(DateOnly today)
        => !Enabled ? ApiKeyState.Disabled
            : Expires is { } last && today > last ? ApiKeyState.Expired
            : ApiKeyState.Active;
}
