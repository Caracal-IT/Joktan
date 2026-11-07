using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Joktan.Users;

namespace Joktan.Website;

/// <summary>Someone signed in to the website: a user of the Users page, with their role.</summary>
/// <param name="IsAdministrator">Whether they have the Administrator role, and so see and may answer every question.</param>
public sealed record WebsiteUser(int Id, string Username, string Name, string Role, bool IsAdministrator)
{
    /// <summary>Their id as a question assigned to one user holds it.</summary>
    public string IdText => Id.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Why nobody can be signed in just now, such as the users database being down.</summary>
public sealed class UsersUnavailableException(string message) : Exception(message);

/// <summary>Who may sign in to the website. <see cref="DirectoryWebsiteUsers"/> asks the Users page; tests give their own.</summary>
public interface IWebsiteUsers
{
    /// <summary>The user with this username and password, or null when either is wrong.</summary>
    /// <exception cref="UsersUnavailableException">The users cannot be read.</exception>
    Task<WebsiteUser?> SignInAsync(string username, string password);

    /// <summary>The user with this id as last read, with the role they have now; null when they have gone.</summary>
    WebsiteUser? Find(int id);
}

/// <summary>The users and roles of the Users page, kept in its database.</summary>
public sealed class DirectoryWebsiteUsers(UserDirectory directory) : IWebsiteUsers
{
    public async Task<WebsiteUser?> SignInAsync(string username, string password)
    {
        if (!directory.HasDatabase)
            throw new UsersUnavailableException("There are no users to sign in as: the users have no database on Settings / User Settings.");
        try
        {
            return await directory.SignInAsync(username, password) is { } user ? Of(user) : null;
        }
        catch (UserStoreException e)
        {
            throw new UsersUnavailableException($"The users could not be read: {e.Message}");
        }
    }

    /// <summary>Why nobody can sign in just now, or null when someone can.</summary>
    public string? Problem => !directory.HasDatabase ? Resources.Strings.WebsiteUsers_NoDatabase
        : !directory.IsLoaded ? Resources.Strings.WebsiteUsers_NotRead
        : directory.Users.Any(u => u.HasPassword) ? null
        : Resources.Strings.WebsiteUsers_NoPasswords;

    public WebsiteUser? Find(int id) => directory.Users.FirstOrDefault(u => u.Id == id) is { } user ? Of(user) : null;

    private WebsiteUser Of(Users.Model.User user)
        => new(user.Id, user.Username, user.FullName, directory.RoleOf(user)?.Name ?? "", directory.IsAdministrator(user));
}
