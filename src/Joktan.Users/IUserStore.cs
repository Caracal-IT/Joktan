using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Joktan.Users.Model;

namespace Joktan.Users;

/// <summary>Where the users and roles are kept. <see cref="PostgresUserStore"/> keeps them in PostgreSQL; tests use their own.</summary>
public interface IUserStore : IAsyncDisposable
{
    /// <summary>
    /// Makes the tables if they are not there. A database with no roles yet is given the recommended
    /// roles and a default administrator. Gives whether it did so.
    /// </summary>
    Task<bool> SetUpAsync(CancellationToken cancel = default);

    Task<IReadOnlyList<Role>> RolesAsync(CancellationToken cancel = default);

    Task<IReadOnlyList<User>> UsersAsync(CancellationToken cancel = default);

    /// <summary>Adds a role with an id of 0, or changes the one with its id; gives it as saved.</summary>
    Task<Role> SaveRoleAsync(Role role, CancellationToken cancel = default);

    Task DeleteRoleAsync(int id, CancellationToken cancel = default);

    /// <summary>Adds a user with an id of 0, or changes the one with its id; gives them as saved.</summary>
    Task<User> SaveUserAsync(User user, CancellationToken cancel = default);

    Task DeleteUserAsync(int id, CancellationToken cancel = default);
}
