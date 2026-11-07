using System.Linq;
using System.Text.Json.Serialization;

namespace Joktan.Users.Model;

/// <summary>A person, with the role they have. Kept in the users database.</summary>
public sealed class User
{
    /// <summary>The database's id; 0 for one not saved yet.</summary>
    [JsonPropertyName("id")]
    public int Id { get; set; }

    /// <summary>What they sign in with: unique, ignoring case.</summary>
    [JsonPropertyName("username")]
    public string Username { get; set; } = "";

    [JsonPropertyName("email")]
    public string Email { get; set; } = "";

    /// <summary>Whether a password is set. The hash itself is never read back out of the database.</summary>
    [JsonPropertyName("has_password")]
    public bool HasPassword { get; set; }

    /// <summary>A password to set when saved, hashed then; null keeps the one there is.</summary>
    [JsonIgnore]
    public string? NewPassword { get; set; }

    [JsonPropertyName("first_name")]
    public string FirstName { get; set; } = "";

    [JsonPropertyName("last_name")]
    public string LastName { get; set; } = "";

    /// <summary>Who they are, in a line, such as "Head of finance".</summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("notes")]
    public string Notes { get; set; } = "";

    /// <summary>The id of their role.</summary>
    [JsonPropertyName("role_id")]
    public int RoleId { get; set; }

    public const int MaxUsernameLength = 64;

    /// <summary>What is wrong with a username, or null: letters, digits and . _ - @, up to 64.</summary>
    public static string? CheckUsername(string username)
    {
        username = username.Trim();
        if (username.Length == 0)
            return Resources.Strings.Directory_EnterAUsername;
        if (username.Length > MaxUsernameLength)
            return string.Format(Resources.Strings.Directory_UsernameTooLong, MaxUsernameLength);
        return username.All(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-' or '@') ? null : Resources.Strings.Directory_UsernameCharacters;
    }

    /// <summary>What is wrong with an email address, or null; none is fine.</summary>
    public static string? CheckEmail(string email)
    {
        email = email.Trim();
        if (email.Length == 0)
            return null;
        var at = email.IndexOf('@');
        return at > 0 && at == email.LastIndexOf('@') && email.IndexOf('.', at) > at + 1 && !email.EndsWith('.') && !email.Any(char.IsWhiteSpace)
            ? null
            : Resources.Strings.Directory_EmailNotValid;
    }

    [JsonIgnore]
    public string FullName => $"{FirstName.Trim()} {LastName.Trim()}".Trim();

    public User Clone() => (User)MemberwiseClone();
}
