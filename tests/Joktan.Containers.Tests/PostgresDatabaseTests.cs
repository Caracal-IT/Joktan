using System;
using System.IO;
using System.Text.Json;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Containers.Tests;

public class PostgresDatabaseTests
{
    private static PostgresDatabase Valid(string name = "app", int port = 5432, string? folder = null) => new()
    {
        Name = name, Port = port, Database = name, DataFolder = folder ?? Path.Combine(Path.GetTempPath(), "joktan-tests", name),
    };

    [Theory]
    [InlineData("app", null)]
    [InlineData("my-db_2", null)]
    [InlineData("", "Enter a name for the database.")]
    [InlineData("App", "Use lower-case letters, digits, dashes and underscores, starting with a letter or digit.")]
    [InlineData("-app", "Use lower-case letters, digits, dashes and underscores, starting with a letter or digit.")]
    [InlineData("taken", "A database is already called taken.")]
    public void Names_must_suit_a_compose_service(string name, string? problem)
        => Assert.Equal(problem, PostgresDatabase.CheckName(name, ["taken"]));

    [Fact]
    public void A_name_can_be_at_most_forty_characters()
        => Assert.Equal("The name can be at most 40 characters.", PostgresDatabase.CheckName(new string('a', 41), []));

    [Theory]
    [InlineData("postgres", null)]
    [InlineData("_app_2", null)]
    [InlineData("", "Enter a username.")]
    [InlineData("2app", "For the username, use lower-case letters, digits and underscores, starting with a letter or underscore.")]
    [InlineData("my-user", "For the username, use lower-case letters, digits and underscores, starting with a letter or underscore.")]
    public void Users_and_databases_need_no_quoting_in_sql(string name, string? problem)
        => Assert.Equal(problem, PostgresDatabase.CheckIdentifier(name, "username"));

    [Fact]
    public void A_user_or_database_can_be_at_most_63_characters()
        => Assert.Equal("The database can be at most 63 characters.", PostgresDatabase.CheckIdentifier(new string('a', 64), "database"));

    [Fact]
    public void The_database_follows_the_name_with_underscores_for_dashes()
        => Assert.Equal("my_app_db", PostgresDatabase.DatabaseFor(" my-app-db "));

    [Theory]
    [InlineData("p@ss w'o\"r$d\\", null)]
    [InlineData("two\nlines", "The password cannot have line breaks or other control characters.")]
    public void Passwords_are_one_line(string password, string? problem)
        => Assert.Equal(problem, PostgresDatabase.CheckPassword(password));

    [Fact]
    public void The_data_folder_must_be_a_full_path_to_a_folder()
    {
        using var folder = new TempFolder();
        var file = folder.Write("file", "");

        Assert.Equal("Choose a folder for the data.", PostgresDatabase.CheckDataFolder(" "));
        Assert.Equal("Enter the data folder's full path.", PostgresDatabase.CheckDataFolder("relative/data"));
        Assert.Equal($"{file} is a file, not a folder.", PostgresDatabase.CheckDataFolder(file));
        Assert.Null(PostgresDatabase.CheckDataFolder(folder.At("new")));
    }

    [Fact]
    public void Another_databases_port_or_data_folder_is_refused()
    {
        var other = Valid("other");

        Assert.Equal("Port 5432 is used by other.", Valid().Check([other], hasPassword: true));
        Assert.Equal("The data folder is used by other.", Valid(port: 5433, folder: other.DataFolder + Path.DirectorySeparatorChar).Check([other], hasPassword: true));
        Assert.Null(Valid(port: 5433).Check([other], hasPassword: true));
    }

    [Fact]
    public void A_database_needs_a_password_a_port_and_names_postgres_accepts()
    {
        Assert.Equal("Enter a password.", Valid().Check([], hasPassword: false));
        Assert.Equal("70000 is not a port: use a number from 1 to 65535.", Valid(port: 70000).Check([], hasPassword: true));

        var badUser = Valid();
        badUser.Username = "Admin";
        Assert.Contains("username", badUser.Check([], hasPassword: true));

        var noFolder = Valid();
        noFolder.DataFolder = "";
        Assert.Equal("Choose a folder for the data.", noFolder.Check([], hasPassword: true));
    }

    [Fact]
    public void It_says_how_to_connect_and_where_its_password_is_kept()
    {
        var database = Valid("shop", 5440);
        database.Username = "admin";

        Assert.Equal("postgres://admin@localhost:5440/shop", database.ConnectionText);
        Assert.Equal($"postgres:{database.Id}", database.Account);
        Assert.DoesNotContain("Account", JsonSerializer.Serialize(database), StringComparison.Ordinal);

        var copy = database.Clone();
        copy.Name = "changed";
        Assert.Equal("shop", database.Name);
    }
}
