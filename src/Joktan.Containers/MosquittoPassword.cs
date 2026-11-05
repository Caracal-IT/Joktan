using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Joktan.Containers;

/// <summary>
/// Lines of a Mosquitto password file, as <c>mosquitto_passwd</c> writes them:
/// <c>user:$7$1000$salt$hash</c>, the hash being PBKDF2 with HMAC-SHA512 over the password, in base 64.
/// The password itself is never written.
/// </summary>
public static class MosquittoPassword
{
    private const int Iterations = 1000;
    private const int SaltLength = 64;
    private const int HashLength = 64;

    /// <summary>A new line for the file, with a salt of its own.</summary>
    public static string Line(string username, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        return $"{username}:$7${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(Hash(password, salt, Iterations))}";
    }

    /// <summary>Whether a line of the file is for this user and password, so it can be kept as it is.</summary>
    public static bool Matches(string line, string username, string password)
    {
        var parts = line.Trim().Split('$');
        if (parts.Length != 5 || parts[0] != username + ":" || parts[1] != "7"
            || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var iterations) || iterations < 1)
            return false;

        try
        {
            var salt = Convert.FromBase64String(parts[3]);
            var expected = Convert.FromBase64String(parts[4]);
            return CryptographicOperations.FixedTimeEquals(Hash(password, salt, iterations, expected.Length), expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static byte[] Hash(string password, byte[] salt, int iterations, int length = HashLength)
        => Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA512, length);
}
