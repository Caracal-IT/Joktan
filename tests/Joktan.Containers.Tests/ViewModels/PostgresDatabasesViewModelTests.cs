using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Joktan.Containers.ViewModels;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Containers.Tests.ViewModels;

public class PostgresDatabasesViewModelTests : FakeEngineTest
{
    private readonly FakeCredentialStore _credentials = new();

    private string DatabasesFolder => Folder.At("databases");

    private string DatabasesFile => Path.Combine(DatabasesFolder, "docker-compose.yml");

    private string DataRoot => Folder.At("data-root");

    private ContainersViewModel DatabasePage(Action<ComposeSettings>? change = null)
    {
        var settings = new ComposeSettings
        {
            ComposeFiles = [], Engine = ContainerEngine.Docker, DatabasesFolder = DatabasesFolder, DatabasesDataRoot = DataRoot,
        };
        change?.Invoke(settings);
        settings.Save(DataFolder);
        return new ContainersViewModel(DataFolder, credentials: _credentials);
    }

    private PostgresDatabase Saved(string name, int port = 5432, string password = "secret")
    {
        var database = new PostgresDatabase { Name = name, Port = port, Database = name, DataFolder = Path.Combine(DataRoot, name) };
        _credentials.Saved[database.Account] = password;
        return database;
    }

    private void Running(string service, string state = "running")
    {
        Answer("services", $"{service}\n");
        Answer("ps", $$"""{"Service":"{{service}}","Name":"p-{{service}}-1","State":"{{state}}","Status":"Up 1 minute (healthy)"}""" + "\n");
    }

    private static async Task<bool> Save(DatabaseEditorViewModel editor)
    {
        var saved = await editor.SaveDatabaseAsync();
        Headless.Settle();
        return saved;
    }

    [Fact]
    public Task Adding_a_database_writes_its_file_makes_its_data_folder_and_puts_it_on_the_page() => Headless.Run(async () =>
    {
        var page = DatabasePage();
        var databases = page.Databases;
        Assert.False(databases.HasDatabases);
        Assert.Equal($"New databases keep their data in a folder of their own in {DataRoot}.", databases.DataRootInfo);

        databases.AddCommand.Execute(null);
        var editor = databases.Editor!;
        Assert.True(editor.IsNew);
        Assert.Equal("New database", editor.Title);
        Assert.Equal(("postgres", "5432", "postgres", "postgres"), (editor.NameText, editor.PortText, editor.Username, editor.DatabaseText));
        Assert.Equal(Path.Combine(DataRoot, "postgres"), editor.DataFolderText);
        Assert.StartsWith("The username, password and database are set up", editor.SetUpHint);
        Assert.False(databases.AddCommand.CanExecute(null));

        // The database and data folder follow the name until they are typed in.
        editor.NameText = "shop-db";
        Assert.Equal(("shop_db", Path.Combine(DataRoot, "shop-db")), (editor.DatabaseText, editor.DataFolderText));
        editor.DatabaseText = "orders";
        editor.NameText = "shop";
        Assert.Equal(("orders", Path.Combine(DataRoot, "shop")), (editor.DatabaseText, editor.DataFolderText));

        Assert.False(await Save(editor));
        Assert.Equal("Enter a password.", editor.Error);
        editor.Password = "secret";
        Assert.True(await Save(editor));

        Assert.Null(databases.Editor);
        var row = Assert.Single(databases.Items);
        Assert.Equal(("shop", "postgres://postgres@localhost:5432/orders", $"Data in {Path.Combine(DataRoot, "shop")}"), (row.Name, row.ConnectionText, row.DataFolderText));
        Assert.Equal("Not created", row.StatusText);
        Assert.True(row.CanStart);
        Assert.True(Directory.Exists(Path.Combine(DataRoot, "shop")));
        Assert.Contains("  shop:", File.ReadAllText(DatabasesFile));
        Assert.Equal("secret", _credentials.Saved[databases.Databases[0].Account]);
        Assert.DoesNotContain("secret", File.ReadAllText(ComposeSettings.PathIn(DataFolder)));
        Assert.Equal(DatabasesFile, Assert.Single(page.Projects).File);
        Assert.Equal("shop", Assert.Single(ComposeSettings.Load(DataFolder).Databases).Name);
    });

