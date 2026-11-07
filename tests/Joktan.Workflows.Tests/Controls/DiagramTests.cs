using System.Linq;
using System.Threading.Tasks;
using Joktan.Tests.TestSupport;
using Avalonia;
using Joktan.Workflows.Controls;
using Joktan.Workflows.Model;
using Xunit;

namespace Joktan.Workflows.Tests.Controls;

public class DiagramTests
{
    private static (Workflow Workflow, WorkflowStep Start, WorkflowStep Check, WorkflowStep End) Sample()
    {
        var flow = new Flow();
        var start = flow.Last;
        var check = flow.Add(Flow.If("{{input.a}}", CompareOperator.IsNotEmpty));
        var end = flow.Add(Flow.End());
        (check.X, check.Y) = (300, 0);
        (end.X, end.Y) = (600, 200);
        flow.Link(start, check).Link(check, end, StepOutputs.True);
        return (flow.Workflow, start, check, end);
    }

    [Fact]
    public void Outputs_are_found_before_steps_and_steps_before_links()
    {
        var (workflow, start, check, _) = Sample();

        var output = DiagramLayout.HitTest(workflow, DiagramLayout.Output(check, StepOutputs.False))!;
        Assert.Equal((check, StepOutputs.False), (output.Step, output.Output));

        var body = DiagramLayout.HitTest(workflow, new Point(check.X + 20, check.Y + 10))!;
        Assert.Equal(check, body.Step);
        Assert.Null(body.Output);

        var (from, to, direction) = DiagramLayout.Ends(workflow, workflow.Links[0])!.Value;
        var middle = DiagramLayout.At(DiagramLayout.Curve(from, to, direction), 0.5);
        Assert.Same(workflow.Links[0], DiagramLayout.HitTest(workflow, middle)!.Link);

        Assert.Null(DiagramLayout.HitTest(workflow, new Point(-500, -500)));

        // A condition is a diamond: in at its left point, true out of its right, false out of its bottom.
        Assert.Equal(new Point(check.X, check.Y + DiagramLayout.DecisionHeight / 2), DiagramLayout.Input(check));
        Assert.Equal(new Point(check.X + DiagramLayout.Width, check.Y + DiagramLayout.DecisionHeight / 2), DiagramLayout.Output(check, StepOutputs.True));
        Assert.Equal(new Point(check.X + DiagramLayout.Width / 2, check.Y + DiagramLayout.DecisionHeight), DiagramLayout.Output(check, StepOutputs.False));
        Assert.Equal(new Vector(0, 1), DiagramLayout.Direction(check, StepOutputs.False));
        Assert.Equal(start.Y + DiagramLayout.Header / 2, DiagramLayout.Input(start).Y);
    }

    [Theory]
    [InlineData(StepType.Http)]
    [InlineData(StepType.Script)]
    [InlineData(StepType.Start)]
    [InlineData(StepType.Delay)]
    [InlineData(StepType.Fail)]
    [InlineData(StepType.UserInput)]
    [InlineData(StepType.ForEach)]
    [InlineData(StepType.Switch)]
    public Task Ports_sit_on_the_outline_of_the_shape(StepType type) => Headless.Run(() =>
    {
        var step = new WorkflowStep { Type = type, X = 100, Y = 50 };
        var outline = DiagramLayout.Outline(step);

        foreach (var output in StepTypes.Outputs(type))
        {
            var port = DiagramLayout.Output(step, output);
            Assert.True(outline.FillContains(port - new Vector(2, 0)), $"{output} is not on the edge");
            Assert.False(outline.FillContains(port + new Vector(3, 0)), $"{output} is inside the shape");
        }

        if (StepTypes.HasInput(type))
        {
            var input = DiagramLayout.Input(step);
            Assert.True(outline.FillContains(input + new Vector(2, 0)));
            Assert.False(outline.FillContains(input - new Vector(3, 0)));
        }
    });

    [Fact]
    public void Each_kind_of_step_has_its_shape()
    {
        Assert.Equal(StepShape.Terminator, DiagramLayout.Shape(StepType.Start));
        Assert.Equal(StepShape.Decision, DiagramLayout.Shape(StepType.Condition));
        Assert.Equal(StepShape.InputOutput, DiagramLayout.Shape(StepType.MqttPublish));
        Assert.Equal(StepShape.Octagon, DiagramLayout.Shape(StepType.Fail));
        Assert.Equal(StepShape.Loop, DiagramLayout.Shape(StepType.ForEach));
        Assert.Equal(StepShape.Branch, DiagramLayout.Shape(StepType.Switch));
        Assert.Equal(12, StepTypes.All.Select(DiagramLayout.Shape).Distinct().Count());
    }

    [Fact]
    public void A_free_place_does_not_overlap_a_step()
    {
        var (workflow, start, _, _) = Sample();

        var after = DiagramLayout.FreePlace(workflow, start);
        var below = DiagramLayout.FreePlace(workflow, null);

        foreach (var place in new[] { after, below })
            Assert.DoesNotContain(workflow.Steps, s => DiagramLayout.Box(s).Intersects(new Rect(place, new Size(DiagramLayout.Width, 40))));
        Assert.Equal(new Point(20, 40), DiagramLayout.Snap(new Point(27, 33)));
    }

