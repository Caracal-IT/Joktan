using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Joktan.Users.Model;

/// <summary>
/// What a user does, such as Approver. Workflows assign their questions to roles, so that anyone with the
/// role can answer. Kept in the users database.
/// </summary>
public sealed class Role
{
    /// <summary>The database's id; 0 for one not saved yet.</summary>
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    /// <summary>Whether it is the Administrator role, which cannot be renamed or removed.</summary>
    [JsonPropertyName("built_in")]
    public bool BuiltIn { get; set; }

    public Role Clone() => (Role)MemberwiseClone();

    public override string ToString() => Name;
}

/// <summary>The roles every users database starts with, and that can be added again on the Roles tab.</summary>
public static class RecommendedRoles
{
    public const string Administrator = "Administrator";

    /// <summary>The Administrator role first, then the others, with what each is for.</summary>
    public static IReadOnlyList<Role> All { get; } =
    [
        new() { Name = Administrator, Description = "Looks after Joktan, its workflows, users and roles, and can answer any question.", BuiltIn = true },
        new() { Name = "Manager", Description = "Makes the decisions a workflow cannot, and takes questions that were escalated." },
        new() { Name = "Approver", Description = "Approves or rejects what a workflow asks about, such as an order or a change." },
        new() { Name = "Reviewer", Description = "Checks work before it goes further, and sends it back when it needs changes." },
        new() { Name = "Operator", Description = "Runs things day to day, and answers routine questions from workflows." },
        new() { Name = "Support", Description = "Answers questions about customers and their requests." },
        new() { Name = "Viewer", Description = "Sees how things are going, but is not asked to act." },
    ];

    /// <summary>The user made with a new database, who has the Administrator role.</summary>
    public static User DefaultAdministrator => new()
    {
        Username = "admin",
        FirstName = "Default",
        LastName = "Administrator",
        Description = "Made when the users database was created.",
        Notes = "Give this user a password, and change the name to that of the person who looks after Joktan, or add them and give them the Administrator role.",
    };
}
