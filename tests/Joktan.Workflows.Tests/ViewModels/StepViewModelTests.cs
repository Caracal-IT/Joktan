using System.Linq;
using Joktan.Workflows.Model;
using Joktan.Workflows.Steps;
using Joktan.Workflows.ViewModels;
using Xunit;

namespace Joktan.Workflows.Tests.ViewModels;

public class StepViewModelTests
{
    private int _changes;

    private StepViewModel Panel(WorkflowStep step) => new(step, new StepChoices
    {
        Clients = () => ["Local"],
        FolderMessages = client => client == "Local" ? ["tank.json"] : [],
        Requests = () => ["Orders/Create"],
        Workflows = () => [new WorkflowChoice("w1", "Other")],
    }, () => _changes++);

    [Fact]
    public void Shows_only_what_the_type_uses()
    {
        var http = Panel(new WorkflowStep { Type = StepType.Http });
        Assert.True(http.IsApi);
        Assert.True(http.HasResult);
        Assert.True(http.CanRetry);
        Assert.False(http.IsMqtt);

        var start = Panel(new WorkflowStep { Type = StepType.Start });
        Assert.False(start.CanDelete);
        Assert.False(start.CanRetry);
        Assert.True(Panel(new WorkflowStep { Type = StepType.MqttWait }).HasComparison);
        Assert.True(Panel(new WorkflowStep { Type = StepType.UserInput }).HasTimeLimit);
    }

    [Fact]
    public void Choosing_a_saved_request_or_writing_one_here_changes_the_step()
    {
        var step = new WorkflowStep { Type = StepType.Http };
        var panel = Panel(step);
        Assert.Equal(["Write it here", "Orders/Create"], panel.RequestChoices);
        Assert.True(panel.WritesRequest);
        Assert.True(panel.ShowsBody);

        panel.RequestChoice = "Orders/Create";
        Assert.Equal("Orders/Create", step.RequestPath);
        Assert.True(panel.UsesSavedRequest);
        Assert.False(panel.ShowsBody);

        panel.BodyMode = BodyMode.Merge;
        Assert.True(panel.ShowsBody);

        panel.RequestChoice = "Write it here";
        Assert.Equal("", step.RequestPath);
        Assert.True(_changes >= 3);
    }

    [Fact]
    public void A_folder_message_can_be_chosen_from_the_client()
    {
        var step = new WorkflowStep { Type = StepType.MqttPublish };
        var panel = Panel(step);

        panel.Client = "Local";
        Assert.Contains("tank.json", panel.MessageChoices);
        panel.MessageChoice = "tank.json";

        Assert.Equal("tank.json", step.MessageFile);
        Assert.Equal("Topic; empty keeps the message's own", panel.TopicLabel);
        Assert.False(panel.WritesMessage);
    }

    [Fact]
    public void Changing_the_language_changes_an_untouched_template()
    {
        var step = new WorkflowStep { Type = StepType.Script, Code = ScriptRunner.CSharpTemplate };
        var panel = Panel(step);

        panel.Language = ScriptLanguage.JavaScript;
        Assert.Equal(ScriptRunner.JavaScriptTemplate, step.Code);
        Assert.True(panel.CanCheckCode);

        panel.Language = ScriptLanguage.Go;
        Assert.Equal(ScriptRunner.GoTemplate, step.Code);
        Assert.False(panel.CanCheckCode);

        // Code of one's own stays.
        panel.Code = "return 1;";
        panel.Language = ScriptLanguage.CSharp;
        Assert.Equal("return 1;", step.Code);
    }

    [Fact]
    public void The_code_can_be_checked_without_running_it()
    {
        var panel = Panel(new WorkflowStep { Type = StepType.Script, Language = ScriptLanguage.JavaScript, Code = "return 1 +;" });

        panel.CheckCodeCommand.Execute(null);
        Assert.StartsWith("Line 1", panel.CodeCheck);

        panel.Code = "return 1;";
        panel.CheckCodeCommand.Execute(null);
        Assert.Equal("The code has no mistakes that stop it running.", panel.CodeCheck);

        panel.Language = ScriptLanguage.CSharp;
        panel.Code = "return nope;";
        panel.CheckCodeCommand.Execute(null);
        Assert.Contains("nope", panel.CodeCheck);
    }

