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
}
