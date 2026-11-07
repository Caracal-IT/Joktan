using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Joktan.Users.Model;

namespace Joktan.Users.Tests;

/// <summary>The users and roles of a database that lives in the test, kept between the stores opened on it.</summary>
public sealed class FakeUserData
{
    public List<Role> Roles { get; } = [];

    public List<User> Users { get; } = [];

    public int NextId { get; set; } = 1;

    /// <summary>How many more opens fail as a server that is not up yet would.</summary>
    public int Unreachable { get; set; }

    public int SetUps { get; set; }

    /// <summary>The password hash of each user, by id, as the database keeps it.</summary>
    public Dictionary<int, string> Hashes { get; } = [];

    /// <summary>The database and password each store was opened with.</summary>
    public List<(string Database, string Password)> Opened { get; } = [];
}

/// <summary>A store over <see cref="FakeUserData"/>, which sets up as <see cref="PostgresUserStore"/> does.</summary>
public sealed class FakeUserStore(FakeUserData data) : IUserStore
{
    private void Check()
    {
        if (data.Unreachable <= 0)
            return;
        data.Unreachable--;
        throw new SocketException((int)SocketError.ConnectionRefused);
    }

    public Task<bool> SetUpAsync(CancellationToken cancel = default)
    {
        Check();
        data.SetUps++;
        if (data.Roles.Count > 0)
            return Task.FromResult(false);
        foreach (var role in RecommendedRoles.All)
            data.Roles.Add(new Role { Id = data.NextId++, Name = role.Name, Description = role.Description, BuiltIn = role.BuiltIn });
        var admin = RecommendedRoles.DefaultAdministrator;
        admin.Id = data.NextId++;
        admin.RoleId = data.Roles[0].Id;
        data.Users.Add(admin);
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<Role>> RolesAsync(CancellationToken cancel = default)
    {
        Check();
        return Task.FromResult<IReadOnlyList<Role>>(data.Roles.Select(r => r.Clone()).OrderByDescending(r => r.BuiltIn).ThenBy(r => r.Name.ToLowerInvariant()).ToList());
    }

    public Task<IReadOnlyList<User>> UsersAsync(CancellationToken cancel = default)
    {
        Check();
        return Task.FromResult<IReadOnlyList<User>>(data.Users.Select(u => u.Clone()).OrderBy(u => u.LastName.ToLowerInvariant()).ToList());
    }

    public Task<Role> SaveRoleAsync(Role role, CancellationToken cancel = default)
    {
        Check();
        var saved = role.Clone();
        if (saved.Id == 0)
        {
            saved.Id = data.NextId++;
            data.Roles.Add(saved);
        }
        else
            data.Roles[data.Roles.FindIndex(r => r.Id == role.Id)] = saved;
        return Task.FromResult(saved.Clone());
    }

    public Task DeleteRoleAsync(int id, CancellationToken cancel = default)
    {
        Check();
        if (data.Users.Any(u => u.RoleId == id))
            throw new InvalidOperationException("The role is in use.");
        data.Roles.RemoveAll(r => r.Id == id && !r.BuiltIn);
        return Task.CompletedTask;
    }

    public Task<User> SaveUserAsync(User user, CancellationToken cancel = default)
    {
        Check();
        var saved = user.Clone();
        if (saved.Id == 0)
        {
            saved.Id = data.NextId++;
            data.Users.Add(saved);
        }
        else
            data.Users[data.Users.FindIndex(u => u.Id == user.Id)] = saved;
        if (user.NewPassword is { Length: > 0 } password)
            data.Hashes[saved.Id] = PasswordHasher.Hash(password);
        saved.NewPassword = null;
        saved.HasPassword = data.Hashes.ContainsKey(saved.Id);
        return Task.FromResult(saved.Clone());
    }

    public Task DeleteUserAsync(int id, CancellationToken cancel = default)
    {
        Check();
        data.Users.RemoveAll(u => u.Id == id);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