    [Fact]
    public Task A_second_database_gets_a_free_name_and_port() => Headless.Run(async () =>
    {
        var page = DatabasePage(s => s.Databases = [Saved("postgres")]);

        page.Databases.AddCommand.Execute(null);

        Assert.Equal(("postgres-2", "5433", "postgres_2"), (page.Databases.Editor!.NameText, page.Databases.Editor.PortText, page.Databases.Editor.DatabaseText));
        page.Databases.Editor.PortText = "5432";
        page.Databases.Editor.Password = "x";
        Assert.False(await Save(page.Databases.Editor));
        Assert.Equal("Port 5432 is used by postgres.", page.Databases.Editor.Error);

        page.Databases.Editor.PortText = "";
        Assert.False(await Save(page.Databases.Editor));
        Assert.Equal("Enter a port.", page.Databases.Editor.Error);

        page.Databases.Editor.CancelCommand.Execute(null);
        Assert.Null(page.Databases.Editor);
        Assert.Single(page.Databases.Items);
    });

    [Fact]
    public Task Editing_keeps_the_saved_password_and_says_when_the_data_is_set_up() => Headless.Run(async () =>
    {
        var database = Saved("app");
        var page = DatabasePage(s => s.Databases = [database]);
        Folder.Write("data-root/app/data/PG_VERSION", "18\n");

        page.Databases.Items[0].EditCommand.Execute(null);
        var editor = page.Databases.Editor!;
        Assert.Equal("Edit app", editor.Title);
        Assert.True(editor.HasSavedPassword);
        Assert.StartsWith("A password is saved", editor.PasswordHint);
        Assert.StartsWith("The data folder already holds a database", editor.SetUpHint);

        // A database being changed does not follow its name.
        editor.NameText = "renamed";
        Assert.Equal(Path.Combine(DataRoot, "app"), editor.DataFolderText);
        Assert.True(await Save(editor));
        Assert.Equal("secret", _credentials.Saved[database.Account]);
        Assert.Equal("renamed", page.Databases.Databases[0].Name);
        Assert.Equal(Path.Combine(DataRoot, "app"), page.Databases.Databases[0].DataFolder);
    });

    [Fact]
    public Task A_password_that_cannot_be_kept_is_not_saved() => Headless.Run(async () =>
    {
        _credentials.FailWrites = true;
        var page = DatabasePage();
        page.Databases.AddCommand.Execute(null);
        page.Databases.Editor!.Password = "secret";

        Assert.False(await Save(page.Databases.Editor));
        Assert.Equal("The password could not be kept in the credential store.", page.Databases.Editor.Error);
        Assert.Empty(page.Databases.Items);
    });

    [Fact]
    public Task Without_a_credential_store_a_password_cannot_be_given() => Headless.Run(async () =>
    {
        new ComposeSettings { ComposeFiles = [], Engine = ContainerEngine.Docker, DatabasesFolder = DatabasesFolder, DatabasesDataRoot = DataRoot }.Save(DataFolder);
        var page = new ContainersViewModel(DataFolder, credentials: new FakeCredentialStore(isAvailable: false));
        page.Databases.AddCommand.Execute(null);
        page.Databases.Editor!.Password = "secret";

        Assert.False(await Save(page.Databases.Editor));
        Assert.Equal("This computer has no credential store to keep the password in.", page.Databases.Editor.Error);
    });

