using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Joktan.Workflows.Engine;
using Joktan.Workflows.Model;

namespace Joktan.Website;

/// <summary>
/// The work items of the website: the questions of user input steps waiting for an answer. A user sees
/// those assigned to their role or to them; an administrator sees them all. Each is shown with its page,
/// or with the page made from its fields when the step has none.
/// </summary>
public static class WorkItems
{
    private static readonly JsonSerializerOptions PageJson = new(JsonSerializerDefaults.Web);

    /// <summary>The runs waiting for an answer to a question, oldest question first.</summary>
    public static IEnumerable<WorkflowRun> Waiting(IEnumerable<WorkflowRun> runs)
        => runs.Where(r => r.PendingInput is { Id.Length: > 0 } && !r.Status.IsFinished()).OrderBy(r => r.PendingInput!.Asked).ThenBy(r => r.Serial);

    /// <summary>Whether the question is for this user: their role's, or theirs alone. A question for nobody is only the administrators'.</summary>
    public static bool IsFor(PendingInput question, WebsiteUser user) => question.AssignedTo is { IsSet: true } to && (to.Kind == AssigneeKind.Role
        ? string.Equals(to.Id.Trim(), user.Role.Trim(), StringComparison.OrdinalIgnoreCase)
        : to.Id.Trim() == user.IdText);

    public static bool CanSee(PendingInput question, WebsiteUser user) => user.IsAdministrator || IsFor(question, user);

    /// <summary>The work items a user may see.</summary>
    public static IEnumerable<WorkflowRun> For(IEnumerable<WorkflowRun> runs, WebsiteUser user) => Waiting(runs).Where(r => CanSee(r.PendingInput!, user));

    /// <summary>The run waiting on the question with this id, if the user may see it.</summary>
    public static WorkflowRun? Find(IEnumerable<WorkflowRun> runs, string id, WebsiteUser user)
        => For(runs, user).FirstOrDefault(r => r.PendingInput!.Id == id);

    /// <summary>The page a question is shown with: its own, or one made from its message and fields.</summary>
    public static WorkflowPage PageOf(PendingInput question) => question.Page ?? WorkflowPage.For(question);

    /// <summary>A work item as the list gives it.</summary>
    public static JsonObject Summary(WorkflowRun run, WebsiteUser user)
    {
        var question = run.PendingInput!;
        var page = PageOf(question);
        return new JsonObject
        {
            ["id"] = question.Id,
            ["runId"] = run.Id,
            ["serial"] = run.Serial,
            ["workflow"] = run.WorkflowName,
            ["title"] = page.Title.Trim().Length > 0 ? page.Title : question.Message,
            ["message"] = question.Message,
            ["asked"] = Time(question.Asked),
            ["until"] = question.Until is { } until ? Time(until) : null,
            ["assignedTo"] = StepContext.AssigneeJson(question.AssignedTo),
            ["assignedToYou"] = IsFor(question, user),
            ["escalated"] = question.Escalated,
            ["paused"] = run.Status == RunStatus.Paused,
            ["pageId"] = question.Page?.Id is { Length: > 0 } pageId ? pageId : null,
            ["metadata"] = question.Metadata.DeepClone(),
        };
    }

    /// <summary>A work item with all it takes to fill it in: its fields and its page.</summary>
    public static JsonObject Detail(WorkflowRun run, WebsiteUser user)
    {
        var question = run.PendingInput!;
        var item = Summary(run, user);
        item["fields"] = new JsonArray(question.Fields.Select(f => (JsonNode)new JsonObject
        {
            ["name"] = f.Name,
            ["label"] = f.Label,
            ["default"] = f.Default,
        }).ToArray());
        item["page"] = PageJsonOf(PageOf(question));
        return item;
    }

    /// <summary>A page as JSON, as the website draws it and other programs read it.</summary>
    public static JsonNode PageJsonOf(WorkflowPage page) => JsonSerializer.SerializeToNode(page, PageJson)!;

    /// <summary>
    /// Checks an answer against the question's page and fields, giving the values for
    /// <see cref="WorkflowEngine.Answer"/>, or what is wrong with each field that is wrong.
    /// </summary>
    public static (Dictionary<string, string>? Values, JsonObject? Problems) Check(PendingInput question, JsonObject? answer)
    {
        answer ??= [];
        var page = PageOf(question);
        var items = page.Inputs.GroupBy(i => i.Name.Trim()).ToDictionary(g => g.Key, g => g.First());
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var problems = new JsonObject();

        foreach (var (name, node) in answer)
        {
            if (question.Fields.All(f => f.Name != name))
            {
                problems[name] = "The question has no field with this name.";
                continue;
            }

            var text = Text(node);
            if (!items.TryGetValue(name, out var item))
            {
                values[name] = text;
                continue;
            }

            if (Problem(item, text, node) is { } problem)
                problems[name] = problem;
            else
                values[name] = Normalize(item, text);
        }

        // A required field not sent is as wrong as one sent empty, unless it starts with a value.
        foreach (var item in items.Values.Where(i => i.Required && !answer.ContainsKey(i.Name.Trim())))
        {
            var start = question.Fields.FirstOrDefault(f => f.Name == item.Name.Trim())?.Default ?? "";
            if (Problem(item, start, JsonValue.Create(start)) is { } problem)
                problems[item.Name.Trim()] = problem;
        }

        return problems.Count > 0 ? (null, problems) : (values, null);
    }

    private static string? Problem(PageItem item, string text, JsonNode? node)
    {
        var empty = text.Trim().Length == 0;
        switch (item.Type)
        {
            case PageItemType.CheckBox:
                if (!bool.TryParse(empty ? "false" : text.Trim(), out var ticked))
                    return "Give true or false.";
                return item.Required && !ticked ? "This must be ticked." : null;
            case PageItemType.Number when !empty && !double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out _):
                return "Give a number, such as 12 or 3.5.";
            case PageItemType.Date when !empty && !DateOnly.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _):
                return "Give a date as yyyy-MM-dd.";
            case PageItemType.DropDown when !empty && item.Options.Count > 0 && !item.Options.Contains(text):
                return "Choose one of the options.";
        }

        if (item.Required && empty)
            return "This must be filled in.";
        return node is JsonObject or JsonArray && item.Type != PageItemType.TextArea && item.Type != PageItemType.TextBox ? "Give a single value." : null;
    }

    // A check box gives true or false, whatever case it was sent in. A number or a date left empty gives null, not
    // empty text, which a database step would send to a date or number column and have refused. The rest go as given.
    private static string Normalize(PageItem item, string text) => item.Type switch
    {
        PageItemType.CheckBox => text.Trim().Equals("true", StringComparison.OrdinalIgnoreCase) ? "true" : "false",
        PageItemType.Number or PageItemType.Date when text.Trim().Length == 0 => "null",
        PageItemType.Number or PageItemType.Date => text.Trim(),
        _ => text,
    };

    // A value as the engine takes it: text as it is, and anything else as JSON.
    private static string Text(JsonNode? node) => node switch
    {
        null => "",
        JsonValue value when value.TryGetValue<string>(out var text) => text,
        _ => JsonText.Of(node),
    };

    private static string Time(DateTime time) => time.ToString("O", CultureInfo.InvariantCulture);
}
