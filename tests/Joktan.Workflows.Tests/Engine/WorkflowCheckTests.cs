using System.Linq;
using Joktan.Workflows.Engine;
using Joktan.Workflows.Model;
using Xunit;

namespace Joktan.Workflows.Tests.Engine;

public class WorkflowCheckTests
{
    [Fact]
    public void A_workflow_without_one_start_cannot_run()
    {
        Assert.Contains(WorkflowCheck.Problems(new Workflow()), p => p.IsBlocking);

        var two = new Flow();
        two.Add(new WorkflowStep { Type = StepType.Start });
        Assert.Contains(WorkflowCheck.Problems(two.Workflow), p => p.IsBlocking);

        Assert.DoesNotContain(WorkflowCheck.Problems(new Flow().Then(Flow.End()).Workflow), p => p.IsBlocking);
    }

    [Fact]
    public void Steps_that_nothing_leads_to_are_pointed_out()
    {
        var flow = new Flow().Then(Flow.End());
        var alone = flow.Add(Flow.Log("never"));

        var problem = Assert.Single(WorkflowCheck.Problems(flow.Workflow));

        Assert.Equal(alone.Id, problem.StepId);
        Assert.False(problem.IsBlocking);
    }

    [Fact]
    public void What_a_step_lacks_is_pointed_out()
    {
        Assert.NotNull(WorkflowCheck.StepProblem(new WorkflowStep { Type = StepType.Http }));
        Assert.Null(WorkflowCheck.StepProblem(new WorkflowStep { Type = StepType.Http, RequestPath = "a" }));
        Assert.NotNull(WorkflowCheck.StepProblem(new WorkflowStep { Type = StepType.Delay, Duration = "soon" }));
        Assert.Null(WorkflowCheck.StepProblem(new WorkflowStep { Type = StepType.Delay, Duration = "{{input.wait}}" }));
        Assert.NotNull(WorkflowCheck.StepProblem(new WorkflowStep { Type = StepType.MqttWait, Client = "c" }));
        Assert.NotNull(WorkflowCheck.StepProblem(new WorkflowStep { Type = StepType.UserInput, Message = "Ok?", Duration = "later" }));
        Assert.NotNull(WorkflowCheck.StepProblem(new WorkflowStep { Type = StepType.Grpc, Address = "x", Service = "s" }));
    }

    [Fact]
    public void An_mqtt_trigger_needs_a_client_and_a_good_topic()
    {
        var flow = new Flow().Then(Flow.End());
        flow.Workflow.Triggers = new WorkflowTriggers { Mqtt = true, MqttTopic = "a/#/b" };

        Assert.Equal(2, WorkflowCheck.Problems(flow.Workflow).Count(p => p.StepId is null));
    }

    [Fact]
    public void A_schedule_must_start_with_a_sample_input_that_is_there()
    {
        var flow = new Flow().Then(Flow.End());
        flow.Workflow.Triggers = new WorkflowTriggers { Scheduled = true, ScheduleInput = "Gone" };
        Assert.Contains(WorkflowCheck.Problems(flow.Workflow), p => p.Text.Contains("Gone"));

        flow.Workflow.Samples.Add(new NamedInput { Name = "Gone" });
        Assert.Empty(WorkflowCheck.Problems(flow.Workflow));
    }

    [Fact]
    public void A_retry_delay_must_be_a_length_of_time()
    {
        var flow = new Flow().Then(new WorkflowStep { Type = StepType.Log, Message = "x", Retries = 2, RetryDelay = "a while" });

        Assert.Single(WorkflowCheck.Problems(flow.Workflow));
    }