    [Fact]
    public Task A_database_starts_stops_and_shows_its_containers_state() => Headless.Run(async () =>
    {
        var page = DatabasePage(s => s.Databases = [Saved("app")]);
        var row = page.Databases.Items[0];

        await row.StartCommand.ExecuteAsync(null);
        Assert.Null(page.Databases.Error);
        Assert.Contains(" up -d app\n", Calls);
        Assert.True(File.Exists(DatabasesFile));
        Assert.True(Directory.Exists(Path.Combine(DataRoot, "app")));

        Running("app");
        await page.Databases.Project!.RefreshStatusAsync();
        Assert.True(row.IsRunning);
        Assert.False(row.CanStart);
        Assert.Equal("Up 1 minute (healthy)", row.StatusText);

        await row.StopCommand.ExecuteAsync(null);
        Assert.Contains(" stop app\n", Calls);

        Answer("command-exit", "1");
        await row.StopCommand.ExecuteAsync(null);
        Assert.NotNull(page.Databases.Error);
    });

    [Fact]
    public Task Starting_writes_the_files_again_so_a_changed_password_is_used() => Headless.Run(async () =>
    {
        var database = Saved("app");
        var page = DatabasePage(s =>
        {
            s.ComposeFiles = [DatabasesFile];
            s.Databases = [database];
        });
        Assert.Null(page.Databases.Write());
        _credentials.Saved[database.Account] = "newer";

        Assert.True(await page.RunCommandAsync(page.Projects[0], "Starting", ["up", "-d"]));
        Assert.Contains("\"newer\"", File.ReadAllText(Path.Combine(DatabasesFolder, "app", "postgres.env")));

        // Without its password it cannot be written, so it is not started.
        _credentials.Saved.Clear();
        Assert.False(await page.RunCommandAsync(page.Projects[0], "Starting", ["up", "-d"]));
        Assert.Equal("app: Enter a password.", page.Projects[0].Error);
    });

    [Fact]
    public Task Saving_a_running_database_starts_it_again_and_renaming_removes_its_old_container() => Headless.Run(async () =>
    {
        var page = DatabasePage(s =>
        {
            s.ComposeFiles = [DatabasesFile];
            s.Databases = [Saved("old")];
        });
        Assert.Null(page.Databases.Write());
        Running("old");
        await page.Projects[0].RefreshStatusAsync();

        page.Databases.Items[0].EditCommand.Execute(null);
        page.Databases.Editor!.NameText = "new";
        page.Databases.Editor.PortText = "5999";
        Assert.True(await Save(page.Databases.Editor));

        Assert.Contains(" rm --stop --force old\n", Calls);
        Assert.Contains(" up -d new\n", Calls);
        Assert.Contains("\"5999:5432\"", File.ReadAllText(DatabasesFile));
        Assert.False(Directory.Exists(Path.Combine(DatabasesFolder, "old")));
    });

    [Fact]
    public Task A_rename_that_cannot_remove_the_old_container_saves_nothing() => Headless.Run(async () =>
    {
        var page = DatabasePage(s =>
        {
            s.ComposeFiles = [DatabasesFile];
            s.Databases = [Saved("old")];
        });
        Assert.Null(page.Databases.Write());
        Running("old", "exited");
        await page.Projects[0].RefreshStatusAsync();
        Answer("command-exit", "1");

        page.Databases.Items[0].EditCommand.Execute(null);
        page.Databases.Editor!.NameText = "new";
        Assert.False(await Save(page.Databases.Editor));
        Assert.Equal("old", page.Databases.Databases[0].Name);
    });

