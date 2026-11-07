using System;
using System.Globalization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Containers;
using Joktan.Core.ViewModels;
using Joktan.Users.Resources;

namespace Joktan.Users.ViewModels;

/// <summary>
/// The form that creates the users database on Data / Database: its name, port, username and password,
/// with a free name and port filled in. Shown on the Users page and on Settings / User Settings.
/// </summary>
public partial class CreateDatabaseViewModel : ViewModelBase
{
    private readonly UserDirectory _directory;
    private readonly Action _close;

    public CreateDatabaseViewModel(UserDirectory directory, Action close)
    {
        _directory = directory;
        _close = close;
        NameText = directory.FreeName();
        PortText = directory.FreePort().ToString(CultureInfo.InvariantCulture);
        _directory.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(UserDirectory.Activity))
                CreateCommand.NotifyCanExecuteChanged();
        };
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OldDataWarning))]
    public partial string NameText { get; set; }

    /// <summary>Why the name will not do because its data folder holds a database from before, or null.</summary>
    public string? OldDataWarning => _directory.HasOldData(NameText)
        ? string.Format(Strings.Directory_OldData, _directory.DataFolderFor(NameText.Trim()))
        : null;

    [ObservableProperty]
    public partial string PortText { get; set; }

    [ObservableProperty]
    public partial string Username { get; set; } = PostgresDatabase.DefaultUsername;

    [ObservableProperty]
    public partial string Password { get; set; } = "";

    [ObservableProperty]
    public partial string ConfirmPassword { get; set; } = "";

    [ObservableProperty]
    public partial string? Error { get; set; }

    private bool CanCreate => !_directory.IsBusy;

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private async Task CreateAsync()
    {
        var portProblem = MosquittoBroker.ParsePort(PortText, out var port);
        Error = PostgresDatabase.CheckIdentifier(Username, Containers.Resources.Strings.PostgresDatabase_Username)
                ?? portProblem
                ?? (Password.Length == 0 ? Strings.Directory_EnterAPassword : null)
                ?? (Password != ConfirmPassword ? Strings.Directory_PasswordsDiffer : null);
        if (Error is not null || port is null)
            return;

        // The form closes once the database is made, so the wait for it to start shows on the page.
        Error = await _directory.CreateDatabaseAsync(NameText, port.Value, Username, Password);
        if (Error is null || _directory.HasDatabase)
            _close();
    }

    [RelayCommand]
    private void Cancel() => _close();
}
