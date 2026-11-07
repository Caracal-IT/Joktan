using System;
using System.Linq;
using Avalonia;
using Joktan.Workflows.Controls;
using Joktan.Workflows.Model;
using Xunit;

namespace Joktan.Workflows.Tests.Controls;

public class CompactLookTests
{
    // Start, an If, a step on each side, and an end both lead to.
    private static (Workflow Workflow, WorkflowStep Start, WorkflowStep Check, WorkflowStep Yes, WorkflowStep No, WorkflowStep End) Sample()
    {
        var flow = new Flow();
        var start = flow.Last;
        var check = flow.Add(Flow.If("{{input.a}}", CompareOperator.IsNotEmpty));
        var yes = flow.Add(Flow.Log("yes"));
        var no = flow.Add(Flow.Log("no"));
        var end = flow.Add(Flow.End());
        flow.Link(start, check).Link(check, yes, StepOutputs.True).Link(check, no, StepOutputs.False).Link(yes, end).Link(no, end);
        return (flow.Workflow, start, check, yes, no, end);
    }

    [Fact]
    public void Arranging_lays_the_steps_out_from_the_top_down_with_true_on_the_left()
    {
        var (workflow, start, check, yes, no, end) = Sample();
        workflow.Look = WorkflowLook.Compact;

        DiagramLayout.ArrangeAll(workflow);

        Assert.True(start.Y < check.Y && check.Y < yes.Y && yes.Y < end.Y);
        Assert.Equal(yes.Y, no.Y);
        Assert.True(yes.X < no.X);
        Assert.Equal(start.X, check.X);

        // The end, which both lead to, is under the middle of them.
        Assert.InRange(end.X, yes.X, no.X);
    }

    [Fact]
    public void Lines_leave_an_if_by_its_sides_and_come_in_at_the_top()
    {
        var (workflow, _, check, yes, _, _) = Sample();
        workflow.Look = WorkflowLook.Compact;
        DiagramLayout.ArrangeAll(workflow);

        var route = DiagramLayout.Route(workflow, workflow.Links.Single(l => l.To == yes.Id))!;

        Assert.Equal(new Point(check.X, check.Y + TileLayout.Size / 2), route[0]);
        Assert.Equal(TileLayout.Input(yes), route[^1]);
        Assert.Equal(3, route.Count);
        Assert.Same(workflow.Links.Single(l => l.To == yes.Id), DiagramLayout.HitTest(workflow, new Point((route[0].X + route[1].X) / 2, route[0].Y))!.Link);
    }

    [Fact]
    public void A_line_to_a_step_above_goes_round_the_side()
    {
        var step = new WorkflowStep { Type = StepType.Log, X = 0, Y = 300 };
        var target = new WorkflowStep { Type = StepType.Log, X = 0, Y = 0 };

        var route = TileLayout.Route(TileLayout.LineStart(step, StepOutputs.Next), new Vector(0, 1), TileLayout.Input(target),
            TileLayout.Tile(step), TileLayout.Tile(target));

        // It goes up beside both tiles, not through them.
        for (var i = 0; i + 1 < route.Count; i++)
        {
            if (route[i + 1].Y < route[i].Y)
                Assert.True(route[i].X > TileLayout.Size);
        }

        Assert.Equal(TileLayout.Input(target), route[^1]);
    }

    [Fact]
    public void A_line_to_a_step_beside_goes_up_the_gap_between_them()
    {
        var step = new WorkflowStep { Type = StepType.Log, X = 0, Y = 0 };
        var beside = new WorkflowStep { Type = StepType.Log, X = 200, Y = 0 };

        var route = TileLayout.Route(TileLayout.LineStart(step, StepOutputs.Next), new Vector(0, 1), TileLayout.Input(beside),
            TileLayout.Tile(step), TileLayout.Tile(beside));

        var middle = (TileLayout.Size + 200) / 2;
        Assert.Contains(route, p => Math.Abs(p.X - middle) < 0.001);
        Assert.All(route, p => Assert.True(p.X <= 200 + TileLayout.Size));
    }

