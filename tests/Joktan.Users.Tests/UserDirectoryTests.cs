using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Joktan.Tests.TestSupport;
using Joktan.Users.Model;
using Xunit;

namespace Joktan.Users.Tests;

public class UserDirectoryTests : UserTest
{
    [Fact]
    public Task Create_adds_a_database_with_the_username_and_password_given_starts_it_and_seeds_it() => Headless.Run(async () =>
    {
        Assert.False(Directory.HasDatabase);
        Data.Unreachable = 3;
        await Created("pa$$");

        var database = Assert.Single(Containers.Databases.Databases);
        Assert.Equal(("users", "users", "joktan"), (database.Name, database.Database, database.Username));
        Assert.Equal("pa$$", Credentials.Saved[database.Account]);
        Assert.Equal(database.Id, Settings.DatabaseId);
        Assert.Equal(1, Saves);
        Assert.Contains("up -d users", Calls);
        Assert.Contains("users:", File.ReadAllText(DatabasesFile));

        // Tried again while the server set itself up, with the password given.
        Assert.Equal(4, Data.Opened.Count);
        Assert.All(Data.Opened, o => Assert.Equal(("users", "pa$$"), o));
        Assert.True(Directory.IsLoaded);
        Assert.Null(Directory.Error);
        Assert.Equal(RecommendedRoles.Administrator, Directory.Roles[0].Name);
        Assert.Equal(RecommendedRoles.All.Select(r => r.Name).Order(), Directory.Roles.Select(r => r.Name).Order());
        var admin = Assert.Single(Directory.Users);
        Assert.Equal(("Default Administrator", "admin", false), (admin.FullName, admin.Username, admin.HasPassword));
        Assert.Equal(RecommendedRoles.Administrator, Directory.RoleOf(admin)!.Name);
    });

    [Fact]
    public Task Create_says_what_is_wrong_with_the_form_and_adds_nothing() => Headless.Run(async () =>
    {
        Assert.Equal("Enter a password.", await Directory.CreateDatabaseAsync("users", 5432, "joktan", ""));
        Assert.StartsWith("Use lower-case letters", await Directory.CreateDatabaseAsync("Users!", 5432, "joktan", "x"));
        Assert.Contains("username", await Directory.CreateDatabaseAsync("users", 5432, "Bad Name", "x"));
        Assert.Empty(Containers.Databases.Databases);
        Assert.False(Directory.HasDatabase);
    });

    [Fact]
    public Task A_name_whose_data_folder_holds_a_database_from_before_is_not_used() => Headless.Run(async () =>
    {
        // As after the users database was removed on Data / Database: its data folder stays, with its old password.
        Folder.Write("data-root/users/data/PG_VERSION", "18");
        Assert.True(Directory.HasOldData("users"));
        Assert.Equal("users-2", Directory.FreeName());

        var problem = await Directory.CreateDatabaseAsync("users", 5432, "joktan", "pw");
        Assert.StartsWith(Path.Combine(Folder.At("data-root"), "users") + " already holds a database from before", problem);
        Assert.Empty(Containers.Databases.Databases);

        var form = new Joktan.Users.ViewModels.CreateDatabaseViewModel(Directory, () => { });
        Assert.Null(form.OldDataWarning);
        form.NameText = "users";
        Assert.NotNull(form.OldDataWarning);
    });

    [Fact]
    public Task A_database_that_does_not_come_up_in_time_is_reported() => Headless.Run(async () =>
    {
        var slow = new UserDirectory(Settings, Containers.Databases, (_, _) => new FakeUserStore(Data), (_, _) => Task.Delay(50))
        {
            StartTimeout = System.TimeSpan.FromMilliseconds(300),
        };
        Data.Unreachable = 1000;
        var problem = await slow.CreateDatabaseAsync("users", 5440, "joktan", "x");
        Assert.StartsWith("The database users did not start in time", problem);
        Assert.True(slow.HasDatabase);
        Assert.False(slow.IsLoaded);
        Assert.False(slow.IsBusy);
    });