    [Fact]
    public void Headers_fields_and_retries_are_edited_on_the_step()
    {
        var step = new WorkflowStep { Type = StepType.UserInput };
        var panel = Panel(step);

        panel.AddFieldCommand.Execute(null);
        panel.Fields[0].Name = " approved ";
        panel.Fields[0].Default = "true";
        Assert.Equal("approved", step.Fields[0].Name);
        panel.Fields[0].RemoveCommand.Execute(null);
        Assert.Empty(step.Fields);

        var http = new WorkflowStep { Type = StepType.Http };
        var httpPanel = Panel(http);
        httpPanel.AddHeaderCommand.Execute(null);
        httpPanel.Headers[0].Name = "X-Id";
        Assert.Equal("X-Id", http.Headers.Single().Name);
        httpPanel.Retries = 500;
        Assert.Equal(100, http.Retries);
        Assert.True(httpPanel.HasRetries);
    }

    [Fact]
    public void The_operator_says_what_the_left_side_is()
    {
        var panel = Panel(new WorkflowStep { Type = StepType.Condition });
        Assert.True(panel.TakesRight);

        panel.Operator = CompareOperator.CSharp;

        Assert.False(panel.TakesRight);
        Assert.StartsWith("C#", panel.LeftLabel);
        Assert.NotNull(Panel(new WorkflowStep { Type = StepType.Condition }).Problem);
    }

    [Fact]
    public void The_workflow_to_call_is_chosen_from_a_list()
    {
        var step = new WorkflowStep { Type = StepType.CallWorkflow };
        var panel = Panel(step);

        panel.WorkflowChoice = panel.WorkflowChoices[0];

        Assert.Equal("w1", step.Workflow);
        Assert.Equal("Other", panel.WorkflowChoice!.Name);
    }

    [Fact]
    public void A_switch_case_keeps_its_link_when_renamed_and_loses_it_when_removed()
    {
        var flow = new Flow();
        var step = flow.Add(new WorkflowStep { Type = StepType.Switch, Cases = ["new"] });
        var target = flow.Add(Flow.End());
        flow.Link(step, target, StepOutputs.Case("new"));
        var panel = new StepViewModel(step, new StepChoices(), () => _changes++, flow.Workflow);

        panel.Cases[0].Text = "fresh";
        panel.Cases[0].Apply();
        Assert.Equal(["fresh"], step.Cases);
        Assert.Equal(StepOutputs.Case("fresh"), flow.Workflow.Links.Single(l => l.From == step.Id).Output);

        panel.NewCase = "paid";
        panel.AddCaseCommand.Execute(null);
        panel.Cases[1].Text = "fresh";
        panel.Cases[1].Apply();
        Assert.Equal("paid", panel.Cases[1].Text);

        panel.Cases[0].RemoveCommand.Execute(null);
        Assert.Equal(["paid"], step.Cases);
        Assert.DoesNotContain(flow.Workflow.Links, l => l.From == step.Id);
    }

    [Fact]
    public void The_panel_says_what_every_script_has_from_the_settings()
    {
        var step = new WorkflowStep { Type = StepType.Script };
        var panel = new StepViewModel(step, new StepChoices { GlobalImports = l => l == ScriptLanguage.CSharp ? ["System.Globalization"] : [] }, () => { });

        Assert.Contains("System.Globalization", panel.GlobalImportsText);
        panel.Language = ScriptLanguage.Go;
        Assert.Null(panel.GlobalImportsText);
    }

    [Fact]
    public void Global_variables_are_offered_to_insert_as_the_language_reads_them()
    {
        var step = new WorkflowStep { Type = StepType.Script };
        var panel = new StepViewModel(step, new StepChoices { Globals = () => ["orderId"] }, () => { });

        Assert.Equal(["orderId"], panel.VariableChoices);
        Assert.Equal("Vars[\"orderId\"]", panel.Reference("orderId"));
        panel.Language = ScriptLanguage.JavaScript;
        Assert.Equal("vars.orderId", panel.Reference("orderId"));
        panel.Language = ScriptLanguage.Go;
        Assert.Equal("data.Vars[\"orderId\"]", panel.Reference("orderId"));
        Assert.False(new StepViewModel(step, new StepChoices(), () => { }).HasVariableChoices);
    }
}
