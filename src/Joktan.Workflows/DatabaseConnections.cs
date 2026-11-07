using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Joktan.Core;
using Joktan.Workflows.Resources;

namespace Joktan.Workflows;

/// <summary>A PostgreSQL database that workflows reach, made on Settings / Workflow Settings or from a database step.</summary>
public sealed class DatabaseConnectionSettings
{
    public const int DefaultPort = 5432;

    public string Id { get; set; } = NewId();

    /// <summary>What it is called in the lists.</summary>
    public string Name { get; set; } = "";

    public string Host { get; set; } = "localhost";

    public int Port { get; set; } = DefaultPort;

    public string Username { get; set; } = "postgres";

    public string Database { get; set; } = "postgres";

    /// <summary>What its password is filed under in the credential store.</summary>
    [JsonIgnore]
    public string Account => $"database:{Id}";

    /// <summary>How to connect, without the password, such as "postgres://postgres@localhost:5432/app".</summary>
    [JsonIgnore]
    public string ConnectionText => $"postgres://{Username}@{Host}:{Port}/{Database}";

    public DatabaseConnectionSettings Clone() => (DatabaseConnectionSettings)MemberwiseClone();

    public static string NewId() => "c" + Guid.NewGuid().ToString("N")[..11];
}

/// <summary>The database connections of <see cref="WorkflowSettings"/>, with their passwords kept in the credential store, never in the settings.</summary>
public sealed class DatabaseConnections(WorkflowSettings settings, CredentialStore credentials)
{
    public IReadOnlyList<DatabaseConnectionSettings> All => settings.DatabaseConnections;

    /// <summary>Raised when a connection is added, changed or removed.</summary>
    public event Action? Changed;

    public DatabaseConnectionSettings? Find(string id) => settings.DatabaseConnections.FirstOrDefault(c => c.Id == id);

    public string? PasswordOf(DatabaseConnectionSettings connection) => credentials.Read(connection.Account);

    /// <summary>Adds a connection, or changes the one with its id, and keeps its password unless that is null.</summary>
    /// <returns>Why the password could not be kept, or null.</returns>
    public string? Save(DatabaseConnectionSettings connection, string? password)
    {
        var saved = connection.Clone();
        var index = settings.DatabaseConnections.FindIndex(c => c.Id == saved.Id);
        if (index >= 0)
            settings.DatabaseConnections[index] = saved;
        else
            settings.DatabaseConnections.Add(saved);
        settings.Save();
        string? problem = null;
        if (password is not null && !credentials.Write(saved.Account, password))
            problem = credentials.IsAvailable ? Strings.Connection_PasswordNotKept : Strings.Connection_NoCredentialStore;
        Changed?.Invoke();
        return problem;
    }

    /// <summary>Removes a connection and its password. Steps that use it fail until another is chosen.</summary>
    public void Remove(string id)
    {
        if (Find(id) is not { } connection)
            return;
        settings.DatabaseConnections.Remove(connection);
        settings.Save();
        credentials.Delete(connection.Account);
        Changed?.Invoke();
    }
}
