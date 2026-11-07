using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Joktan.Containers;
using Joktan.Containers.ViewModels;
using Joktan.Core;
using Joktan.Data.Postgres;
using Joktan.Users.Model;
using Joktan.Users.Resources;
using Microsoft.Extensions.Logging;

namespace Joktan.Users;

/// <summary>
/// The users and roles, kept in a database on Data / Database, as last read. Creates that database when
/// asked, sets up its tables, and keeps to the rules: the Administrator role stays, someone always has it,
/// and a role that users have is not removed. Workflows assign their questions to these roles and users.
/// </summary>
public sealed partial class UserDirectory : ObservableObject
{
    /// <summary>The name a database made for the users is given, with a number added if it is taken.</summary>
    public const string DatabaseName = "users";

    private readonly UserSettings _settings;
    private readonly PostgresDatabasesViewModel _databases;
    private readonly Func<PostgresDatabase, string, IUserStore> _open;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Action _save;
    private string? _setUp;

    /// <param name="databases">The databases on Data / Database.</param>
    /// <param name="open">Opens the store in a database with its password; over the network otherwise.</param>
    /// <param name="delay">Waits, while a new database starts; tests give one that does not.</param>
    /// <param name="save">Saves the settings; to the settings folder otherwise.</param>
    public UserDirectory(UserSettings settings, PostgresDatabasesViewModel databases, Func<PostgresDatabase, string, IUserStore>? open = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null, Action? save = null)
    {
        _settings = settings;
        _databases = databases;
        _open = open ?? ((database, password) => new PostgresUserStore(new PostgresConnection("localhost", database.Port, database.Username, password, database.Database)));
        _delay = delay ?? Task.Delay;
        _save = save ?? (() => settings.Save());

        // A database removed on Data / Database takes the users with it; the page then offers to create one.
        databases.Items.CollectionChanged += (_, _) =>
        {
            if (Database is null && (IsLoaded || Roles.Count > 0))
            {
                _setUp = null;
                Forget();
            }
            else
                Changed?.Invoke();
        };
    }

