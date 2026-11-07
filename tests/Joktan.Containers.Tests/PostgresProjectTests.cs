using System;
using System.Collections.Generic;
using System.IO;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Containers.Tests;

public class PostgresProjectTests : IDisposable
{
    private readonly TempFolder _folder = new();
    private readonly Dictionary<string, string> _passwords = [];

    public void Dispose() => _folder.Dispose();

    private string Project => _folder.At("project");

    private PostgresDatabase Database(string name = "app", int port = 5432, string password = "secret")
    {
        var database = new PostgresDatabase { Name = name, Port = port, Database = PostgresDatabase.DatabaseFor(name), DataFolder = _folder.At("data", name) };
        _passwords[database.Id] = password;
        return database;
    }

    private void Write(params PostgresDatabase[] databases)
        => PostgresProject.Write(Project, "test-postgres", PostgresProject.DefaultImage, databases, d => _passwords.GetValueOrDefault(d.Id));

    private string Read(params string[] parts) => File.ReadAllText(Path.Combine([Project, .. parts]));

    // A path in the compose file is a YAML string in double quotes, where a backslash, as on Windows, is doubled.
    private static string InCompose(string path) => path.Replace("\\", "\\\\");

    [Fact]
    public void A_database_gets_a_service_with_its_data_folder_mounted()
    {
        var database = Database("my-app", 5440);
        Write(database);

        var compose = Read("docker-compose.yml");
        Assert.StartsWith("# Written by Joktan", compose);
        Assert.Contains("name: test-postgres\n", compose);
        Assert.Contains("  my-app:\n    image: docker.io/library/postgres:18\n    restart: unless-stopped\n", compose);
        Assert.Contains("    env_file: ./my-app/postgres.env\n", compose);
        Assert.Contains("      POSTGRES_USER: postgres\n      POSTGRES_DB: my_app\n      PGDATA: /var/lib/postgresql/joktan/data\n", compose);
        Assert.Contains("      - \"5440:5432\"\n", compose);
        Assert.Contains($"      - type: bind\n        source: \"{InCompose(database.DataFolder)}\"\n        target: /var/lib/postgresql/joktan\n", compose);
        Assert.Contains("[\"CMD\", \"pg_isready\", \"-U\", \"postgres\", \"-d\", \"my_app\"]", compose);
        Assert.DoesNotContain("secret", compose);

        Assert.True(Directory.Exists(database.DataFolder));
        Assert.False(PostgresProject.HasData(database.DataFolder));
    }