    [Fact]
    public Task The_administrator_role_stays_and_someone_always_has_it() => Headless.Run(async () =>
    {
        await Created();
        var adminRole = Directory.AdministratorRole!;
        var admin = Directory.Users[0];

        Assert.Equal("The Administrator role cannot be removed.", await Directory.DeleteRoleAsync(adminRole));
        Assert.Equal("The Administrator role keeps its name; its description can change.",
            await Directory.SaveRoleAsync(new Role { Id = adminRole.Id, Name = "Boss", BuiltIn = true }));
        Assert.Null(await Directory.SaveRoleAsync(new Role { Id = adminRole.Id, Name = adminRole.Name, Description = "Runs it all", BuiltIn = true }));
        Assert.Equal("Runs it all", Directory.AdministratorRole!.Description);

        Assert.True(Directory.IsLastAdministrator(admin.Id));
        Assert.StartsWith("This is the only user with the Administrator role, so", await Directory.DeleteUserAsync(admin));
        var demoted = admin.Clone();
        demoted.RoleId = Directory.Roles.First(r => r.Name == "Viewer").Id;
        Assert.StartsWith("This is the only user with the Administrator role. Give", await Directory.SaveUserAsync(demoted));

        // With a second administrator, the first can go.
        Assert.Null(await Directory.SaveUserAsync(new User { Username = "ann", NewPassword = "pw", FirstName = "Ann", LastName = "Lee", RoleId = adminRole.Id }));
        Assert.False(Directory.IsLastAdministrator(admin.Id));
        Assert.Null(await Directory.DeleteUserAsync(admin));
        Assert.Equal(["Ann Lee"], Directory.Users.Select(u => u.FullName));
    });

    [Fact]
    public Task Roles_have_unique_names_and_one_in_use_is_not_removed() => Headless.Run(async () =>
    {
        await Created();
        Assert.Equal("Give the role a name.", await Directory.SaveRoleAsync(new Role { Name = " " }));
        Assert.Equal("There is already a role called approver.", await Directory.SaveRoleAsync(new Role { Name = "approver" }));
        Assert.Null(await Directory.SaveRoleAsync(new Role { Name = "Auditor", Description = "Checks the books" }));
        var auditor = Directory.Roles.Single(r => r.Name == "Auditor");

        Assert.Null(await Directory.SaveUserAsync(new User { Username = "bo", NewPassword = "pw", FirstName = "Bo", LastName = "Ng", RoleId = auditor.Id }));
        Assert.Equal("One user has the role Auditor. Give them another role first.", await Directory.DeleteRoleAsync(auditor));
        Assert.Null(await Directory.DeleteUserAsync(Directory.Users.Single(u => u.FirstName == "Bo")));
        Assert.Null(await Directory.DeleteRoleAsync(Directory.Roles.Single(r => r.Name == "Auditor")));
        Assert.DoesNotContain(Directory.Roles, r => r.Name == "Auditor");
    });

