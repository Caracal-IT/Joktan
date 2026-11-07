using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Joktan.Tests.TestSupport;
using Joktan.Users.ViewModels;
using Joktan.Users.Views;
using Xunit;

namespace Joktan.Users.Tests.Views;

public class UsersViewTests : UserTest
{
    private static void Picture(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("JOKTAN_SCREENSHOTS") is { Length: > 0 } folder)
        {
            using var frame = window.CaptureRenderedFrame();
            frame?.Save(System.IO.Path.Combine(folder, name + ".png"));
        }
    }

    [Fact]
    public Task The_page_creates_the_database_from_its_form_and_lists_users_and_roles() => Headless.Run(async () =>
    {
        var page = new UsersViewModel(Directory);
        var window = Headless.Show(new UsersView(), page, 1200, 900);
        Assert.True(Headless.Find<Button>(window, b => b.Name == "CreateButton").IsEffectivelyVisible);
        Assert.False(Headless.Find<TabControl>(window, t => t.Name == "Tabs").IsVisible);

        Headless.Click(Headless.Find<Button>(window, b => b.Name == "CreateButton"));
        Headless.Settle();
        Assert.True(Headless.Find<TextBox>(window, t => t.Name == "CreateUsernameBox").IsEffectivelyVisible);
        Assert.True(Headless.Find<TextBox>(window, t => t.Name == "CreatePasswordBox").IsEffectivelyVisible);
        Picture(window, "users-create");
        page.CreateForm!.Password = page.CreateForm.ConfirmPassword = "pw";
        Headless.Click(Headless.Find<Button>(window, b => b.Name == "CreateDatabaseButton"));
        await Headless.Until(() => page.ShowsTabs && !page.IsBusy);
        Headless.Settle();

        Assert.True(Headless.Find<TabControl>(window, t => t.Name == "Tabs").IsEffectivelyVisible);
        Assert.Contains(Headless.All<TextBlock>(Headless.Find<ItemsControl>(window, i => i.Name == "UserList")), t => t.Text == "Default Administrator");
        Assert.True(Headless.Find<Button>(window, b => b.Name == "AddUserButton").IsEffectivelyEnabled);
        Headless.Click(Headless.Find<Button>(window, b => b.Name == "AddUserButton"));
        Headless.Settle();
        Assert.True(Headless.Find<TextBox>(window, t => t.Name == "FirstNameBox").IsEffectivelyVisible);
        Assert.True(Headless.Find<TextBox>(window, t => t.Name == "UsernameBox").IsEffectivelyVisible);
        Assert.True(Headless.Find<TextBox>(window, t => t.Name == "UserPasswordBox").IsEffectivelyVisible);
        Assert.True(Headless.Find<TextBox>(window, t => t.Name == "EmailBox").IsEffectivelyVisible);
        Picture(window, "users-add-user");
        page.UserEditor!.CancelCommand.Execute(null);

        page.SelectedTab = UsersViewModel.RolesTab;
        Headless.Settle();
        var roles = Headless.All<TextBlock>(Headless.Find<ItemsControl>(window, i => i.Name == "RoleList")).Select(t => t.Text).ToList();
        Assert.Contains("Approver", roles);
        Assert.True(Headless.Find<Button>(window, b => b.Name == "AddRoleButton").IsEffectivelyEnabled);
        Picture(window, "users-roles");
        window.Close();
    });

    [Fact]
    public Task A_stopped_database_shows_a_start_button() => Headless.Run(async () =>
    {
        await Created();
        // Once for choosing it again, once for the page reading it when shown.
        Data.Unreachable = 2;
        await Directory.UseDatabaseAsync(null);
        await Directory.UseDatabaseAsync(Containers.Databases.Databases[0].Id);
        var window = Headless.Show(new UsersView(), new UsersViewModel(Directory), 1200, 700);
        Assert.True(Headless.Find<Border>(window, b => b.Name == "NotRunningCard").IsEffectivelyVisible);
        Assert.True(Headless.Find<Button>(window, b => b.Name == "StartButton").IsEffectivelyEnabled);
        Picture(window, "users-not-running");
        window.Close();
    });
}
