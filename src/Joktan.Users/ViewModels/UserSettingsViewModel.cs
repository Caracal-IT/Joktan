using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Core.ViewModels;
using Joktan.Users.Resources;

namespace Joktan.Users.ViewModels;

/// <summary>A database on Data / Database the users can be kept in, or none.</summary>
public sealed record UserDatabaseChoice(string Id, string Name)
{
    public override string ToString() => Name;
}

/// <summary>Settings / User Settings: which database on Data / Database keeps the users and roles, or a new one made for them.</summary>
public partial class UserSettingsViewModel : PageViewModel
{
    private readonly UserDirectory _directory;

    public UserSettingsViewModel(UserDirectory directory) : base(Strings.UserSettings_Title, Strings.UserSettings_Subtitle)
    {
        _directory = directory;
        directory.Changed += Refresh;
        directory.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(UserDirectory.Activity))
            {
                OnPropertyChanged(nameof(Activity));
                OnPropertyChanged(nameof(IsBusy));
                CreateDatabaseCommand.NotifyCanExecuteChanged();
            }
            else if (e.PropertyName is nameof(UserDirectory.Error))
                OnPropertyChanged(nameof(Error));
        };
    }

    /// <summary>None, then the databases on Data / Database.</summary>
    public IReadOnlyList<UserDatabaseChoice> Choices
        => [new("", Strings.UserSettings_None), .. _directory.Databases.Select(d => new UserDatabaseChoice(d.Id, d.Name))];

    public UserDatabaseChoice? Selected
    {
        get => Choices.FirstOrDefault(c => c.Id == (_directory.Database?.Id ?? ""));
        set
        {
            if (value is null || value.Id == (_directory.Database?.Id ?? ""))
                return;
            _ = _directory.UseDatabaseAsync(value.Id.Length == 0 ? null : value.Id);
        }
    }

    public string DatabaseText => _directory.Database is { } database
        ? string.Format(Strings.Users_KeptIn, database.Name, database.ConnectionText)
        : Strings.Users_NoDatabase;

    public bool IsBusy => _directory.IsBusy;

    public string? Activity => _directory.Activity;

    public string? Error => _directory.Error;

    private bool CanCreate => !_directory.IsBusy;

    /// <summary>The form for a new users database, while it is open.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCreating))]
    public partial CreateDatabaseViewModel? CreateForm { get; set; }

    public bool IsCreating => CreateForm is not null;

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private void CreateDatabase() => CreateForm = new CreateDatabaseViewModel(_directory, () => CreateForm = null);

    /// <summary>Shows the databases again, as after one was added or removed on Data / Database.</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(Choices));
        OnPropertyChanged(nameof(Selected));
        OnPropertyChanged(nameof(DatabaseText));
    }
}
