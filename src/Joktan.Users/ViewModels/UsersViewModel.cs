using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Core.ViewModels;
using Joktan.Users.Model;
using Joktan.Users.Resources;

namespace Joktan.Users.ViewModels;

/// <summary>
/// Users: the people and their roles, a tab each, kept in a database on Data / Database that the page
/// can create. Workflows assign their questions to these roles, or to one user. Specified in docs/features/users/spec.md.
/// </summary>
public partial class UsersViewModel : PageViewModel
{
    public const int UsersTab = 0;
    public const int RolesTab = 1;

    public UsersViewModel(UserDirectory directory) : base(Strings.Users_Title, Strings.Users_Subtitle)
    {
        Directory = directory;
        directory.Changed += Rebuild;
        directory.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(UserDirectory.Activity) or nameof(UserDirectory.IsBusy))
                BusyChanged();
            else if (e.PropertyName is nameof(UserDirectory.Error))
                OnPropertyChanged(nameof(Error));
        };
        Rebuild();
    }

    public UserDirectory Directory { get; }

    [ObservableProperty]
    public partial int SelectedTab { get; set; } = UsersTab;

    public ObservableCollection<UserRow> Users { get; } = [];

    public ObservableCollection<RoleRow> Roles { get; } = [];

    public bool HasDatabase => Directory.HasDatabase;

    public bool NeedsDatabase => !Directory.HasDatabase;

    /// <summary>Where the users are kept, such as "Kept in users on Data / Database: postgres://postgres@localhost:5433/users".</summary>
    public string DatabaseText => Directory.Database is { } database
        ? string.Format(Strings.Users_KeptIn, database.Name, database.ConnectionText)
        : Strings.Users_NoDatabase;

    public bool IsBusy => Directory.IsBusy;

    public string? Activity => Directory.Activity;

    public string? Error => Directory.Error;

    public bool HasUsers => Users.Count > 0;

    public bool HasRoles => Roles.Count > 0;

    public bool ShowsNoUsers => Directory.IsLoaded && Users.Count == 0;

    /// <summary>The recommended roles not in the database, such as "Manager, Viewer"; empty when it has them all.</summary>
    public string MissingRolesText => string.Join(", ", Directory.MissingRecommendedRoles.Select(r => r.Name));

    public bool HasMissingRoles => Directory.IsLoaded && Directory.MissingRecommendedRoles.Count > 0;

    /// <summary>The user being added or changed, shown in place of the list.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditingUser))]
    public partial UserEditorViewModel? UserEditor { get; set; }

    public bool IsEditingUser => UserEditor is not null;

    /// <summary>The role being added or changed, shown in place of the list.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditingRole))]
    public partial RoleEditorViewModel? RoleEditor { get; set; }

    public bool IsEditingRole => RoleEditor is not null;

    /// <summary>Opens Settings / User Settings. Set by the app.</summary>
    public Action? OpenSettings { get; set; }

    /// <summary>Reads the users again. Called while the page is shown.</summary>
    public Task ActivateAsync() => Directory.HasDatabase ? Directory.RefreshAsync() : Task.CompletedTask;

    private bool CanRun => !Directory.IsBusy;

    private bool CanChange => CanRun && Directory.HasDatabase && Directory.IsLoaded;

    /// <summary>The form for a new users database, while it is open.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCreating))]
    public partial CreateDatabaseViewModel? CreateForm { get; set; }

    public bool IsCreating => CreateForm is not null;

    /// <summary>Whether the database is chosen but could not be read, as when its container is stopped.</summary>
    public bool IsUnreachable => Directory.HasDatabase && !Directory.IsLoaded && !Directory.IsBusy;

    /// <summary>Whether the tabs can show: the database is chosen, and was read or is being read.</summary>
    public bool ShowsTabs => Directory.HasDatabase && !IsUnreachable;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private void CreateDatabase()
    {
        Directory.Error = null;
        CreateForm = new CreateDatabaseViewModel(Directory, () => CreateForm = null);
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task StartDatabaseAsync() => Directory.StartDatabaseAsync();

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task RefreshAsync() => Directory.RefreshAsync();

    [RelayCommand]
    private void Settings() => OpenSettings?.Invoke();

    [RelayCommand(CanExecute = nameof(CanChange))]
    private void AddUser()
    {
        Directory.Error = null;
        SelectedTab = UsersTab;
        UserEditor = new UserEditorViewModel(null, this);
    }

    [RelayCommand(CanExecute = nameof(CanChange))]
    private void AddRole()
    {
        Directory.Error = null;
        SelectedTab = RolesTab;
        RoleEditor = new RoleEditorViewModel(null, this);
    }

    [RelayCommand(CanExecute = nameof(CanChange))]
    private Task AddRecommendedRolesAsync() => Directory.AddRecommendedRolesAsync();

    internal void Edit(UserRow row)
    {
        Directory.Error = null;
        UserEditor = new UserEditorViewModel(row.User, this);
    }

    internal void Edit(RoleRow row)
    {
        Directory.Error = null;
        RoleEditor = new RoleEditorViewModel(row.Role, this);
    }

    internal void CloseEditors()
    {
        UserEditor = null;
        RoleEditor = null;
    }

    internal async Task RemoveAsync(UserRow row)
    {
        if (await Directory.DeleteUserAsync(row.User) is { } problem)
            Directory.Error = problem;
    }

    internal async Task RemoveAsync(RoleRow row)
    {
        if (await Directory.DeleteRoleAsync(row.Role) is { } problem)
            Directory.Error = problem;
    }

    internal bool IsBusyNow => Directory.IsBusy;

    // The rows are made again from what was read; an editor open stays open.
    private void Rebuild()
    {
        Users.Clear();
        foreach (var user in Directory.Users)
            Users.Add(new UserRow(user, Directory.RoleOf(user), Directory.IsLastAdministrator(user.Id), this));
        Roles.Clear();
        foreach (var role in Directory.Roles)
            Roles.Add(new RoleRow(role, Directory.UsersWith(role), this));

        OnPropertyChanged(nameof(HasDatabase));
        OnPropertyChanged(nameof(NeedsDatabase));
        OnPropertyChanged(nameof(DatabaseText));
        OnPropertyChanged(nameof(HasUsers));
        OnPropertyChanged(nameof(HasRoles));
        OnPropertyChanged(nameof(ShowsNoUsers));
        OnPropertyChanged(nameof(MissingRolesText));
        OnPropertyChanged(nameof(HasMissingRoles));
        UserEditor?.RolesChanged();
        BusyChanged();
    }

    private void BusyChanged()
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(Activity));
        OnPropertyChanged(nameof(IsUnreachable));
        OnPropertyChanged(nameof(ShowsTabs));
        CreateDatabaseCommand.NotifyCanExecuteChanged();
        StartDatabaseCommand.NotifyCanExecuteChanged();
        RefreshCommand.NotifyCanExecuteChanged();
        AddUserCommand.NotifyCanExecuteChanged();
        AddRoleCommand.NotifyCanExecuteChanged();
        AddRecommendedRolesCommand.NotifyCanExecuteChanged();
        UserEditor?.BusyChanged();
        RoleEditor?.BusyChanged();
        foreach (var row in Users)
            row.BusyChanged();
        foreach (var row in Roles)
            row.BusyChanged();
    }
}

