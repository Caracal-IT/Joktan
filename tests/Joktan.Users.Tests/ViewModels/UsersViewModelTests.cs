using System.Linq;
using System.Threading.Tasks;
using Joktan.Tests.TestSupport;
using Joktan.Users.ViewModels;
using Xunit;

namespace Joktan.Users.Tests.ViewModels;

public class UsersViewModelTests : UserTest
{
    [Fact]
    public Task Without_a_database_the_page_offers_the_create_form_with_free_values() => Headless.Run(async () =>
    {
        var page = new UsersViewModel(Directory);
        Assert.True(page.NeedsDatabase);
        Assert.False(page.ShowsTabs);
        Assert.False(page.AddUserCommand.CanExecute(null));

        page.CreateDatabaseCommand.Execute(null);
        var form = page.CreateForm!;
        Assert.Equal(("users", "5432", "postgres"), (form.NameText, form.PortText, form.Username));

        await form.CreateCommand.ExecuteAsync(null);
        Assert.Equal("Enter a password for the database.", form.Error);
        form.Username = "joktan";
        form.Password = "one";
        form.ConfirmPassword = "two";
        await form.CreateCommand.ExecuteAsync(null);
        Assert.Equal("The passwords are not the same.", form.Error);

        form.ConfirmPassword = "one";
        await form.CreateCommand.ExecuteAsync(null);
        Headless.Settle();
        Assert.Null(page.CreateForm);
        Assert.True(page.ShowsTabs);
        Assert.Equal("joktan", Containers.Databases.Databases[0].Username);
        Assert.Equal("one", Credentials.Saved[Containers.Databases.Databases[0].Account]);
        Assert.True(page.AddUserCommand.CanExecute(null));
        Assert.True(page.AddRoleCommand.CanExecute(null));
        Assert.Single(page.Users);
        Assert.Equal(7, page.Roles.Count);
    });

    [Fact]
    public Task Users_and_roles_are_added_edited_and_removed_from_the_page() => Headless.Run(async () =>
    {
        await Created();
        var page = new UsersViewModel(Directory);

        page.AddRoleCommand.Execute(null);
        Assert.Equal(UsersViewModel.RolesTab, page.SelectedTab);
        page.RoleEditor!.Name = "Auditor";
        page.RoleEditor.Description = "Checks the books";
        await page.RoleEditor.SaveCommand.ExecuteAsync(null);
        Assert.Null(page.RoleEditor);
        var auditor = page.Roles.Single(r => r.Name == "Auditor");
        Assert.True(auditor.CanBeRemoved);

        page.AddUserCommand.Execute(null);
        var editor = page.UserEditor!;
        Assert.False(editor.SelectedRole!.BuiltIn);
        editor.Username = "ann";
        editor.Email = "ann@example.com";
        editor.Password = "pw1";
        editor.ConfirmPassword = "pw2";
        editor.FirstName = "Ann";
        editor.LastName = "Lee";
        await editor.SaveCommand.ExecuteAsync(null);
        Assert.Equal("The passwords are not the same.", editor.Error);
        editor.ConfirmPassword = "pw1";
        editor.Notes = "Mornings only";
        editor.SelectedRole = editor.Roles.Single(r => r.Name == "Auditor");
        await editor.SaveCommand.ExecuteAsync(null);
        Assert.Null(page.UserEditor);
        var ann = page.Users.Single(u => u.Name == "Ann Lee");
        Assert.Equal(("Auditor", "ann", "ann@example.com", false), (ann.RoleName, ann.Username, ann.Email, ann.HasNoPassword));
        Assert.True(page.Users.Single(u => u.Username == "admin").HasNoPassword);
        Assert.Equal("1 user", page.Roles.Single(r => r.Name == "Auditor").UsersText);
        Assert.False(page.Roles.Single(r => r.Name == "Auditor").RemoveCommand.CanExecute(null));

        ann.EditCommand.Execute(null);
        Assert.Equal("Leave the password empty to keep the one set.", page.UserEditor!.PasswordHint);
        page.UserEditor.Description = "Head of finance";
        await page.UserEditor.SaveCommand.ExecuteAsync(null);
        Assert.Equal("Head of finance", page.Users.Single(u => u.Name == "Ann Lee").Description);

        var row = page.Users.Single(u => u.Name == "Ann Lee");
        row.RemoveCommand.Execute(null);
        Assert.True(row.IsConfirmingRemove);
        await row.ConfirmRemoveCommand.ExecuteAsync(null);
        Assert.DoesNotContain(page.Users, u => u.Name == "Ann Lee");

        // The only administrator and the built-in role cannot be removed.
        Assert.False(page.Users.Single().RemoveCommand.CanExecute(null));
        Assert.False(page.Roles.Single(r => r.IsBuiltIn).RemoveCommand.CanExecute(null));
    });

    [Fact]
    public Task A_database_that_cannot_be_reached_shows_start_instead_of_greyed_out_lists() => Headless.Run(async () =>
    {
        await Created();
        var page = new UsersViewModel(Directory);
        Data.Unreachable = 1;
        await Directory.UseDatabaseAsync(null);
        await Directory.UseDatabaseAsync(Containers.Databases.Databases[0].Id);
        Assert.True(page.IsUnreachable);
        Assert.False(page.ShowsTabs);
        Assert.NotNull(page.Error);

        await page.StartDatabaseCommand.ExecuteAsync(null);
        Assert.False(page.IsUnreachable);
        Assert.True(page.ShowsTabs);
        Assert.True(page.AddUserCommand.CanExecute(null));
    });

    [Fact]
    public Task Settings_choose_the_database_or_create_one() => Headless.Run(async () =>
    {
        var settings = new UserSettingsViewModel(Directory);
        Assert.Equal(["None"], settings.Choices.Select(c => c.Name));
        settings.CreateDatabaseCommand.Execute(null);
        settings.CreateForm!.Password = settings.CreateForm.ConfirmPassword = "pw";
        await settings.CreateForm.CreateCommand.ExecuteAsync(null);
        Headless.Settle();

        Assert.Null(settings.CreateForm);
        Assert.Equal(["None", "users"], settings.Choices.Select(c => c.Name));
        Assert.Equal("users", settings.Selected!.Name);
        settings.Selected = settings.Choices[0];
        Headless.Settle();
        Assert.False(Directory.HasDatabase);
        Assert.Equal("None", settings.Selected!.Name);
    });
}
