using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Users.Model;
using Joktan.Users.Resources;

namespace Joktan.Users.ViewModels;

/// <summary>Adds a user on the Users tab, or changes one. Nothing is saved until Save.</summary>
public partial class UserEditorViewModel : ObservableObject
{
    private readonly User? _original;
    private readonly UsersViewModel _page;

    public UserEditorViewModel(User? original, UsersViewModel page)
    {
        _original = original;
        _page = page;
        Username = original?.Username ?? "";
        Email = original?.Email ?? "";
        FirstName = original?.FirstName ?? "";
        LastName = original?.LastName ?? "";
        Description = original?.Description ?? "";
        Notes = original?.Notes ?? "";
        Roles = page.Directory.Roles;
        // A new user gets the first role that is not Administrator, so nobody is made an administrator by accident.
        SelectedRole = original is not null
            ? Roles.FirstOrDefault(r => r.Id == original.RoleId)
            : Roles.FirstOrDefault(r => !r.BuiltIn) ?? Roles.FirstOrDefault();
    }

    public bool IsNew => _original is null;

    public string Title => _original is null ? Strings.UserEditor_NewUser : string.Format(Strings.UserEditor_Edit, _original.FullName);

    [ObservableProperty]
    public partial string Username { get; set; }

    [ObservableProperty]
    public partial string Email { get; set; }

    /// <summary>A new password; empty keeps the one set, for a user who has one.</summary>
    [ObservableProperty]
    public partial string Password { get; set; } = "";

    [ObservableProperty]
    public partial string ConfirmPassword { get; set; } = "";

    public string PasswordHint => _original is { HasPassword: true } ? Strings.UsersView_PasswordKept : Strings.UsersView_PasswordHint;

    [ObservableProperty]
    public partial string FirstName { get; set; }

    [ObservableProperty]
    public partial string LastName { get; set; }

    [ObservableProperty]
    public partial string Description { get; set; }

    [ObservableProperty]
    public partial string Notes { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<Role> Roles { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RoleDescription))]
    public partial Role? SelectedRole { get; set; }

    public string RoleDescription => SelectedRole?.Description ?? "";

    [ObservableProperty]
    public partial string? Error { get; set; }

    private bool CanSave => !_page.IsBusyNow;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (Password != ConfirmPassword)
        {
            Error = Strings.UserEditor_PasswordsDiffer;
            return;
        }

        var user = _original?.Clone() ?? new User();
        user.Username = Username.Trim();
        user.Email = Email.Trim();
        user.NewPassword = Password.Length > 0 ? Password : null;
        user.FirstName = FirstName.Trim();
        user.LastName = LastName.Trim();
        user.Description = Description.Trim();
        user.Notes = Notes.Trim();
        user.RoleId = SelectedRole?.Id ?? 0;
        Error = await _page.Directory.SaveUserAsync(user);
        if (Error is null)
            _page.CloseEditors();
    }

    [RelayCommand]
    private void Cancel() => _page.CloseEditors();

    internal void RolesChanged()
    {
        var id = SelectedRole?.Id;
        Roles = _page.Directory.Roles;
        SelectedRole = Roles.FirstOrDefault(r => r.Id == id);
    }

    internal void BusyChanged() => SaveCommand.NotifyCanExecuteChanged();
}