    [Fact]
    public Task Removing_asks_first_then_removes_the_container_and_password_but_keeps_the_data() => Headless.Run(async () =>
    {
        var keep = Saved("keep", 5433);
        var gone = Saved("gone");
        var page = DatabasePage(s =>
        {
            s.ComposeFiles = [DatabasesFile];
            s.Databases = [keep, gone];
        });
        Assert.Null(page.Databases.Write());
        Folder.Write("data-root/gone/data/PG_VERSION", "18\n");
        Running("gone");
        await page.Projects[0].RefreshStatusAsync();

        var row = page.Databases.Items[1];
        row.RemoveCommand.Execute(null);
        Assert.True(row.IsConfirmingRemove);
        Assert.Equal($"Remove gone? Its container is stopped and removed, and its password forgotten. Its data stays in {gone.DataFolder}.", row.RemoveText);
        row.CancelRemoveCommand.Execute(null);
        Assert.False(row.IsConfirmingRemove);

        await row.ConfirmRemoveCommand.ExecuteAsync(null);

        Assert.Contains(" rm --stop --force gone\n", Calls);
        Assert.Equal("keep", Assert.Single(page.Databases.Items).Name);
        Assert.False(_credentials.Saved.ContainsKey(gone.Account));
        Assert.True(File.Exists(Path.Combine(gone.DataFolder, "data", "PG_VERSION")));
        Assert.DoesNotContain("gone", File.ReadAllText(DatabasesFile));

        // The last one takes the compose file and its group with it, but not its data.
        Answer("services", "");
        Answer("ps", "");
        await page.Projects[0].RefreshStatusAsync();
        await page.Databases.Items[0].ConfirmRemoveCommand.ExecuteAsync(null);
        Assert.Empty(page.Projects);
        Assert.False(File.Exists(DatabasesFile));
        Assert.True(Directory.Exists(keep.DataFolder));
    });

    [Fact]
    public Task A_remove_that_fails_keeps_the_database() => Headless.Run(async () =>
    {
        var page = DatabasePage(s =>
        {
            s.ComposeFiles = [DatabasesFile];
            s.Databases = [Saved("app")];
        });
        Assert.Null(page.Databases.Write());
        Running("app");
        await page.Projects[0].RefreshStatusAsync();
        Answer("command-exit", "1");

        await page.Databases.Items[0].ConfirmRemoveCommand.ExecuteAsync(null);

        Assert.NotNull(page.Databases.Error);
        Assert.Single(page.Databases.Items);
    });

    [Fact]
    public Task Commands_wait_while_another_runs() => Headless.Run(async () =>
    {
        var page = DatabasePage(s => s.Databases = [Saved("app")]);
        page.Databases.AddCommand.Execute(null);

        page.IsBusy = true;
        Assert.False(page.Databases.Items[0].StartCommand.CanExecute(null));
        Assert.False(page.Databases.Editor!.SaveCommand.CanExecute(null));
        Assert.False(page.Databases.ApplyLocationCommand.CanExecute(null));
        page.IsBusy = false;
        Assert.True(page.Databases.Items[0].StartCommand.CanExecute(null));
        await Task.CompletedTask;
    });

    [Fact]
    public Task The_data_root_is_saved_once_valid_and_used_for_new_databases() => Headless.Run(async () =>
    {
        var page = DatabasePage();
        var databases = page.Databases;
        Assert.Equal(DataRoot, databases.DataRootText);

        databases.DataRootText = "relative";
        Assert.Equal("Enter the data folder's full path.", databases.DataRootError);
        Assert.Equal(DataRoot, ComposeSettings.Load(DataFolder).DatabasesDataRoot);

        var other = Folder.At("elsewhere");
        databases.DataRootText = other;
        Assert.Null(databases.DataRootError);
        Assert.Equal(other, ComposeSettings.Load(DataFolder).DatabasesDataRoot);
        databases.AddCommand.Execute(null);
        Assert.Equal(Path.Combine(other, "postgres"), databases.Editor!.DataFolderText);

        // Empty is the default, in Joktan's data folder.
        databases.DataRootText = "";
        Assert.Equal(PostgresProject.DefaultDataRoot, databases.DataRoot);
        Assert.Equal("", ComposeSettings.Load(DataFolder).DatabasesDataRoot);

        databases.PickFolder = _ => Task.FromResult<string?>(other);
        await databases.BrowseDataRootCommand.ExecuteAsync(null);
        Assert.Equal(other, databases.DataRootText);
        await databases.Editor.BrowseDataFolderCommand.ExecuteAsync(null);
        Assert.Equal(other, databases.Editor.DataFolderText);
        await databases.BrowseFolderCommand.ExecuteAsync(null);
        Assert.Equal(other, databases.FolderText);
    });

