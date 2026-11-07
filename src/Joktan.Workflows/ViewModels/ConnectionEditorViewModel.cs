using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Core.ViewModels;
using Joktan.Data.Postgres;
using Joktan.Workflows.Engine;
using Joktan.Workflows.Resources;

namespace Joktan.Workflows.ViewModels;

/// <summary>
/// A database connection being made or changed: where the server is, who to be, and the password, which goes to the
/// credential store. Used on Settings / Workflow Settings and in a database step's window.
/// </summary>
public sealed partial class ConnectionEditorViewModel : ViewModelBase
{
    private readonly DatabaseConnections _connections;
    private readonly DatabaseConnectionSettings? _existing;
    private readonly DatabaseOpener _open;
    private readonly Action<DatabaseConnectionSettings> _saved;
    private readonly Action _cancelled;

    /// <param name="existing">The connection to change, or null for a new one.</param>
    /// <param name="open">Opens the database to test it; over the network otherwise.</param>
    /// <param name="saved">Called with the connection once it is saved.</param>
    public ConnectionEditorViewModel(DatabaseConnections connections, DatabaseConnectionSettings? existing, DatabaseOpener? open,
        Action<DatabaseConnectionSettings> saved, Action cancelled)
    {
        _connections = connections;
        _existing = existing;
        _open = open ?? ((host, port, username, password, database) => new PostgresConnection(host, port, username, password, database));
        _saved = saved;
        _cancelled = cancelled;
        if (existing is not null)
        {
            Name = existing.Name;
            Host = existing.Host;
            Port = existing.Port;
            Username = existing.Username;
            Database = existing.Database;
        }
    }

    public bool IsNew => _existing is null;

    public string Title => IsNew ? Strings.Connection_New : string.Format(Strings.Connection_Change, _existing!.Name);

    /// <summary>What an empty password box means: none for a new connection, the one kept for a changed one.</summary>
    public string PasswordHint => IsNew ? Strings.Connection_Password : Strings.Connection_PasswordKept;

    [ObservableProperty]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    public partial string Host { get; set; } = "localhost";

    [ObservableProperty]
    public partial decimal Port { get; set; } = DatabaseConnectionSettings.DefaultPort;

    [ObservableProperty]
    public partial string Username { get; set; } = "postgres";

    [ObservableProperty]
    public partial string Password { get; set; } = "";

    [ObservableProperty]
    public partial string Database { get; set; } = "postgres";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; set; }

    public bool HasError => Error is not null;

    /// <summary>What testing found, when it worked.</summary>
    [ObservableProperty]
    public partial string? Status { get; set; }

    [ObservableProperty]
    public partial bool IsTesting { get; set; }

    // The password to use: the one typed, or for a changed connection the one kept.
    private string PasswordToUse => Password.Length > 0 || _existing is null ? Password : _connections.PasswordOf(_existing) ?? "";

    private string? Problem()
    {
        if (Name.Trim().Length == 0)
            return Strings.Connection_EnterAName;
        if (Host.Trim().Length == 0)
            return Strings.Connection_EnterAHost;
        if (Port is < 1 or > 65535)
            return Strings.Connection_PortRange;
        if (Username.Trim().Length == 0 || Database.Trim().Length == 0)
            return Strings.Connection_EnterUserAndDatabase;
        return null;
    }

    private DatabaseConnectionSettings Settings() => new()
    {
        Id = _existing?.Id ?? DatabaseConnectionSettings.NewId(),
        Name = Name.Trim(),
        Host = Host.Trim(),
        Port = (int)Port,
        Username = Username.Trim(),
        Database = Database.Trim(),
    };

    /// <summary>Connects with what is filled in, and says whether it worked.</summary>
    [RelayCommand]
    private async Task Test()
    {
        Status = null;
        if ((Error = Problem()) is not null)
            return;
        var settings = Settings();
        IsTesting = true;
        try
        {
            await using var connection = _open(settings.Host, settings.Port, settings.Username, PasswordToUse, settings.Database);
            var schemas = await connection.SchemasAsync();
            Status = string.Format(Strings.Connection_Works, schemas.Count);
        }
        catch (Exception e) when (e is Npgsql.NpgsqlException or System.Net.Sockets.SocketException or TimeoutException or InvalidOperationException
                                      or ArgumentException or OperationCanceledException)
        {
            Error = string.Format(Strings.Connection_DidNotWork, e is Npgsql.PostgresException postgres ? postgres.MessageText : e.Message);
        }
        finally
        {
            IsTesting = false;
        }
    }

    [RelayCommand]
    private void Save()
    {
        Status = null;
        if ((Error = Problem()) is not null)
            return;
        var settings = Settings();
        var password = Password.Length > 0 || _existing is null ? Password : null;
        if (_connections.Save(settings, password) is { } problem)
        {
            // The connection is kept; only its password is not.
            Error = problem;
            return;
        }

        _saved(settings);
    }

    [RelayCommand]
    private void Cancel() => _cancelled();
}