    [Fact]
    public Task Users_need_a_unique_username_a_password_both_names_and_a_role() => Headless.Run(async () =>
    {
        await Created();
        var role = Directory.Roles.First(r => !r.BuiltIn).Id;
        User Ann() => new() { Username = "ann", NewPassword = "pw", FirstName = "Ann", LastName = "Lee", RoleId = role };

        Assert.Equal("Enter a username.", await Directory.SaveUserAsync(new User { FirstName = "Ann", LastName = "Lee", RoleId = role }));
        var bad = Ann();
        bad.Username = "ann lee";
        Assert.Equal("Use letters, digits and . _ - @ in the username.", await Directory.SaveUserAsync(bad));
        bad.Username = "ADMIN";
        Assert.Equal("There is already a user called ADMIN.", await Directory.SaveUserAsync(bad));
        bad = Ann();
        bad.Email = "ann@";
        Assert.StartsWith("Enter an email address", await Directory.SaveUserAsync(bad));
        bad = Ann();
        bad.NewPassword = null;
        Assert.Equal("Enter a password for the user.", await Directory.SaveUserAsync(bad));
        bad = Ann();
        bad.FirstName = "";
        Assert.Equal("Enter the first name.", await Directory.SaveUserAsync(bad));
        bad = Ann();
        bad.LastName = " ";
        Assert.Equal("Enter the last name.", await Directory.SaveUserAsync(bad));
        bad = Ann();
        bad.RoleId = 0;
        Assert.Equal("Choose a role.", await Directory.SaveUserAsync(bad));

        var good = Ann();
        good.FirstName = " Ann ";
        good.Email = " ann@example.com ";
        good.Description = "Finance";
        good.Notes = "Mornings";
        Assert.Null(await Directory.SaveUserAsync(good));
        var ann = Directory.Users.Single(u => u.Username == "ann");
        Assert.Equal(("Ann", "ann@example.com", "Finance", "Mornings", true), (ann.FirstName, ann.Email, ann.Description, ann.Notes, ann.HasPassword));
        Assert.True(PasswordHasher.Verify("pw", Data.Hashes[ann.Id]));

        // Changed without a password, the one set is kept.
        ann.Description = "Head of finance";
        Assert.Null(await Directory.SaveUserAsync(ann));
        Assert.True(PasswordHasher.Verify("pw", Data.Hashes[ann.Id]));
        Assert.True(Directory.Users.Single(u => u.Username == "ann").HasPassword);
    });

    [Fact]
    public Task Recommended_roles_that_were_removed_can_be_added_again() => Headless.Run(async () =>
    {
        await Created();
        Assert.Empty(Directory.MissingRecommendedRoles);
        Assert.Null(await Directory.DeleteRoleAsync(Directory.Roles.Single(r => r.Name == "Viewer")));
        Assert.Null(await Directory.DeleteRoleAsync(Directory.Roles.Single(r => r.Name == "Support")));
        Assert.Equal(["Support", "Viewer"], Directory.MissingRecommendedRoles.Select(r => r.Name).Order());

        Assert.Null(await Directory.AddRecommendedRolesAsync());
        Assert.Empty(Directory.MissingRecommendedRoles);
        Assert.Equal(RecommendedRoles.All.Count, Directory.Roles.Count);
    });

    [Fact]
    public Task A_stopped_database_is_reported_and_can_be_started() => Headless.Run(async () =>
    {
        await Created();
        Data.Unreachable = 1;
        Assert.False(await Directory.RefreshAsync());
        Assert.StartsWith("The users database users could not be reached.", Directory.Error);

        Data.Unreachable = 2;
        Assert.Null(await Directory.StartDatabaseAsync());
        Assert.True(Directory.IsLoaded);
        Assert.Null(Directory.Error);
        Assert.Equal(2, Calls.Split('\n').Count(l => l.Contains("up -d users")));
    });

    [Fact]
    public Task Removing_the_database_on_data_database_forgets_the_users() => Headless.Run(async () =>
    {
        await Created();
        var changes = 0;
        Directory.Changed += () => changes++;
        await Containers.Databases.Items[0].ConfirmRemoveCommand.ExecuteAsync(null);
        Headless.Settle();

        Assert.False(Directory.HasDatabase);
        Assert.False(Directory.IsLoaded);
        Assert.Empty(Directory.Roles);
        Assert.Empty(Directory.Users);
        Assert.True(changes > 0);
    });

    [Fact]
    public Task Another_database_can_be_chosen_and_is_set_up_when_first_read() => Headless.Run(async () =>
    {
        await Created();
        Data.Roles.Clear();
        Data.Users.Clear();
        var setUps = Data.SetUps;

        await Directory.UseDatabaseAsync(null);
        Assert.False(Directory.HasDatabase);
        Assert.Empty(Directory.Roles);

        await Directory.UseDatabaseAsync(Containers.Databases.Databases[0].Id);
        Assert.Equal(setUps + 1, Data.SetUps);
        Assert.Equal(RecommendedRoles.All.Count, Directory.Roles.Count);

        // Set up once a session, not on every read.
        await Directory.RefreshAsync();
        Assert.Equal(setUps + 1, Data.SetUps);
    });
}
