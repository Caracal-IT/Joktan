using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using Joktan.Tests.TestSupport;
using Joktan.Users.Model;
using Xunit;

namespace Joktan.Users.Tests;

/// <summary>Signing in as a user of the Users page, as the website does.</summary>
public class SignInTests : UserTest
{
    [Fact]
    public Task A_user_signs_in_with_their_username_and_password_and_the_users_are_read_afresh() => Headless.Run(async () =>
    {
        Assert.Null(await Directory.SignInAsync("admin", "x"));
        await Created();
        var admin = Directory.Users.Single();
        admin.NewPassword = "open sesame";
        Assert.Null(await Directory.SaveUserAsync(admin));

        // Someone added straight in the database, since the users were last read.
        var approver = Directory.Roles.Single(r => r.Name == "Approver");
        Data.Users.Add(new User { Id = 99, Username = "ann", FirstName = "Ann", LastName = "Lee", RoleId = approver.Id });
        Data.Hashes[99] = PasswordHasher.Hash("pw");

        var ann = await Directory.SignInAsync("ANN", "pw");
        Assert.Equal("Ann Lee", ann!.FullName);
        Assert.False(Directory.IsAdministrator(ann));
        Assert.Contains(Directory.Users, u => u.Id == 99);

        Assert.True(Directory.IsAdministrator((await Directory.SignInAsync("admin", "open sesame"))!));
        Assert.Null(await Directory.SignInAsync("admin", "wrong"));
        Assert.Null(await Directory.SignInAsync("nobody", "pw"));
        Assert.Null(await Directory.SignInAsync("ann", ""));
        Assert.Null(Directory.Error);
    });

    [Fact]
    public Task A_database_that_cannot_be_reached_says_so() => Headless.Run(async () =>
    {
        await Created();
        Data.Unreachable = 1;
        var problem = await Assert.ThrowsAsync<UserStoreException>(() => Directory.SignInAsync("admin", "x"));
        Assert.Contains("users", problem.Message);
        Assert.False(Directory.IsBusy);
    });
}