    [Fact]
    public void A_run_marks_its_steps_and_the_links_it_took()
    {
        var (workflow, start, check, end) = Sample();
        var diagram = new WorkflowDiagram { Workflow = workflow };
        var changes = 0;
        diagram.Changed += () => changes++;
        var run = new WorkflowRun
        {
            Status = RunStatus.Completed,
            Steps =
            [
                new() { StepId = start.Id, Status = StepStatus.Done, Output = StepOutputs.Next },
                new() { StepId = check.Id, Status = StepStatus.Done, Output = StepOutputs.True },
                new() { StepId = end.Id, Status = StepStatus.Done, Output = StepOutputs.Next },
            ],
        };

        diagram.Show(run);

        Assert.All(new[] { start, check, end }, s => Assert.Equal(StepMark.Done, diagram.Marks[s.Id].Mark));
        Assert.Equal(workflow.Links.Select(WorkflowDiagram.KeyOf).ToHashSet(), diagram.Taken);
        Assert.Equal(1, changes);

        diagram.Show(null);
        Assert.Empty(diagram.Marks);
    }

    [Fact]
    public void A_paused_run_marks_the_step_it_goes_on_with()
    {
        var (workflow, start, check, _) = Sample();
        var diagram = new WorkflowDiagram { Workflow = workflow };

        diagram.Show(new WorkflowRun
        {
            Status = RunStatus.Paused,
            CurrentStep = check.Id,
            Steps = [new() { StepId = start.Id, Status = StepStatus.Done, Output = StepOutputs.Next }],
        });

        Assert.Equal(StepMark.Waiting, diagram.Marks[check.Id].Mark);
        Assert.Contains($"{start.Id}|next|{check.Id}", diagram.Taken);
    }

    [Fact]
    public void Steps_say_what_they_do()
    {
        Assert.Equal("GET https://x", StepSummary.Of(new WorkflowStep { Type = StepType.Http, Method = "GET", Url = "https://x" }));
        Assert.Equal("Local → a/b", StepSummary.Of(new WorkflowStep { Type = StepType.MqttPublish, Client = "Local", Topic = "a/b" }));
        Assert.Equal("{{a}} is greater than 3", StepSummary.Of(new WorkflowStep { Type = StepType.Condition, Left = "{{a}}", Operator = CompareOperator.GreaterThan, Right = "3" }));
        Assert.Equal("JavaScript: const a = 1;", StepSummary.Of(new WorkflowStep { Type = StepType.Script, Language = ScriptLanguage.JavaScript, Code = "// note\nconst a = 1;" }));
        Assert.Equal("Wait 5m", StepSummary.Of(new WorkflowStep { Type = StepType.Delay, Duration = "5m" }));
    }

    [Fact]
    public Task A_description_makes_room_under_the_summary_and_moves_the_outputs_down() => Headless.Run(() =>
    {
        var plain = new WorkflowStep { Type = StepType.Http, X = 0, Y = 0 };
        var described = new WorkflowStep { Type = StepType.Http, X = 0, Y = 0, Description = "Fetches the order so the next steps know its total" };

        var room = DiagramLayout.DescriptionHeight(described);
        Assert.True(room > 0);
        Assert.Equal(0, DiagramLayout.DescriptionHeight(plain));
        Assert.Equal(DiagramLayout.Height(plain) + room, DiagramLayout.Height(described));
        Assert.Equal(DiagramLayout.Output(plain, StepOutputs.Next).Y + room, DiagramLayout.Output(described, StepOutputs.Next).Y);

        var port = DiagramLayout.Output(described, StepOutputs.Error);
        Assert.True(DiagramLayout.Outline(described).FillContains(port - new Vector(2, 0)));

        // A diamond keeps its size; its description goes inside it.
        Assert.Equal(DiagramLayout.DecisionHeight, DiagramLayout.Height(new WorkflowStep { Type = StepType.Condition, Description = "x" }));
    });

    [Fact]
    public void Swimlanes_hold_the_steps_in_them_and_move_the_steps_under_them_when_they_change()
    {
        var workflow = new Workflow { Steps = [new WorkflowStep { Id = "a", Type = StepType.Log, X = 100, Y = 100 }] };
        var first = DiagramLayout.AddLane(workflow, "Sales");
        Assert.Equal(0, DiagramLayout.LaneOf(workflow, workflow.Steps[0]));
        Assert.True(workflow.LanesTop <= 100 - DiagramLayout.Grid);
        DiagramLayout.AddLane(workflow, "Stores");
        var below = new WorkflowStep { Id = "b", Type = StepType.Log, X = 100, Y = DiagramLayout.LaneTop(workflow, 1) + 40 };
        workflow.Steps.Add(below);
        Assert.Equal(1, DiagramLayout.LaneOf(workflow, below));

        // Taller: the step under it keeps to its lane. Shorter: never past the steps in it.
        var y = below.Y;
        DiagramLayout.ResizeLane(workflow, 0, first.Height + 100);
        Assert.Equal(y + 100, below.Y);
        Assert.Equal(1, DiagramLayout.LaneOf(workflow, below));
        DiagramLayout.ResizeLane(workflow, 0, 10);
        Assert.Equal(DiagramLayout.MinHeight(workflow, 0), first.Height);
        Assert.Equal(0, DiagramLayout.LaneOf(workflow, workflow.Steps[0]));
        Assert.Equal(1, DiagramLayout.LaneOf(workflow, below));
        Assert.Equal(0, DiagramLayout.LaneEdgeAt(workflow, DiagramLayout.LaneTop(workflow, 1) + 2, 5));

        // Removing a lane moves the ones under it up into its place.
        y = below.Y;
        var height = first.Height;
        DiagramLayout.RemoveLane(workflow, 0);
        Assert.Equal(y - height, below.Y);
        Assert.Equal(0, DiagramLayout.LaneOf(workflow, below));
        Assert.Equal("Stores", Assert.Single(workflow.Lanes).Name);
    }
}
