using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Joktan.Workflows.Resources;

namespace Joktan.Workflows.Model;

/// <summary>What a part of a page is: something it shows, or a value the person fills in.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<PageItemType>))]
public enum PageItemType
{
    /// <summary>A title within the page.</summary>
    Heading,

    /// <summary>A paragraph of text.</summary>
    Text,

    /// <summary>A label and a value from the run, such as an order's total.</summary>
    Value,

    /// <summary>A line between parts of the page.</summary>
    Divider,

    /// <summary>One line of text to fill in.</summary>
    TextBox,

    /// <summary>Several lines of text to fill in.</summary>
    TextArea,

    Number,

    /// <summary>Yes or no, answered as true or false.</summary>
    CheckBox,

    /// <summary>One of a list of options.</summary>
    DropDown,

    /// <summary>A date, answered as yyyy-MM-dd.</summary>
    Date,
}

public static class PageItemTypes
{
    /// <summary>Whether the person fills it in, so that it is a field of the answer.</summary>
    public static bool IsInput(this PageItemType type) => type >= PageItemType.TextBox;

    /// <summary>What a type of page item is called on screen.</summary>
    public static string Text(this PageItemType type) => type switch
    {
        PageItemType.Heading => Strings.PageItem_Heading,
        PageItemType.Text => Strings.PageItem_Text,
        PageItemType.Value => Strings.PageItem_Value,
        PageItemType.Divider => Strings.PageItem_Divider,
        PageItemType.TextBox => Strings.PageItem_TextBox,
        PageItemType.TextArea => Strings.PageItem_TextArea,
        PageItemType.Number => Strings.PageItem_Number,
        PageItemType.CheckBox => Strings.PageItem_CheckBox,
        PageItemType.DropDown => Strings.PageItem_DropDown,
        _ => Strings.PageItem_Date,
    };
}

/// <summary>One part of a page, from top to bottom. Text settings may hold <c>{{paths}}</c> into the run's data.</summary>
public sealed class PageItem
{
    public string Id { get; set; } = Engine.WorkflowLibrary.NewId();

    public PageItemType Type { get; set; }

    /// <summary>For something filled in, the field of the answer it gives, such as <c>approved</c>.</summary>
    public string Name { get; set; } = "";

    /// <summary>What a field or a value is called on the page; the name when empty.</summary>
    public string Label { get; set; } = "";

    /// <summary>A heading's or a paragraph's text, or the value a value shows.</summary>
    public string Text { get; set; } = "";

    /// <summary>The value filled in to start with.</summary>
    public string Default { get; set; } = "";

    /// <summary>What an empty box shows in grey.</summary>
    public string Placeholder { get; set; } = "";

    /// <summary>A line under a field saying what to put in it.</summary>
    public string Help { get; set; } = "";

    /// <summary>Whether the page cannot be sent without it; a check box that is required must be ticked.</summary>
    public bool Required { get; set; }

    /// <summary>A drop-down's options. One that is only a path to a list, such as <c>{{vars.colours}}</c>, gives each item of the list.</summary>
    public List<string> Options { get; set; } = [];

    [JsonIgnore]
    public bool IsInput => Type.IsInput();

    /// <summary>The label, or the name when there is none.</summary>
    [JsonIgnore]
    public string Caption => Label.Trim().Length > 0 ? Label.Trim() : Name.Trim();
}

/// <summary>
/// A page built on Workflows / Pages that a user input step shows on the website, where the person
/// assigned the question fills it in. Each is a <c>&lt;name&gt;.page.json</c> file.
/// </summary>
public sealed class WorkflowPage
{
    public string Id { get; set; } = "";

    /// <summary>What the page is called in the builder and a user input step's list.</summary>
    public string Name { get; set; } = "";

    /// <summary>The title at the top of the page; the question's message when empty.</summary>
    public string Title { get; set; } = "";

    /// <summary>A line under the title.</summary>
    public string Description { get; set; } = "";

    /// <summary>What the button that sends the answer says; Send when empty.</summary>
    public string SubmitText { get; set; } = "";

    public List<PageItem> Items { get; set; } = [];

    /// <summary>The fields filled in, in order.</summary>
    [JsonIgnore]
    public IEnumerable<PageItem> Inputs => Items.Where(i => i.IsInput && i.Name.Trim().Length > 0);

    public WorkflowPage Clone() => JsonSerializer.Deserialize<WorkflowPage>(JsonSerializer.Serialize(this))!;

    /// <summary>
    /// A page made from a user input step: its question as the title, and a text box for each of its fields, or,
    /// with none, a box to tick and a comment, to change on Workflows / Pages.
    /// </summary>
    public static WorkflowPage For(WorkflowStep step, string name) => new()
    {
        Id = Engine.WorkflowLibrary.NewId(),
        Name = name,
        Items = step.Fields.Any(f => f.Name.Trim().Length > 0)
            ? step.Fields.Where(f => f.Name.Trim().Length > 0).Select(f => new PageItem
            {
                Type = PageItemType.TextBox,
                Name = f.Name.Trim(),
                Label = f.Label,
                Default = f.Default,
            }).ToList()
            :
            [
                new PageItem { Type = PageItemType.CheckBox, Name = "approved", Label = Strings.Pages_SampleCheckBox },
                new PageItem { Type = PageItemType.TextArea, Name = "comment", Label = Strings.Pages_SampleComment },
            ],
    };

    /// <summary>
    /// The page a question without one of its own is shown with: its message, then a text box for each of
    /// its fields, filled in with the values offered.
    /// </summary>
    public static WorkflowPage For(PendingInput question) => new()
    {
        Id = "",
        Name = "",
        Title = question.Message,
        Items = question.Fields.Select(f => new PageItem
        {
            Id = f.Name,
            Type = PageItemType.TextBox,
            Name = f.Name,
            Label = f.Label,
            Default = f.Default,
        }).ToList(),
    };
}