    [Fact]
    public void A_database_step_needs_its_database_table_and_what_it_changes()
    {
        var step = new WorkflowStep { Type = StepType.DatabaseRead };
        Assert.Equal(Resources.Strings.Check_ChooseDatabase, WorkflowCheck.StepProblem(step));
        step.Database = "db1";
        Assert.Equal(Resources.Strings.Check_ChooseTable, WorkflowCheck.StepProblem(step));
        step.Table = "orders";
        Assert.Null(WorkflowCheck.StepProblem(step));

        step.Filters = [new() { Column = "id", Operator = CompareOperator.CSharp }];
        Assert.Equal(Resources.Strings.Check_FilterCannotBeCSharp, WorkflowCheck.StepProblem(step));
        step.Filters = [new() { Column = "id", Value = "" }];
        Assert.Null(WorkflowCheck.StepProblem(step));
        step.Columns = [new() { Column = "name", Variable = "2bad" }];
        Assert.Equal(Resources.Strings.Check_ColumnVariableName, WorkflowCheck.StepProblem(step));

        step.Type = StepType.DatabaseInsert;
        step.Columns = [new() { Column = "name" }, new() { Column = "name " }, new() { Column = "name", SetsValue = false, Variable = "n" }];
        Assert.Equal(Resources.Strings.Check_ColumnTwice, WorkflowCheck.StepProblem(step));
        step.Columns = [new() { Column = "id", SetsValue = false, Variable = "id" }];
        Assert.Null(WorkflowCheck.StepProblem(step));

        step.Type = StepType.DatabaseUpsert;
        Assert.Equal(Resources.Strings.Check_UpsertNeedsKey, WorkflowCheck.StepProblem(step));
        step.Columns = [new() { Column = "sku", Match = true }];
        Assert.Equal(Resources.Strings.Check_KeyNeedsValue, WorkflowCheck.StepProblem(step));
        step.Columns[0].Value = "{{input.sku}}";
        Assert.Null(WorkflowCheck.StepProblem(step));
        Assert.Equal("public.orders", StepSummary.Of(step));

        step.Type = StepType.DatabaseUpdate;
        step.Columns = [];
        step.Filters = [];
        Assert.Equal(Resources.Strings.Check_UpdateNeedsColumns, WorkflowCheck.StepProblem(step));
        step.Columns = [new() { Column = "name", Value = "x" }];
        Assert.Equal(Resources.Strings.Check_NeedsFilter, WorkflowCheck.StepProblem(step));
        step.Filters = [new() { Column = "id", Value = " " }];
        Assert.Equal(Resources.Strings.Check_FilterNeedsValue, WorkflowCheck.StepProblem(step));
        step.Filters = [new() { Column = "done", Operator = CompareOperator.IsEmpty }];
        Assert.Equal(Resources.Strings.Check_FilterNeedsComparison, WorkflowCheck.StepProblem(step));
        step.Filters.Add(new() { Column = "id", Value = "{{input.id}}" });
        Assert.Null(WorkflowCheck.StepProblem(step));
        Assert.Equal($"public.orders where done {CompareOperator.IsEmpty.Text()} and id {CompareOperator.Equals.Text()} {{{{input.id}}}}", StepSummary.Of(step));

        step.Type = StepType.DatabaseDelete;
        Assert.Null(WorkflowCheck.StepProblem(step));
        step.Filters = [];
        Assert.Equal(Resources.Strings.Check_NeedsFilter, WorkflowCheck.StepProblem(step));
        Assert.Equal(Resources.Strings.Summary_ChooseATable, StepSummary.Of(new WorkflowStep { Type = StepType.DatabaseDelete }));
    }

    [Fact]
    public void A_user_inputs_metadata_needs_names_of_its_own()
    {
        var step = new WorkflowStep
        {
            Type = StepType.UserInput, Message = "Ok?", Metadata = [new() { Name = "", Value = "x" }], AssignedTo = new() { Id = "Approver", Name = "Approver" },
        };
        Assert.Equal(Resources.Strings.Check_MetadataNeedsName, WorkflowCheck.StepProblem(step));
        step.Metadata = [new() { Name = "serial", Value = "1" }];
        Assert.Equal(Resources.Strings.Check_MetadataReserved, WorkflowCheck.StepProblem(step));
        step.Metadata = [new() { Name = "assignedTo", Value = "1" }];
        Assert.Equal(Resources.Strings.Check_MetadataReserved, WorkflowCheck.StepProblem(step));
        step.Metadata = [new() { Name = "a", Value = "1" }, new() { Name = "a ", Value = "2" }];
        Assert.Equal(Resources.Strings.Check_MetadataTwice, WorkflowCheck.StepProblem(step));
        step.Metadata = [new() { Name = "a", Value = "{{vars.a}}" }, new() { Name = "", Value = "" }];
        Assert.Null(WorkflowCheck.StepProblem(step));
    }