/// <summary>A user in the list on the Users tab.</summary>
public partial class UserRow : ObservableObject
{
    private readonly UsersViewModel _page;

    public UserRow(User user, Role? role, bool isLastAdministrator, UsersViewModel page)
    {
        User = user;
        RoleName = role?.Name ?? "";
        IsAdministrator = role?.BuiltIn == true;
        IsLastAdministrator = isLastAdministrator;
        _page = page;
    }

    public User User { get; }

    public string Name => User.FullName;

    public string Username => User.Username;

    public string Email => User.Email;

    public bool HasEmail => User.Email.Length > 0;

    /// <summary>Whether they have no password yet, as the default administrator starts.</summary>
    public bool HasNoPassword => !User.HasPassword;

    public string RoleName { get; }

    public bool IsAdministrator { get; }

    /// <summary>Whether they are the only one with the Administrator role, so they cannot be removed.</summary>
    public bool IsLastAdministrator { get; }

    public string Description => User.Description;

    public bool HasDescription => User.Description.Trim().Length > 0;

    public string Notes => User.Notes;

    public bool HasNotes => User.Notes.Trim().Length > 0;

    [ObservableProperty]
    public partial bool IsConfirmingRemove { get; set; }

    public string RemoveText => string.Format(Strings.UserRow_Remove, Name);

    private bool CanRun => !_page.IsBusyNow;

    private bool CanRemove => CanRun && !IsLastAdministrator;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private void Edit() => _page.Edit(this);

    [RelayCommand(CanExecute = nameof(CanRemove))]
    private void Remove() => IsConfirmingRemove = true;

    [RelayCommand]
    private void CancelRemove() => IsConfirmingRemove = false;

    [RelayCommand(CanExecute = nameof(CanRemove))]
    private Task ConfirmRemoveAsync()
    {
        IsConfirmingRemove = false;
        return _page.RemoveAsync(this);
    }

    internal void BusyChanged()
    {
        EditCommand.NotifyCanExecuteChanged();
        RemoveCommand.NotifyCanExecuteChanged();
        ConfirmRemoveCommand.NotifyCanExecuteChanged();
    }
}

/// <summary>A role in the list on the Roles tab, with how many users have it.</summary>
public partial class RoleRow : ObservableObject
{
    private readonly UsersViewModel _page;

    public RoleRow(Role role, int users, UsersViewModel page)
    {
        Role = role;
        UserCount = users;
        _page = page;
    }

    public Role Role { get; }

    public string Name => Role.Name;

    public string Description => Role.Description;

    public bool IsBuiltIn => Role.BuiltIn;

    public int UserCount { get; }

    public string UsersText => UserCount switch
    {
        0 => Strings.RoleRow_NoUsers,
        1 => Strings.RoleRow_OneUser,
        _ => string.Format(Strings.RoleRow_Users, UserCount),
    };

    /// <summary>The Administrator role and roles that users have cannot be removed.</summary>
    public bool CanBeRemoved => !IsBuiltIn && UserCount == 0;

    public string? CannotRemoveTip => IsBuiltIn ? Strings.Directory_AdministratorStays : UserCount > 0 ? Strings.RoleRow_InUse : null;

    [ObservableProperty]
    public partial bool IsConfirmingRemove { get; set; }

    public string RemoveText => string.Format(Strings.RoleRow_Remove, Name);

    private bool CanRun => !_page.IsBusyNow;

    private bool CanRemove => CanRun && CanBeRemoved;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private void Edit() => _page.Edit(this);

    [RelayCommand(CanExecute = nameof(CanRemove))]
    private void Remove() => IsConfirmingRemove = true;

    [RelayCommand]
    private void CancelRemove() => IsConfirmingRemove = false;

    [RelayCommand(CanExecute = nameof(CanRemove))]
    private Task ConfirmRemoveAsync()
    {
        IsConfirmingRemove = false;
        return _page.RemoveAsync(this);
    }

    internal void BusyChanged()
    {
        EditCommand.NotifyCanExecuteChanged();
        RemoveCommand.NotifyCanExecuteChanged();
        ConfirmRemoveCommand.NotifyCanExecuteChanged();
    }
}
