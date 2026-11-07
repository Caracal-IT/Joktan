using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Joktan.Tests.TestSupport;
using Joktan.Workflows.Engine;
using Joktan.Workflows.Model;
using Joktan.Workflows.Tests.Engine;
using Xunit;

namespace Joktan.Workflows.Tests.Steps;

/// <summary>A user input step that shows a page of Workflows / Pages on the website.</summary>
[Collection(ProcessStateCollection.Name)]
public class PageStepTests : EngineTest
{
    private static readonly Assignee Approver = new() { Kind = AssigneeKind.Role, Id = "Approver", Name = "Approver" };

    private void SavePage()
    {
        var page = new WorkflowPage
        {
            Id = "page1",
            Name = "Approve order",
            Title = "Order {{input.order}}",
            Items =
            [
                new PageItem { Type = PageItemType.Value, Label = "Total", Text = "{{input.total}}" },
                new PageItem { Type = PageItemType.CheckBox, Name = "approved", Default = "false", Required = true },
                new PageItem { Type = PageItemType.DropDown, Name = "colour", Options = ["{{input.colours}}", "Other"] },
                new PageItem { Type = PageItemType.TextArea, Name = "comment", Default = "{{input.note}}" },
            ],
        };
        Engine.Pages.Save(page);
    }

    private WorkflowRun Start(WorkflowStep ask, string input)
    {
        var flow = new Flow("Orders").Then(ask).Then(Flow.End("{{vars.answer}}"));
        Library.Save(flow.Workflow);
        return Engine.Start(flow.Workflow, JsonNode.Parse(input), RunTrigger.Manual);
    }

    [Fact]
    public async Task The_page_is_filled_in_from_the_run_and_its_fields_are_asked_for_as_well_as_the_steps()
    {
        SavePage();
        var run = Start(new WorkflowStep
        {
            Type = StepType.UserInput, Message = "Approve?", PageId = "page1", AssignedTo = Approver.Clone(), Variable = "answer",
            Fields = [new InputField { Name = "comment", Default = "from the step" }, new InputField { Name = "hidden", Default = "7" }],
        }, """{"order": "A-1", "total": 12.5, "colours": ["Red", "Blue"], "note": "n"}""");
        await Until(run, RunStatus.WaitingForInput);

        var question = run.PendingInput!;
        Assert.Equal(32, question.Id.Length);
        var page = question.Page!;
        Assert.Equal("Order A-1", page.Title);
        Assert.Equal("12.5", page.Items[0].Text);
        Assert.Equal(["Red", "Blue", "Other"], page.Items[2].Options);
        Assert.Equal("from the step", page.Items[3].Default);
        Assert.Equal(["comment", "hidden", "approved", "colour"], question.Fields.Select(f => f.Name));

        // The page as saved keeps its paths, for the next run.
        Assert.Equal("Order {{input.order}}", Engine.Pages.Find("page1")!.Title);

        Engine.Answer(run.Id, new Dictionary<string, string> { ["approved"] = "true", ["colour"] = "Blue" }, "Ann Lee (ann) on the website");
        var finished = await Engine.WhenFinished(run.Id).WaitAsync(Patience, TestContext.Current.CancellationToken);
        Assert.Equal(RunStatus.Completed, finished.Status);
        Assert.Equal("""{"comment":"from the step","hidden":7,"approved":true,"colour":"Blue"}""", JsonText.Of(finished.Steps.Single(s => s.Type == StepType.UserInput).Result));
        Assert.Contains(finished.Log, l => l.Text == "Answered by Ann Lee (ann) on the website.");
    }

    [Fact]
    public async Task A_step_without_a_page_asks_as_before_and_a_page_that_has_gone_fails_the_step()
    {
        var run = Start(new WorkflowStep { Type = StepType.UserInput, Message = "OK?", Fields = [new InputField { Name = "ok" }], AssignedTo = Approver.Clone() }, "{}");
        await Until(run, RunStatus.WaitingForInput);
        Assert.Null(run.PendingInput!.Page);
        Engine.Kill(run.Id);

        var failed = await RunAsync(new Flow("Gone").Then(new WorkflowStep { Type = StepType.UserInput, PageId = "nope", AssignedTo = Approver.Clone() }));
        Assert.Equal(RunStatus.Failed, failed.Status);
        Assert.Contains("There is no page nope", failed.Error);
    }

    [Fact]
    public void A_step_with_a_page_needs_no_message_or_fields()
    {
        var step = new WorkflowStep { Type = StepType.UserInput, PageId = "page1", AssignedTo = Approver.Clone() };
        Assert.Null(WorkflowCheck.StepProblem(step));
        step.PageId = "";
        Assert.NotNull(WorkflowCheck.StepProblem(step));
    }
}