    [Fact]
    public void The_password_is_only_in_a_file_for_the_user_alone()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "File modes are Unix's.");
        Write(Database(password: "p\"a$s\\s'"));

        Assert.Equal("# Written by Joktan for the database app. Change it on Data / Database.\nPOSTGRES_PASSWORD=\"p\\\"a\\$s\\\\s'\"\n", Read("app", "postgres.env"));
        if (OperatingSystem.IsWindows())
            return;
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(Project, "app", "postgres.env")));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(Path.Combine(Project, "app")));
    }

    [Fact]
    public void A_path_with_quotes_or_dollars_is_kept_as_it_is()
    {
        // Windows does not allow a quote in a folder's name.
        var (name, written) = OperatingSystem.IsWindows() ? ("weird $HOME", "weird $$HOME") : ("we\"ird $HOME", "we\\\"ird $$HOME");
        var database = Database();
        database.DataFolder = _folder.At(name);
        Write(database);

        Assert.Contains("        source: \"" + InCompose(_folder.At("") + Path.DirectorySeparatorChar) + written + "\"\n", Read("docker-compose.yml"));
        Assert.True(Directory.Exists(database.DataFolder));
    }

    [Fact]
    public void Unchanged_files_are_left_alone()
    {
        var database = Database();
        Write(database);
        var compose = Path.Combine(Project, "docker-compose.yml");
        var environment = Path.Combine(Project, "app", "postgres.env");
        var then = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(compose, then);
        File.SetLastWriteTimeUtc(environment, then);

        Write(database);
        Assert.Equal(then, File.GetLastWriteTimeUtc(compose));
        Assert.Equal(then, File.GetLastWriteTimeUtc(environment));

        _passwords[database.Id] = "changed";
        Write(database);
        Assert.Contains("changed", Read("app", "postgres.env"));
    }

    [Fact]
    public void A_database_taken_away_loses_its_files_but_never_its_data()
    {
        var keep = Database("keep");
        var gone = Database("gone", 5433);
        Write(keep, gone);
        File.WriteAllText(Path.Combine(gone.DataFolder, "PG_VERSION"), "18");
        Directory.CreateDirectory(Path.Combine(Project, "mine"));
        File.WriteAllText(Path.Combine(Project, "mine", "notes.txt"), "not Joktan's");

        Write(keep);

        Assert.False(Directory.Exists(Path.Combine(Project, "gone")));
        Assert.True(File.Exists(Path.Combine(gone.DataFolder, "PG_VERSION")));
        Assert.True(File.Exists(Path.Combine(Project, "mine", "notes.txt")));
        Assert.DoesNotContain("gone", Read("docker-compose.yml"));
    }

    [Fact]
    public void Deleting_leaves_the_data_and_anything_not_written_here()
    {
        var database = Database();
        Write(database);
        File.WriteAllText(Path.Combine(Project, "README"), "mine");

        PostgresProject.Delete(Project);

        Assert.False(File.Exists(Path.Combine(Project, "docker-compose.yml")));
        Assert.False(Directory.Exists(Path.Combine(Project, "app")));
        Assert.True(File.Exists(Path.Combine(Project, "README")));
        Assert.True(Directory.Exists(database.DataFolder));

        File.Delete(Path.Combine(Project, "README"));
        PostgresProject.Delete(Project);
        Assert.False(Directory.Exists(Project));
        PostgresProject.Delete(Project);
    }

    [Fact]
    public void No_databases_is_an_empty_project()
    {
        Write();
        Assert.EndsWith("services: {}\n", Read("docker-compose.yml"));
    }

    [Fact]
    public void Problems_are_reported_and_nothing_is_written()
    {
        var database = Database();
        _passwords.Remove(database.Id);
        Assert.Equal("app: Enter a password.", Assert.Throws<FormatException>(() => Write(database)).Message);

        _passwords[database.Id] = "two\nlines";
        Assert.StartsWith("app: The password cannot", Assert.Throws<FormatException>(() => Write(database)).Message);

        Assert.Throws<FormatException>(() => PostgresProject.Write(Project, "Bad", PostgresProject.DefaultImage, [], _ => null));
        Assert.Throws<FormatException>(() => PostgresProject.Write(Project, "ok", "has space", [], _ => null));
        Assert.False(Directory.Exists(Project));
    }

    [Fact]
    public void A_folder_that_cannot_be_written_is_reported()
    {
        var file = _folder.Write("file", "");
        var message = Assert.Throws<FormatException>(() => PostgresProject.Write(Path.Combine(file, "project"), "ok", PostgresProject.DefaultImage, [], _ => null)).Message;
        Assert.StartsWith($"The databases' files cannot be written to {Path.Combine(file, "project")}", message);

        var database = Database();
        database.DataFolder = Path.Combine(file, "data");
        message = Assert.Throws<FormatException>(() => Write(database)).Message;
        Assert.StartsWith($"The data folder {database.DataFolder} cannot be made", message);
    }

    [Fact]
    public void A_set_up_data_folder_is_recognised()
    {
        var folder = _folder.At("set-up");
        Assert.False(PostgresProject.HasData(folder));
        _folder.Write("set-up/data/PG_VERSION", "18\n");
        Assert.True(PostgresProject.HasData(folder));
        Assert.Equal(Path.Combine("/root", "name"), PostgresProject.DataFolderFor("/root", " name "));
    }
}