    /// <summary>How long a new database is given to start and set itself up before it is given up on.</summary>
    public TimeSpan StartTimeout { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>The databases on Data / Database, which the users can be kept in.</summary>
    public IReadOnlyList<PostgresDatabase> Databases => _databases.Databases;

    /// <summary>The database the users are kept in, or null when none is chosen or it was removed.</summary>
    public PostgresDatabase? Database => _databases.Databases.FirstOrDefault(d => d.Id == _settings.DatabaseId);

    public bool HasDatabase => Database is not null;

    public IReadOnlyList<Role> Roles { get; private set; } = [];

    public IReadOnlyList<User> Users { get; private set; } = [];

    /// <summary>Whether the users and roles have been read from the database since it was chosen.</summary>
    [ObservableProperty]
    public partial bool IsLoaded { get; private set; }

    /// <summary>What is under way, such as "Starting the database", or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    public partial string? Activity { get; private set; }

    public bool IsBusy => Activity is not null;

    /// <summary>Why the users could not be read or changed, or null.</summary>
    [ObservableProperty]
    public partial string? Error { get; set; }

    /// <summary>Raised after the users or roles were read or changed, or another database was chosen.</summary>
    public event Action? Changed;

    public Role? RoleOf(User user) => Roles.FirstOrDefault(r => r.Id == user.RoleId);

    public int UsersWith(Role role) => Users.Count(u => u.RoleId == role.Id);

    /// <summary>The Administrator role, which cannot be removed or renamed.</summary>
    public Role? AdministratorRole => Roles.FirstOrDefault(r => r.BuiltIn);

    /// <summary>The recommended roles there is no role of the same name for.</summary>
    public IReadOnlyList<Role> MissingRecommendedRoles
        => RecommendedRoles.All.Where(r => Roles.All(o => !string.Equals(o.Name.Trim(), r.Name, StringComparison.OrdinalIgnoreCase))).ToList();

    /// <summary>Keeps the users in another database on Data / Database, or in none; its tables are made when it is next read.</summary>
    public async Task UseDatabaseAsync(string? id)
    {
        if ((id ?? "") == _settings.DatabaseId)
            return;
        _settings.DatabaseId = id ?? "";
        _save();
        _setUp = null;
        Error = null;
        Forget();
        if (HasDatabase)
            await RefreshAsync();
    }

    /// <summary>Reads the roles and users again, making the tables first the first time a database is used.</summary>
    public async Task<bool> RefreshAsync()
    {
        if (Database is null || IsBusy)
            return false;
        return await RunAsync(Strings.Directory_Reading, async store =>
        {
            await SetUpOnceAsync(store);
            await ReadAsync(store);
            return true;
        });
    }

    /// <summary>A name for a new users database that no database has yet, and whose data folder holds no database left from before.</summary>
    public string FreeName()
    {
        var name = _databases.FreeName(DatabaseName);
        for (var i = 2; HasOldData(name); i++)
            name = _databases.FreeName($"{DatabaseName}-{i}");
        return name;
    }

    /// <summary>The folder a new database of this name keeps its data in.</summary>
    public string DataFolderFor(string name) => PostgresProject.DataFolderFor(_databases.DataRoot, name);

    /// <summary>
    /// Whether the data folder a new database of this name would get already holds one, as after a database
    /// was removed on Data / Database: it keeps the username and password it was made with, not new ones.
    /// </summary>
    public bool HasOldData(string name) => name.Trim().Length > 0 && PostgresProject.HasData(DataFolderFor(name));

    /// <summary>The first port no database on Data / Database publishes.</summary>
    public int FreePort() => _databases.FreePort();

    /// <summary>
    /// Adds a database for the users on Data / Database with this name, port, username and password (kept
    /// in the credential store), starts it, waits for it to be up, and gives it the recommended roles and a
    /// default administrator. Gives why it could not, or null.
    /// </summary>
    public async Task<string?> CreateDatabaseAsync(string name, int port, string username, string password)
    {
        if (IsBusy)
            return Strings.Directory_Busy;
        if (HasOldData(name))
            return string.Format(Strings.Directory_OldData, DataFolderFor(name.Trim()));
        Error = null;
        Activity = Strings.Directory_Creating;
        PostgresDatabase database;
        try
        {
            var (made, problem) = await _databases.CreateAsync(name, port, username, password);
            if (made is null)
                return problem ?? Strings.Directory_CouldNotCreate;
            database = made;

            _settings.DatabaseId = made.Id;
            _save();
            _setUp = null;
            Forget();
            AppLog.For("Users").LogInformation("Made the database {Database} for the users", made.Name);
            if (problem is not null)
                return Error = string.Format(Strings.Directory_MadeButNotStarted, made.Name, problem);
        }
        finally
        {
            Activity = null;
        }

        return await WaitForAsync(database);
    }

    /// <summary>Starts the users database on Data / Database, waits for it to be up and reads the users. Gives why it could not, or null.</summary>
    public async Task<string?> StartDatabaseAsync()
    {
        if (Database is not { } database)
            return Strings.Directory_NoDatabase;
        if (IsBusy)
            return Strings.Directory_Busy;
        Error = null;
        Activity = Strings.Directory_Starting;
        try
        {
            if (await _databases.StartAsync(database.Id) is { } problem)
                return Error = string.Format(Strings.Directory_CouldNotStart, database.Name, problem);
        }
        finally
        {
            Activity = null;
        }

        return await WaitForAsync(database);
    }

    // A server that was just started sets up its data folder before it takes connections, which can take a while.
    private async Task<string?> WaitForAsync(PostgresDatabase database)
    {
        Activity = Strings.Directory_WaitingForTheDatabase;
        try
        {
            using var timeout = new CancellationTokenSource(StartTimeout);
            Exception? last = null;
            while (!timeout.IsCancellationRequested)
            {
                try
                {
                    await using var store = Open(database);
                    await SetUpOnceAsync(store);
                    await ReadAsync(store);
                    return null;
                }
                catch (Exception e) when (IsUnreachable(e) && !timeout.IsCancellationRequested)
                {
                    last = e;
                }

                try
                {
                    await _delay(TimeSpan.FromSeconds(2), timeout.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            return Error = string.Format(Strings.Directory_DidNotStart, database.Name, last?.Message ?? "");
        }
        catch (Exception e) when (IsShowable(e))
        {
            return Error = Describe(e, database);
        }
        finally
        {
            Activity = null;
            Changed?.Invoke();
        }
    }

    // What was read from a database no longer in use goes, so that nothing offers its roles.
    private void Forget()
    {
        Roles = [];
        Users = [];
        IsLoaded = false;
        OnPropertyChanged(nameof(Database));
        OnPropertyChanged(nameof(HasDatabase));
        Changed?.Invoke();
    }

    /// <summary>Adds a role, or changes one. Gives why it could not, or null.</summary>
    public async Task<string?> SaveRoleAsync(Role role)
    {
        var name = role.Name.Trim();
        if (name.Length == 0)
            return Strings.Directory_NameTheRole;
        if (Roles.Any(r => r.Id != role.Id && string.Equals(r.Name.Trim(), name, StringComparison.OrdinalIgnoreCase)))
            return string.Format(Strings.Directory_RoleExists, name);
        if (Roles.FirstOrDefault(r => r.Id == role.Id) is { BuiltIn: true } builtIn && builtIn.Name != name)
            return Strings.Directory_AdministratorKeepsItsName;
        return await ChangeAsync(Strings.Directory_Saving, async store =>
        {
            await store.SaveRoleAsync(role);
            AppLog.For("Users").LogInformation("Saved the role {Role}", name);
        });
    }

    /// <summary>Removes a role that nobody has. Gives why it could not, or null.</summary>
    public async Task<string?> DeleteRoleAsync(Role role)
    {
        if (role.BuiltIn)
            return Strings.Directory_AdministratorStays;
        if (UsersWith(role) is > 0 and var count)
            return string.Format(count == 1 ? Strings.Directory_RoleInUseOne : Strings.Directory_RoleInUse, role.Name, count);
        return await ChangeAsync(Strings.Directory_Saving, async store =>
        {
            await store.DeleteRoleAsync(role.Id);
            AppLog.For("Users").LogInformation("Removed the role {Role}", role.Name);
        });
    }

    /// <summary>Adds the recommended roles there is no role of the same name for. Gives why it could not, or null.</summary>
    public async Task<string?> AddRecommendedRolesAsync()
    {
        var missing = MissingRecommendedRoles;
        if (missing.Count == 0)
            return null;
        return await ChangeAsync(Strings.Directory_Saving, async store =>
        {
            // The Administrator role is made built in by setting up, which a database that lost it is given again.
            foreach (var role in missing.Where(r => !r.BuiltIn))
                await store.SaveRoleAsync(new Role { Name = role.Name, Description = role.Description });
            if (missing.Any(r => r.BuiltIn))
            {
                _setUp = null;
                await store.SetUpAsync();
            }
        });
    }

    /// <summary>Adds a user, or changes one. Gives why it could not, or null.</summary>
    public async Task<string?> SaveUserAsync(User user)
    {
        user = user.Clone();
        user.Username = user.Username.Trim();
        user.Email = user.Email.Trim();
        user.FirstName = user.FirstName.Trim();
        user.LastName = user.LastName.Trim();
        user.Description = user.Description.Trim();
        user.Notes = user.Notes.Trim();
        if (User.CheckUsername(user.Username) is { } usernameProblem)
            return usernameProblem;
        if (Users.Any(u => u.Id != user.Id && string.Equals(u.Username, user.Username, StringComparison.OrdinalIgnoreCase)))
            return string.Format(Strings.Directory_UsernameTaken, user.Username);
        if (User.CheckEmail(user.Email) is { } emailProblem)
            return emailProblem;
        if (user.Id == 0 && user.NewPassword is not { Length: > 0 })
            return Strings.Directory_EnterAUserPassword;
        if (user.FirstName.Length == 0)
            return Strings.Directory_EnterTheFirstName;
        if (user.LastName.Length == 0)
            return Strings.Directory_EnterTheLastName;
        if (Roles.All(r => r.Id != user.RoleId))
            return Strings.Directory_ChooseARole;
        if (IsLastAdministrator(user.Id) && AdministratorRole is { } admin && user.RoleId != admin.Id)
            return Strings.Directory_LastAdministratorKeepsRole;
        return await ChangeAsync(Strings.Directory_Saving, async store =>
        {
            await store.SaveUserAsync(user);
            AppLog.For("Users").LogInformation("Saved the user {User}", user.FullName);
        });
    }

    /// <summary>Removes a user, unless they are the last with the Administrator role. Gives why it could not, or null.</summary>
    public async Task<string?> DeleteUserAsync(User user)
    {
        if (IsLastAdministrator(user.Id))
            return Strings.Directory_LastAdministratorStays;
        return await ChangeAsync(Strings.Directory_Saving, async store =>
        {
            await store.DeleteUserAsync(user.Id);
            AppLog.For("Users").LogInformation("Removed the user {User}", user.FullName);
        });
    }

    /// <summary>
    /// The user with this username and password, as for signing in to the website, read afresh with the
    /// roles so that a change made since counts; null when either is wrong or there is no users database.
    /// Does not show what it is doing on the Users page, as it is not something done there.
    /// </summary>
    /// <exception cref="UserStoreException">The users database could not be reached or read.</exception>
    public async Task<User?> SignInAsync(string username, string password, CancellationToken cancel = default)
    {
        if (Database is not { } database || username.Trim().Length == 0 || password.Length == 0)
            return null;
        try
        {
            await using var store = Open(database);
            if (!await store.CheckPasswordAsync(username, password, cancel))
                return null;
            await ReadAsync(store);
            return Users.FirstOrDefault(u => string.Equals(u.Username, username.Trim(), StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception e) when (IsUnreachable(e) || (IsShowable(e) && e is not UserStoreException))
        {
            throw new UserStoreException(IsUnreachable(e) ? string.Format(Strings.Directory_Unreachable, database.Name, e.Message) : Describe(e, database));
        }
    }

    /// <summary>Whether a user has the Administrator role, who may see and answer every question.</summary>
    public bool IsAdministrator(User user) => RoleOf(user)?.BuiltIn == true;

    /// <summary>Whether the user with this id is the only one with the Administrator role.</summary>
    public bool IsLastAdministrator(int userId)
        => AdministratorRole is { } admin && Users.Where(u => u.RoleId == admin.Id).Select(u => u.Id).ToList() is [var only] && only == userId;

    private async Task<string?> ChangeAsync(string activity, Func<IUserStore, Task> change)
    {
        if (Database is null)
            return Strings.Directory_NoDatabase;
        if (IsBusy)
            return Strings.Directory_Busy;
        var done = await RunAsync(activity, async store =>
        {
            await change(store);
            await ReadAsync(store);
            return true;
        });
        return done ? null : Error;
    }

    private async Task<bool> RunAsync(string activity, Func<IUserStore, Task<bool>> work)
    {
        if (Database is not { } database)
            return false;
        Error = null;
        Activity = activity;
        try
        {
            await using var store = Open(database);
            return await work(store);
        }
        catch (Exception e) when (IsUnreachable(e))
        {
            Error = string.Format(Strings.Directory_Unreachable, database.Name, e.Message);
            return false;
        }
        catch (Exception e) when (IsShowable(e))
        {
            Error = Describe(e, database);
            return false;
        }
        finally
        {
            Activity = null;
        }
    }

    private IUserStore Open(PostgresDatabase database) => _open(database, _databases.PasswordOf(database) ?? "");

    private async Task SetUpOnceAsync(IUserStore store)
    {
        if (_setUp == _settings.DatabaseId)
            return;
        if (await store.SetUpAsync())
            AppLog.For("Users").LogInformation("Gave the users database its recommended roles and a default administrator");
        _setUp = _settings.DatabaseId;
    }

    private async Task ReadAsync(IUserStore store)
    {
        Roles = await store.RolesAsync();
        Users = await store.UsersAsync();
        IsLoaded = true;
        Changed?.Invoke();
    }

    // A server that is down, still starting, or not there.
    private static bool IsUnreachable(Exception e) => e is SocketException or TimeoutException
                                                     || (e is DbException && e.InnerException is SocketException or TimeoutException or System.IO.IOException)
                                                     || (e is DbException db && db.SqlState is "57P03");

    // A refused password is most often a data folder made before, which keeps the password it was made with.
    private static string Describe(Exception e, PostgresDatabase database)
        => e is DbException { SqlState: "28P01" or "28000" }
            ? string.Format(Strings.Directory_PasswordRefused, database.Name, database.Username, database.DataFolder)
            : e.Message;

    private static bool IsShowable(Exception e) => e is DbException or UserStoreException or InvalidOperationException or System.Text.Json.JsonException;
}
