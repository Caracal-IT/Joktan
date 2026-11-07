using System.Linq;
using System.Text.Json.Nodes;
using Joktan.Workflows.Model;
using Xunit;

namespace Joktan.Website.Tests;

public class WorkItemsTests
{
    private static readonly WebsiteUser Ann = new(2, "ann", "Ann Lee", "Approver", false);
    private static readonly WebsiteUser Admin = new(1, "admin", "Admin", "Administrator", true);

    private static PendingInput Question(Assignee to, WorkflowPage? page = null) => new()
    {
        Id = "q1",
        AssignedTo = to,
        Page = page,
        Fields = page?.Inputs.Select(i => new InputField { Name = i.Name, Default = i.Default }).ToList() ?? [new InputField { Name = "ok" }],
    };

    [Fact]
    public void A_question_is_for_the_role_or_the_user_it_is_assigned_to_and_administrators_see_every_one()
    {
        Assert.True(WorkItems.IsFor(Question(WebsiteTest.Role("approver")), Ann));
        Assert.False(WorkItems.IsFor(Question(WebsiteTest.Role("Manager")), Ann));
        Assert.True(WorkItems.IsFor(Question(WebsiteTest.User(Ann)), Ann));
        Assert.False(WorkItems.IsFor(Question(new Assignee { Kind = AssigneeKind.User, Id = "3" }), Ann));
        Assert.False(WorkItems.IsFor(Question(new Assignee()), Ann));

        Assert.False(WorkItems.CanSee(Question(new Assignee()), Ann));
        Assert.True(WorkItems.CanSee(Question(new Assignee()), Admin));
        Assert.True(WorkItems.CanSee(Question(WebsiteTest.Role("Manager")), Admin));
        Assert.False(WorkItems.IsFor(Question(WebsiteTest.Role("Manager")), Admin));
    }

    [Fact]
    public void Answers_are_checked_against_the_page()
    {
        var page = new WorkflowPage
        {
            Items =
            [
                new PageItem { Type = PageItemType.Heading, Text = "Hi" },
                new PageItem { Type = PageItemType.TextBox, Name = "name", Required = true },
                new PageItem { Type = PageItemType.Date, Name = "when" },
                new PageItem { Type = PageItemType.CheckBox, Name = "ok" },
                new PageItem { Type = PageItemType.Number, Name = "count", Default = "2", Required = true },
            ],
        };
        var question = Question(WebsiteTest.Role("Approver"), page);

        var (_, problems) = WorkItems.Check(question, new JsonObject { ["when"] = "7/11/2026", ["ok"] = "maybe" });
        Assert.Equal(["name", "ok", "when"], problems!.Select(p => p.Key).Order());

        var (values, none) = WorkItems.Check(question, new JsonObject { ["name"] = "Ann", ["when"] = "2026-11-07", ["ok"] = "TRUE", ["count"] = 2.5 });
        Assert.Null(none);
        Assert.Equal("true", values!["ok"]);
        Assert.Equal("2.5", values["count"]);
        Assert.Equal("2026-11-07", values["when"]);

        // A required field not sent is fine while it starts with a value.
        (values, _) = WorkItems.Check(question, new JsonObject { ["name"] = "Ann" });
        Assert.False(values!.ContainsKey("count"));
        Assert.NotNull(WorkItems.Check(question, new JsonObject { ["name"] = " " }).Problems);
    }

    [Fact]
    public void A_date_or_a_number_left_empty_is_no_value_so_a_database_step_writes_null()
    {
        var page = new WorkflowPage
        {
            Items =
            [
                new PageItem { Type = PageItemType.Date, Name = "deliverBy" },
                new PageItem { Type = PageItemType.Number, Name = "boxes" },
                new PageItem { Type = PageItemType.TextBox, Name = "note" },
            ],
        };

        var (values, problems) = WorkItems.Check(Question(WebsiteTest.Role("Approver"), page), new JsonObject { ["deliverBy"] = "", ["boxes"] = " ", ["note"] = "" });

        Assert.Null(problems);
        Assert.Null(Joktan.Workflows.Engine.Templates.Parse(values!["deliverBy"]));
        Assert.Null(Joktan.Workflows.Engine.Templates.Parse(values["boxes"]));

        // Text keeps being text, empty or not.
        Assert.Equal("", values["note"]);
    }
}
