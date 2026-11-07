using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Users.Model;
using Joktan.Users.Resources;

namespace Joktan.Users.ViewModels;

/// <summary>Adds a role on the Roles tab, or changes one. The Administrator role keeps its name. Nothing is saved until Save.</summary>
public partial class RoleEditorViewModel : ObservableObject
{
    private readonly Role? _original;
    private readonly UsersViewModel _page;

    public RoleEditorViewModel(Role? original, UsersViewModel page)
    {
        _original = original;
        _page = page;
        Name = original?.Name ?? "";
        Description = original?.Description ?? "";
    }

    public bool IsNew => _original is null;

    public string Title => _original is null ? Strings.RoleEditor_NewRole : string.Format(Strings.RoleEditor_Edit, _original.Name);

    /// <summary>Whether the name can change: not for the Administrator role.</summary>
    public bool CanRename => _original?.BuiltIn != true;

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial string Description { get; set; }

    [ObservableProperty]
    public partial string? Error { get; set; }

    private bool CanSave => !_page.IsBusyNow;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        var role = _original?.Clone() ?? new Role();
        role.Name = Name.Trim();
        role.Description = Description.Trim();
        Error = await _page.Directory.SaveRoleAsync(role);
        if (Error is null)
            _page.CloseEditors();
    }

    [RelayCommand]
    private void Cancel() => _page.CloseEditors();

    internal void BusyChanged() => SaveCommand.NotifyCanExecuteChanged();
}
