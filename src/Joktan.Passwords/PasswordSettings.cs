using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Joktan.Core;

namespace Joktan.Passwords;

/// <summary>Which storage the password manager uses, and when a vault locks itself.</summary>
public sealed class PasswordSettings
{
    /// <summary>Null until the user has chosen.</summary>
    public PasswordStorageKind? Storage { get; set; }

    /// <summary>The vault file; empty for the default one in the data folder.</summary>
    public string VaultPath { get; set; } = "";

    /// <summary>Minutes without use after which an open vault locks; 0 never.</summary>
    public int LockMinutes { get; set; } = 5;

    public string EffectiveVaultPath => VaultPath.Trim() is { Length: > 0 } path ? path : VaultStorage.DefaultPath;

    private static string FilePath => Path.Combine(DataFolder.Path, "passwords-settings.json");

    public static PasswordSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<PasswordSettings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // Unreadable settings ask for the storage again; the entries themselves are not touched.
        }

        return new();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(DataFolder.Path);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonFormats.Indented));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Settings that cannot be saved are simply not remembered.
        }
    }
}

/// <summary>Makes random passwords.</summary>
public static class PasswordGenerator
{
    private const string Lower = "abcdefghijkmnopqrstuvwxyz";
    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Digits = "23456789";
    private const string Symbols = "!#$%&*+-=?@^_~";

    public const int MinimumLength = 8;
    public const int MaximumLength = 128;

    /// <summary>
    /// A password with at least one lower-case letter, upper-case letter and digit, and a symbol
    /// if wanted. Letters and digits that are easy to mix up (l, I, O, 0, 1) are left out.
    /// </summary>
    public static string Generate(int length, bool symbols)
    {
        length = Math.Clamp(length, MinimumLength, MaximumLength);
        string[] sets = symbols ? [Lower, Upper, Digits, Symbols] : [Lower, Upper, Digits];
        var all = string.Concat(sets);

        var characters = new char[length];
        for (var i = 0; i < length; i++)
            characters[i] = i < sets.Length ? Pick(sets[i]) : Pick(all);

        // The required characters were put first; shuffle them in.
        for (var i = length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (characters[i], characters[j]) = (characters[j], characters[i]);
        }

        return new string(characters);

        static char Pick(string set) => set[RandomNumberGenerator.GetInt32(set.Length)];
    }
}