    [Fact]
    public Task The_image_is_saved_once_valid() => Headless.Run(async () =>
    {
        var page = DatabasePage();

        page.Databases.ImageText = "has space";
        Assert.NotNull(page.Databases.ImageError);
        Assert.Equal(PostgresProject.DefaultImage, ComposeSettings.Load(DataFolder).PostgresImage);

        page.Databases.ImageText = "docker.io/library/postgres:17";
        Assert.Null(page.Databases.ImageError);
        Assert.Equal("docker.io/library/postgres:17", ComposeSettings.Load(DataFolder).PostgresImage);
        await Task.CompletedTask;
    });

    [Fact]
    public Task Applying_a_new_location_moves_the_files_but_not_the_data() => Headless.Run(async () =>
    {
        var database = Saved("app");
        var page = DatabasePage(s =>
        {
            s.ComposeFiles = [DatabasesFile];
            s.Databases = [database];
        });
        Assert.Null(page.Databases.Write());
        await page.Projects[0].RefreshStatusAsync();
        Assert.False(page.Databases.HasLocationChanges);

        page.Databases.ProjectText = "Bad";
        Assert.NotNull(page.Databases.LocationError);
        await page.Databases.ApplyLocationCommand.ExecuteAsync(null);
        Assert.Equal(PostgresProject.DefaultName, ComposeSettings.Load(DataFolder).DatabasesProject);

        // Not while it has containers.
        Running("app");
        page.Databases.ProjectText = "moved";
        await page.Databases.ApplyLocationCommand.ExecuteAsync(null);
        Assert.StartsWith("Take ", page.Databases.LocationError);

        Answer("services", "app\n");
        Answer("ps", "");
        var moved = Folder.At("moved");
        page.Databases.FolderText = moved;
        await page.Databases.ApplyLocationCommand.ExecuteAsync(null);

        Assert.Null(page.Databases.LocationError);
        Assert.Equal(("moved", moved), (ComposeSettings.Load(DataFolder).DatabasesProject, ComposeSettings.Load(DataFolder).DatabasesFolder));
        Assert.False(File.Exists(DatabasesFile));
        Assert.Contains("name: moved\n", File.ReadAllText(Path.Combine(moved, "docker-compose.yml")));
        Assert.Equal(Path.Combine(moved, "docker-compose.yml"), Assert.Single(page.Projects).File);
        Assert.True(Directory.Exists(database.DataFolder));
        Assert.Equal($"Written to {Path.Combine(moved, "docker-compose.yml")}.", page.Databases.WrittenToText);
    });

    [Fact]
    public Task A_database_that_cannot_be_written_is_saved_and_says_why() => Headless.Run(async () =>
    {
        var file = Folder.Write("a-file", "");
        var page = DatabasePage();
        page.Databases.AddCommand.Execute(null);
        page.Databases.Editor!.Password = "secret";
        page.Databases.Editor.DataFolderText = Path.Combine(file, "data");

        Assert.True(await Save(page.Databases.Editor));

        Assert.StartsWith("The data folder", page.Databases.Error);
        Assert.Single(page.Databases.Items);
        Assert.Empty(page.Projects);
    });

    [Fact]
    public Task Brokers_and_databases_live_side_by_side() => Headless.Run(async () =>
    {
        var page = DatabasePage(s =>
        {
            s.BrokersFolder = Folder.At("brokers");
            s.Brokers = [new MosquittoBroker { Name = "mosquitto", Port = 1883 }];
            s.Databases = [Saved("app")];
        });

        await page.Brokers.Items[0].StartCommand.ExecuteAsync(null);
        await page.Databases.Items[0].StartCommand.ExecuteAsync(null);

        Assert.Equal([page.Brokers.ComposeFile, DatabasesFile], page.Projects.Select(p => p.File));
        Assert.False(page.Databases.IsTheirs(page.Projects[0]));
        Assert.True(page.Databases.IsTheirs(page.Projects[1]));
    });
}