    [Fact]
    public void A_user_input_step_is_assigned_to_a_role_and_warned_about_a_user()
    {
        var step = new WorkflowStep { Type = StepType.UserInput, Message = "Ok?" };
        Assert.Equal(Resources.Strings.Check_AssignTheQuestion, WorkflowCheck.StepProblem(step));

        step.AssignedTo = new() { Kind = AssigneeKind.Role, Id = "Approver", Name = "Approver" };
        Assert.Null(WorkflowCheck.StepProblem(step));
        Assert.Null(WorkflowCheck.AssignmentWarning(step));

        // One user is allowed, but it is bad practice, so it is a problem that does not stop the run.
        step.AssignedTo = new() { Kind = AssigneeKind.User, Id = "7", Name = "Ann Lee" };
        Assert.Null(WorkflowCheck.StepProblem(step));
        Assert.Equal(Resources.Strings.Check_AssignedToAUser, WorkflowCheck.AssignmentWarning(step));
        var workflow = new Flow().Then(step).Then(Flow.End()).Workflow;
        Assert.Contains(WorkflowCheck.Problems(workflow), p => p.StepId == step.Id && p.Text == Resources.Strings.Check_AssignedToAUser && !p.IsBlocking);
    }

    [Fact]
    public void An_escalation_needs_a_time_and_someone_and_should_come_before_the_time_limit()
    {
        var step = new WorkflowStep { Type = StepType.UserInput, Message = "Ok?", AssignedTo = new() { Id = "Approver" }, Escalate = true };
        Assert.Equal(Resources.Strings.Check_EscalateAfter, WorkflowCheck.StepProblem(step));
        step.EscalateAfter = "{{vars.wait}}";
        Assert.Equal(Resources.Strings.Check_EscalateToWhom, WorkflowCheck.StepProblem(step));
        step.EscalateAfter = "30m";
        step.EscalateTo = new() { Kind = AssigneeKind.Role, Id = "Manager" };
        Assert.Null(WorkflowCheck.StepProblem(step));
        Assert.Null(WorkflowCheck.AssignmentWarning(step));

        step.Duration = "30m";
        Assert.Equal(Resources.Strings.Check_EscalatesTooLate, WorkflowCheck.AssignmentWarning(step));
        step.Duration = "1h";
        step.EscalateTo = new() { Kind = AssigneeKind.User, Id = "3", Name = "Bo" };
        Assert.Equal(Resources.Strings.Check_EscalatedToAUser, WorkflowCheck.AssignmentWarning(step));

        // Switched off, what it was set to does not matter.
        step.Escalate = false;
        step.EscalateAfter = "";
        Assert.Null(WorkflowCheck.StepProblem(step));
        Assert.Null(WorkflowCheck.AssignmentWarning(step));
    }

    [Fact]
    public void The_database_steps_have_a_section_of_their_own_on_the_palette()
    {
        var database = StepTypes.Palette.Single(g => g.Key == StepTypes.Group.Database);
        Assert.Equal([StepType.DatabaseRead, StepType.DatabaseInsert, StepType.DatabaseUpdate, StepType.DatabaseUpsert, StepType.DatabaseDelete], database);
        Assert.All(database, t => Assert.NotNull(StepTypes.DatabaseActionOf(t)));
        Assert.Null(StepTypes.DatabaseActionOf(StepType.Log));
        Assert.Equal(Resources.Strings.StepGroup_Database, StepTypes.Group.Database.Text());
    }
}
