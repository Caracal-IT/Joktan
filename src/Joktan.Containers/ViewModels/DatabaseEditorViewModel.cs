using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Containers.Resources;

namespace Joktan.Containers.ViewModels;

/// <summary>
/// Adds a PostgreSQL server on Data / Database, or changes one. For a new one, the database and the
/// data folder follow the name, until they are typed in. Nothing is saved until Save.
/// </summary>
public partial class DatabaseEditorViewModel : ObservableObject
{
    private readonly PostgresDatabase? _original;
    private readonly PostgresDatabasesViewModel _list;

    public DatabaseEditorViewModel(PostgresDatabase? original, PostgresDatabasesViewModel list)
    {
        _original = original;
        _list = list;
        var name = original?.Name ?? list.FreeName();
        NameText = name;
        PortText = (original?.Port ?? list.FreePort()).ToString(CultureInfo.InvariantCulture);
        Username = original?.Username ?? PostgresDatabase.DefaultUsername;
        DatabaseText = original?.Database ?? PostgresDatabase.DatabaseFor(name);
        DataFolderText = original?.DataFolder ?? PostgresProject.DataFolderFor(list.DataRoot, name);
        HasSavedPassword = original is not null && list.HasSavedPassword(original);
        HasData = original is not null && PostgresProject.HasData(original.DataFolder);
    }

    public bool IsNew => _original is null;

    public string Title => _original is null ? Strings.DatabaseEditor_NewDatabase : string.Format(Strings.DatabaseEditor_Edit, _original.Name);

    [ObservableProperty]
    public partial string NameText { get; set; }

    [ObservableProperty]
    public partial string PortText { get; set; }

    [ObservableProperty]
    public partial string Username { get; set; }

    /// <summary>The password typed; empty keeps the one saved.</summary>
    [ObservableProperty]
    public partial string Password { get; set; } = "";

    public bool HasSavedPassword { get; }

    public string PasswordHint => HasSavedPassword ? Strings.DatabaseEditor_ASavedPasswordIsKept : Strings.DatabaseEditor_KeptInTheCredentialStore;

    /// <summary>The database made when the data folder is first set up.</summary>
    [ObservableProperty]
    public partial string DatabaseText { get; set; }

    /// <summary>The folder on this computer that holds the data.</summary>
    [ObservableProperty]
    public partial string DataFolderText { get; set; }

    /// <summary>Whether PostgreSQL has already set up the data folder, so the user, password and database are kept as they are in it.</summary>
    public bool HasData { get; }

    public string SetUpHint => HasData ? Strings.DatabaseEditor_TheDataFolderIsSetUp : Strings.DatabaseEditor_TheUserPasswordAndDatabaseAreSetUp;

    [ObservableProperty]
    public partial string? Error { get; set; }

    // A new database's own database and data folder follow its name while they are what the name gave.
    partial void OnNameTextChanged(string oldValue, string newValue)
    {
        if (_original is not null || oldValue is null)
            return;
        if (DatabaseText == PostgresDatabase.DatabaseFor(oldValue))
            DatabaseText = PostgresDatabase.DatabaseFor(newValue);
        if (DataFolderText == PostgresProject.DataFolderFor(_list.DataRoot, oldValue))
            DataFolderText = PostgresProject.DataFolderFor(_list.DataRoot, newValue);
    }

    [RelayCommand]
    private async Task BrowseDataFolderAsync()
    {
        if (_list.PickFolder is not null && await _list.PickFolder(Strings.DatabaseEditor_ChooseTheDataFolder) is { } folder)
            DataFolderText = folder;
    }

    private bool CanSave => _list.CanRun;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private Task SaveAsync() => SaveDatabaseAsync();

    /// <summary>Checks and saves the database. Returns false, with <see cref="Error"/> set, when it cannot be saved.</summary>
    public async Task<bool> SaveDatabaseAsync()
    {
        if (Build(out var problem) is not { } database)
        {
            Error = problem;
            return false;
        }

        Error = await _list.SaveAsync(database, _original, Password.Length > 0 ? Password : null);
        return Error is null;
    }

    [RelayCommand]
    private void Cancel() => _list.CloseEditor();

    private PostgresDatabase? Build(out string? problem)
    {
        var others = _list.Databases.Where(d => d.Id != _original?.Id).ToList();
        var portProblem = MosquittoBroker.ParsePort(PortText, out var port);
        problem = PostgresDatabase.CheckName(NameText, others.Select(d => d.Name))
                  ?? portProblem
                  ?? (port is null ? Strings.DatabaseEditor_EnterAPort : null)
                  ?? PostgresDatabase.CheckPassword(Password);
        if (problem is not null || port is null)
            return null;

        var database = _original?.Clone() ?? new PostgresDatabase();
        database.Name = NameText.Trim();
        database.Port = port.Value;
        database.Username = Username.Trim();
        database.Database = DatabaseText.Trim();
        database.DataFolder = DataFolderText.Trim();

        if (Password.Length > 0 && !_list.CanKeepPasswords)
        {
            problem = Strings.DatabaseEditor_ThisComputerHasNoCredentialStore;
            return null;
        }

        problem = database.Check(others, Password.Length > 0 || HasSavedPassword);
        return problem is null ? database : null;
    }

    internal void BusyChanged() => SaveCommand.NotifyCanExecuteChanged();
}
