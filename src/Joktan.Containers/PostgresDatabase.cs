using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using Joktan.Containers.Resources;

namespace Joktan.Containers;

/// <summary>
/// A PostgreSQL server made on Data / Database and written into Joktan's own compose file for
/// databases, as a service of that name. Its data is kept in a folder on this computer, mounted into
/// the container, not in the container. Kept in containers.json; its password is in the credential
/// store, under <see cref="Account"/>.
/// </summary>
public sealed class PostgresDatabase
{
    /// <summary>The port inside the container, which the one chosen is published on.</summary>
    public const int ContainerPort = 5432;

    public const int MaxNameLength = 40;

    /// <summary>PostgreSQL's longest name for a user or database.</summary>
    public const int MaxIdentifierLength = 63;

    public const string DefaultUsername = "postgres";

    public string Id { get; set; } = NewId();

    /// <summary>The compose service, and the folder its environment file is written to.</summary>
    public string Name { get; set; } = "";

    /// <summary>The port on the host.</summary>
    public int Port { get; set; } = ContainerPort;

    /// <summary>The superuser made when the data folder is first set up.</summary>
    public string Username { get; set; } = DefaultUsername;

    /// <summary>The database made when the data folder is first set up.</summary>
    public string Database { get; set; } = "";

    /// <summary>The folder on this computer that holds the database's files.</summary>
    public string DataFolder { get; set; } = "";

    /// <summary>What its password is filed under in the credential store.</summary>
    [JsonIgnore]
    public string Account => $"postgres:{Id}";

    /// <summary>How to connect from this computer, without the password, such as "postgres://postgres@localhost:5432/app".</summary>
    [JsonIgnore]
    public string ConnectionText => $"postgres://{Username}@localhost:{Port}/{Database}";

    public PostgresDatabase Clone() => (PostgresDatabase)MemberwiseClone();

    public static string NewId() => Guid.NewGuid().ToString("N")[..12];

    /// <summary>The database a new server makes, from its name: dashes become underscores.</summary>
    public static string DatabaseFor(string name) => name.Trim().Replace('-', '_');

    /// <summary>
    /// What is wrong with a database's name, or null. It becomes a compose service and a folder, so
    /// only lower-case letters, digits, dashes and underscores will do.
    /// </summary>
    public static string? CheckName(string name, IEnumerable<string> others)
    {
        name = name.Trim();
        if (name.Length == 0)
            return Strings.PostgresDatabase_EnterAName;
        if (name.Length > MaxNameLength)
            return string.Format(Strings.PostgresDatabase_TheNameCanBeAtMost, MaxNameLength);
        if ((!char.IsAsciiLetterLower(name[0]) && !char.IsAsciiDigit(name[0]))
            || name.Any(c => !char.IsAsciiLetterLower(c) && !char.IsAsciiDigit(c) && c is not ('-' or '_')))
            return Strings.PostgresDatabase_UseLowerCaseLettersDigits;
        if (others.Any(o => string.Equals(o, name, StringComparison.Ordinal)))
            return string.Format(Strings.PostgresDatabase_ADatabaseIsAlreadyCalled, name);
        return null;
    }

    /// <summary>
    /// What is wrong with a user or database name, or null. Lower-case letters, digits and
    /// underscores, starting with a letter or underscore, need no quoting in SQL.
    /// </summary>
    /// <param name="what">"username" or "database", for the message.</param>
    public static string? CheckIdentifier(string name, string what)
    {
        name = name.Trim();
        if (name.Length == 0)
            return string.Format(Strings.PostgresDatabase_EnterA, what);
        if (name.Length > MaxIdentifierLength)
            return string.Format(Strings.PostgresDatabase_CanBeAtMost, what, MaxIdentifierLength);
        if ((!char.IsAsciiLetterLower(name[0]) && name[0] != '_')
            || name.Any(c => !char.IsAsciiLetterLower(c) && !char.IsAsciiDigit(c) && c != '_'))
            return string.Format(Strings.PostgresDatabase_UseLowerCaseLettersDigitsAndUnderscores, what);
        return null;
    }

    /// <summary>What is wrong with a password, or null. It goes into an environment file, one line to a setting.</summary>
    public static string? CheckPassword(string password)
        => password.Any(char.IsControl) ? Strings.PostgresDatabase_ThePasswordCannotHaveLineBreaks : null;

    /// <summary>What is wrong with a data folder, or null.</summary>
    public static string? CheckDataFolder(string folder)
    {
        folder = folder.Trim();
        if (folder.Length == 0)
            return Strings.PostgresDatabase_ChooseAFolderForTheData;
        if (folder.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || !Path.IsPathFullyQualified(folder))
            return Strings.PostgresDatabase_EnterTheDataFolderSFullPath;
        if (File.Exists(folder))
            return string.Format(Strings.PostgresDatabase_IsAFileNotAFolder, folder);
        return null;
    }

    /// <summary>
    /// What stops the database from being written, or null: a port or data folder another database
    /// has, a name that is not allowed, or no password.
    /// </summary>
    public string? Check(IEnumerable<PostgresDatabase> others, bool hasPassword)
    {
        if ((CheckIdentifier(Username, Strings.PostgresDatabase_Username) ?? CheckIdentifier(Database, Strings.PostgresDatabase_Database)
                ?? CheckDataFolder(DataFolder)) is { } problem)
            return problem;
        if (Port is < 1 or > 65535)
            return string.Format(Strings.PostgresDatabase_IsNotAPort, Port);
        foreach (var other in others.Where(o => o.Id != Id))
        {
            if (other.Port == Port)
                return string.Format(Strings.PostgresDatabase_PortIsUsedBy, Port, other.Name);
            if (SameFolder(other.DataFolder, DataFolder))
                return string.Format(Strings.PostgresDatabase_TheDataFolderIsUsedBy, other.Name);
        }

        return hasPassword ? null : Strings.PostgresDatabase_EnterAPassword;
    }

    internal static bool SameFolder(string a, string b)
        => string.Equals(Path.GetFullPath(a.Trim()).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(b.Trim()).TrimEnd(Path.DirectorySeparatorChar),
            OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
}