    [Fact]
    public void Hovering_a_tile_tells_what_it_does_its_description_and_what_is_wrong()
    {
        var step = new WorkflowStep { Id = "s", Type = StepType.Log, Name = "Log arrival", Description = "Notes the order in the log." };
        var diagram = new WorkflowDiagram { Summary = _ => "order {{vars.orderId}}" };
        diagram.Problems["s"] = "No message.";

        var tip = WorkflowCanvas.TileTip(diagram, step);

        Assert.Equal("Log arrival · Log\n\norder {{vars.orderId}}\n\nNotes the order in the log.\n\n⚠ No message.", tip);
    }

    [Fact]
    public void Failures_leave_from_the_right_and_the_rest_from_the_bottom()
    {
        var step = new WorkflowStep { Type = StepType.Http };

        Assert.Equal(new Vector(0, 1), TileLayout.Direction(step, StepOutputs.Next));
        Assert.Equal(new Vector(1, 0), TileLayout.Direction(step, StepOutputs.Error));
        Assert.Equal(TileLayout.Size, TileLayout.Output(step, StepOutputs.Error).X);
        Assert.Equal(TileLayout.Size + TileLayout.LabelRoom, TileLayout.LineStart(step, StepOutputs.Next).Y);
    }

    [Fact]
    public void Switching_looks_puts_each_step_back_where_it_was_in_that_look()
    {
        var (workflow, _, check, _, _, _) = Sample();
        DiagramLayout.ArrangeAll(workflow);
        (check.X, check.Y) = (500, 60);

        // The first time it is compact, it is laid out afresh, from the top down.
        DiagramLayout.SwitchLook(workflow, WorkflowLook.Compact);
        Assert.Equal(WorkflowLook.Compact, workflow.Look);
        Assert.Equal((500.0, 60.0), (check.OtherX!.Value, check.OtherY!.Value));
        (check.X, check.Y) = (220, 180);

        DiagramLayout.SwitchLook(workflow, WorkflowLook.Detailed);
        Assert.Equal((500.0, 60.0), (check.X, check.Y));

        DiagramLayout.SwitchLook(workflow, WorkflowLook.Compact);
        Assert.Equal((220.0, 180.0), (check.X, check.Y));
    }

    [Fact]
    public void Switching_looks_keeps_each_step_in_its_swimlane()
    {
        var (workflow, start, check, yes, no, end) = Sample();
        DiagramLayout.ArrangeAll(workflow);
        DiagramLayout.AddLane(workflow, "Intake");
        DiagramLayout.AddLane(workflow, "Decision").Height = 800;
        foreach (var step in new[] { yes, no, end })
            step.Y += workflow.Lanes[0].Height;
        Assert.Equal([0, 0, 1, 1, 1], workflow.Steps.Select(s => DiagramLayout.LaneOf(workflow, s)));

        DiagramLayout.SwitchLook(workflow, WorkflowLook.Compact);

        Assert.Equal([0, 0, 1, 1, 1], workflow.Steps.Select(s => DiagramLayout.LaneOf(workflow, s)));
        Assert.True(start.Y < check.Y);
        Assert.All(workflow.Steps, s => Assert.True(DiagramLayout.Box(s, WorkflowLook.Compact).Bottom <= DiagramLayout.LaneTop(workflow, DiagramLayout.LaneOf(workflow, s) + 1)));
    }

    [Fact]
    public void A_step_added_in_one_look_finds_a_free_place_in_the_other()
    {
        var (workflow, _, _, yes, _, _) = Sample();
        DiagramLayout.SwitchLook(workflow, WorkflowLook.Compact);
        var added = new WorkflowStep { Id = "added", Type = StepType.Log, Name = "Added" };
        var place = DiagramLayout.FreePlace(workflow, yes);
        (added.X, added.Y) = (place.X, place.Y);
        workflow.Steps.Add(added);
        workflow.Links.Add(new WorkflowLink { From = yes.Id, To = added.Id });

        DiagramLayout.SwitchLook(workflow, WorkflowLook.Detailed);

        Assert.DoesNotContain(workflow.Steps, s => s != added && DiagramLayout.Box(s).Intersects(DiagramLayout.Box(added)));
        Assert.NotNull(added.OtherX);
    }
}
