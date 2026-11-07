using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Joktan.Data.Postgres;
using Joktan.Users.Model;
using Xunit;

namespace Joktan.Users.Tests;

public class PostgresUserStoreTests
{
    /// <summary>Keeps what is sent and answers each statement with <see cref="Answer"/>; the browser's calls are not used.</summary>
    private sealed class Recorder : IDatabaseConnection
    {
        public List<SqlText> Sent { get; } = [];

        public Func<SqlText, string> Answer { get; set; } = _ => "";

        public Task<IReadOnlyList<string>> RunAsync(IReadOnlyList<SqlText> statements, CancellationToken cancel = default)
        {
            Sent.AddRange(statements);
            return Task.FromResult<IReadOnlyList<string>>(statements.Select(Answer).ToList());
        }

        public Task<IReadOnlyList<string>> SchemasAsync(CancellationToken cancel = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> TablesAsync(string schema, CancellationToken cancel = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<TableColumn>> ColumnsAsync(string schema, string table, CancellationToken cancel = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<TableRow>> RowsAsync(string schema, string table, IReadOnlyList<string> columns, IReadOnlyList<string> orderBy, int limit, int offset, CancellationToken cancel = default) => throw new NotSupportedException();
        public Task<long> CountAsync(string schema, string table, CancellationToken cancel = default) => throw new NotSupportedException();
        public Task CreateSchemaAsync(string name, CancellationToken cancel = default) => throw new NotSupportedException();
        public Task CreateTableAsync(string schema, string name, bool withId, CancellationToken cancel = default) => throw new NotSupportedException();
        public Task AddColumnAsync(string schema, string table, ColumnSpec column, CancellationToken cancel = default) => throw new NotSupportedException();
        public Task DropSchemaAsync(string name, CancellationToken cancel = default) => throw new NotSupportedException();
        public Task DropTableAsync(string schema, string table, CancellationToken cancel = default) => throw new NotSupportedException();
        public Task DropColumnAsync(string schema, string table, string column, CancellationToken cancel = default) => throw new NotSupportedException();
        public Task InsertAsync(string schema, string table, IReadOnlyList<KeyValuePair<string, string?>> values, CancellationToken cancel = default) => throw new NotSupportedException();
        public Task<int> UpdateAsync(string schema, string table, string rowId, IReadOnlyList<KeyValuePair<string, string?>> values, CancellationToken cancel = default) => throw new NotSupportedException();
        public Task<int> DeleteAsync(string schema, string table, string rowId, CancellationToken cancel = default) => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Setting_up_an_empty_database_seeds_the_recommended_roles_and_an_administrator()
    {
        var db = new Recorder { Answer = s => s.Sql.Contains("NOT EXISTS") ? "true" : "1" };
        Assert.True(await new PostgresUserStore(db).SetUpAsync());

        Assert.StartsWith("CREATE SCHEMA IF NOT EXISTS \"joktan\"", db.Sent[0].Sql);
        var roles = db.Sent.Where(s => s.Sql.StartsWith("INSERT INTO \"joktan\".roles")).ToList();
        Assert.Equal(RecommendedRoles.All.Select(r => r.Name), roles.Select(r => r.Parameters[0]));
        Assert.Equal(["true", "false"], roles.Select(r => r.Parameters[2]).Distinct());
        var admin = db.Sent.Single(s => s.Sql.StartsWith("INSERT INTO \"joktan\".users"));
        Assert.Equal(["admin", "Default", "Administrator"], admin.Parameters.Take(3));
        Assert.Equal(RecommendedRoles.Administrator, admin.Parameters[^1]);

        // Values are always parameters, never in the SQL.
        Assert.All(db.Sent, s => Assert.DoesNotContain("Approver", s.Sql));
    }

    [Fact]
    public async Task A_database_that_has_roles_is_not_seeded_again()
    {
        var db = new Recorder { Answer = s => s.Sql.Contains("NOT EXISTS") ? "false" : "" };
        Assert.False(await new PostgresUserStore(db, "people").SetUpAsync());
        Assert.Equal(10, db.Sent.Count);
        Assert.Contains("\"people\".users", db.Sent[2].Sql);
    }

    [Fact]
    public async Task Rows_come_back_as_json_and_a_row_that_has_gone_is_said_to_have()
    {
        var db = new Recorder
        {
            Answer = s => s.Sql.Contains("FROM \"joktan\".roles") && s.Sql.StartsWith("SELECT")
                ? """[{"id":1,"name":"Administrator","description":"d","built_in":true}]"""
                : s.Sql.StartsWith("SELECT") ? """[{"id":4,"username":"ann","email":"a@b.co","has_password":true,"first_name":"Ann","last_name":"Lee","description":"","notes":"n","role_id":1}]""" : "",
        };
        var store = new PostgresUserStore(db);
        var role = Assert.Single(await store.RolesAsync());
        Assert.True(role is { Id: 1, Name: "Administrator", BuiltIn: true });
        var user = Assert.Single(await store.UsersAsync());
        Assert.Equal(("Ann Lee", 1, "n", "ann", "a@b.co", true), (user.FullName, user.RoleId, user.Notes, user.Username, user.Email, user.HasPassword));
        Assert.DoesNotContain("password_hash'", db.Sent.Single(s => s.Sql.Contains("FROM \"joktan\".users")).Sql);

        // No password keeps the one set; one given goes only as its hash.
        await Assert.ThrowsAsync<UserStoreException>(() => store.SaveUserAsync(new User { Id = 9, Username = "ab", FirstName = "A", LastName = "B", RoleId = 1 }));
        Assert.Equal(["ab", "", "A", "B", "", "", "1", null, "9"], db.Sent[^1].Parameters);
        await Assert.ThrowsAsync<UserStoreException>(() => store.SaveUserAsync(new User { Id = 9, Username = "ab", NewPassword = "hunter2", FirstName = "A", LastName = "B", RoleId = 1 }));
        var hash = db.Sent[^1].Parameters[7]!;
        Assert.StartsWith("pbkdf2-sha256$", hash);
        Assert.DoesNotContain(db.Sent, s => s.Parameters.Contains("hunter2"));
        Assert.True(PasswordHasher.Verify("hunter2", hash));
        await Assert.ThrowsAsync<UserStoreException>(() => store.DeleteRoleAsync(1));
    }

    private const string Variable = "JOKTAN_TEST_POSTGRES_PORT";

    [Fact]
    public async Task Users_and_roles_go_to_a_real_server()
    {
        Assert.SkipUnless(int.TryParse(Environment.GetEnvironmentVariable(Variable), out var port), $"Set {Variable} to test against a real server.");
        var schema = "joktan_users_test_" + Guid.NewGuid().ToString("N")[..6];
        await using var connection = new PostgresConnection("localhost", port, "postgres", "secret", "test");
        var store = new PostgresUserStore(connection, schema);
        try
        {
            Assert.True(await store.SetUpAsync());
            Assert.False(await store.SetUpAsync());
            var roles = await store.RolesAsync();
            Assert.Equal(RecommendedRoles.Administrator, roles[0].Name);
            Assert.True(roles[0].BuiltIn);
            Assert.Equal(RecommendedRoles.All.Count, roles.Count);
            var admin = Assert.Single(await store.UsersAsync());
            Assert.Equal(roles[0].Id, admin.RoleId);

            var auditor = await store.SaveRoleAsync(new Role { Name = "Auditor's \"desk\"", Description = "x" });
            var ann = await store.SaveUserAsync(new User { Username = "ann", Email = "ann@example.com", NewPassword = "pw", FirstName = "Ann", LastName = "O'Lee", Notes = "line 1\nline 2", RoleId = auditor.Id });
            Assert.True(ann is { Id: > 0, HasPassword: true, Username: "ann" });
            Assert.True(await store.CheckPasswordAsync("ANN", "pw"));
            Assert.False(await store.CheckPasswordAsync("ann", "nope"));
            await Assert.ThrowsAnyAsync<System.Data.Common.DbException>(() => store.SaveUserAsync(new User { Username = "Ann", FirstName = "X", LastName = "Y", RoleId = auditor.Id }));
            ann.Description = "Finance";
            Assert.Equal("Finance", (await store.SaveUserAsync(ann)).Description);
            Assert.True(await store.CheckPasswordAsync("ann", "pw"));

            // The built-in role keeps its name; a role in use stays.
            Assert.Equal(RecommendedRoles.Administrator, (await store.SaveRoleAsync(new Role { Id = roles[0].Id, Name = "Boss", Description = "new" })).Name);
            await Assert.ThrowsAnyAsync<System.Data.Common.DbException>(() => store.DeleteRoleAsync(auditor.Id));
            await Assert.ThrowsAsync<UserStoreException>(() => store.DeleteRoleAsync(roles[0].Id));
            await store.DeleteUserAsync(ann.Id);
            await store.DeleteRoleAsync(auditor.Id);
            Assert.DoesNotContain(await store.RolesAsync(), r => r.Id == auditor.Id);
        }
        finally
        {
            await connection.RunAsync([new SqlText($"DROP SCHEMA IF EXISTS {PostgresSql.Quote(schema)} CASCADE", [])]);
        }
    }

    [Fact]
    public async Task Tables_made_before_users_had_usernames_are_given_them()
    {
        Assert.SkipUnless(int.TryParse(Environment.GetEnvironmentVariable(Variable), out var port), $"Set {Variable} to test against a real server.");
        var schema = "joktan_users_old_" + Guid.NewGuid().ToString("N")[..6];
        var q = PostgresSql.Quote(schema);
        await using var connection = new PostgresConnection("localhost", port, "postgres", "secret", "test");
        try
        {
            await connection.RunAsync([
                new SqlText($"CREATE SCHEMA {q}", []),
                new SqlText($"CREATE TABLE {q}.roles (id integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY, name text NOT NULL UNIQUE, description text NOT NULL DEFAULT '', built_in boolean NOT NULL DEFAULT false, created timestamptz NOT NULL DEFAULT now(), changed timestamptz NOT NULL DEFAULT now())", []),
                new SqlText($"CREATE TABLE {q}.users (id integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY, first_name text NOT NULL, last_name text NOT NULL, description text NOT NULL DEFAULT '', notes text NOT NULL DEFAULT '', role_id integer NOT NULL REFERENCES {q}.roles (id), created timestamptz NOT NULL DEFAULT now(), changed timestamptz NOT NULL DEFAULT now())", []),
                new SqlText($"INSERT INTO {q}.roles (name, built_in) VALUES ('Administrator', true)", []),
                new SqlText($"INSERT INTO {q}.users (first_name, last_name, role_id) VALUES ('Ann', 'Lee', 1)", []),
            ]);

            var store = new PostgresUserStore(connection, schema);
            Assert.False(await store.SetUpAsync());
            var ann = Assert.Single(await store.UsersAsync());
            Assert.Equal(("ann.lee.1", "", false), (ann.Username, ann.Email, ann.HasPassword));
        }
        finally
        {
            await connection.RunAsync([new SqlText($"DROP SCHEMA IF EXISTS {q} CASCADE", [])]);
        }
    }
}
