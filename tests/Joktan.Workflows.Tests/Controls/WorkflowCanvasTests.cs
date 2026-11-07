using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Joktan.Tests.TestSupport;
using Joktan.Workflows.Controls;
using Joktan.Workflows.Model;
using Xunit;

namespace Joktan.Workflows.Tests.Controls;

public class WorkflowCanvasTests
{
    private static (WorkflowCanvas Canvas, WorkflowDiagram Diagram, Avalonia.Controls.Window Window, WorkflowStep Start, WorkflowStep End) Show(bool editable = true)
    {
        var flow = new Flow();
        var start = flow.Last;
        var end = flow.Add(Flow.End());
        (start.X, start.Y) = (100, 100);
        (end.X, end.Y) = (500, 100);
        var diagram = new WorkflowDiagram { Workflow = flow.Workflow, IsEditable = editable };
        var canvas = new WorkflowCanvas { Diagram = diagram };
        var window = Headless.Show(canvas, width: 900, height: 600);
        return (canvas, diagram, window, start, end);
    }

    private static Point On(WorkflowCanvas canvas, Point point) => canvas.FromCanvas(point);

    [Fact]
    public Task Dragging_a_step_moves_it_to_the_grid() => Headless.Run(() =>
    {
        var (canvas, diagram, window, start, _) = Show();
        string? moved = null;
        diagram.Moved = (id, _) => moved = id;
        var from = On(canvas, new Point(start.X + 40, start.Y + 10));

        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(from + new Vector(53 * canvas.Zoom, 47 * canvas.Zoom));
        window.MouseUp(from + new Vector(53 * canvas.Zoom, 47 * canvas.Zoom), MouseButton.Left);

        Assert.Equal(start.Id, moved);
        Assert.Equal(start.Id, diagram.SelectedStep);
        Assert.Equal(0, start.X % DiagramLayout.Grid);
        Assert.InRange(start.X, 140, 160);
        window.Close();
    });

    [Fact]
    public Task Dragging_from_an_output_to_a_step_links_them() => Headless.Run(() =>
    {
        var (canvas, diagram, window, start, end) = Show();
        (string, string, string)? linked = null;
        diagram.Linked = (from, output, to) => linked = (from, output, to);
        var port = On(canvas, DiagramLayout.Output(start, StepOutputs.Next));
        var target = On(canvas, new Point(end.X + 50, end.Y + 30));

        window.MouseDown(port, MouseButton.Left);
        window.MouseMove(target);
        window.MouseUp(target, MouseButton.Left);

        Assert.Equal((start.Id, StepOutputs.Next, end.Id), linked);
        window.Close();
    });

    [Fact]
    public Task Delete_asks_to_remove_what_is_selected() => Headless.Run(() =>
    {
        var (canvas, diagram, window, _, end) = Show();
        var deletes = 0;
        diagram.DeleteRequested = () => deletes++;

        window.MouseDown(On(canvas, new Point(end.X + 20, end.Y + 10)), MouseButton.Left);
        window.MouseUp(On(canvas, new Point(end.X + 20, end.Y + 10)), MouseButton.Left);
        window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
        Headless.Settle();

        Assert.Equal(1, deletes);

        // Clicking the background lets go of it.
        window.MouseDown(new Point(5, 590), MouseButton.Left);
        window.MouseUp(new Point(5, 590), MouseButton.Left);
        Assert.Null(diagram.SelectedStep);
        window.Close();
    });

    [Fact]
    public Task A_diagram_that_cannot_be_edited_only_selects() => Headless.Run(() =>
    {
        var (canvas, diagram, window, start, _) = Show(editable: false);
        var x = start.X;
        var at = On(canvas, new Point(start.X + 40, start.Y + 10));

        window.MouseDown(at, MouseButton.Left);
        window.MouseMove(at + new Vector(100, 0));
        window.MouseUp(at + new Vector(100, 0), MouseButton.Left);

        Assert.Equal(x, start.X);
        Assert.Equal(start.Id, diagram.SelectedStep);
        window.Close();
    });

    [Fact]
    public Task Zooming_keeps_within_its_limits() => Headless.Run(() =>
    {
        var (canvas, _, window, _, _) = Show();

        for (var i = 0; i < 30; i++)
            canvas.ZoomBy(1.5);
        Assert.Equal(2, canvas.Zoom);
        for (var i = 0; i < 30; i++)
            canvas.ZoomBy(0.5);
        Assert.Equal(0.3, canvas.Zoom, 3);

        canvas.FitToView();
        Assert.InRange(canvas.Zoom, 0.3, 1);
        window.Close();
    });

    [Fact]
    public Task A_run_shows_its_steps_in_the_colours_of_how_they_did() => Headless.Run(() =>
    {
        var (canvas, diagram, window, start, end) = Show(editable: false);
        static bool Green(Color c) => c.G > 120 && c.R < 120 && c.B < 120;
        var before = Headless.CountPixels(window, Green);

        diagram.Show(new WorkflowRun
        {
            Status = RunStatus.Completed,
            Steps = [new() { StepId = start.Id, Status = StepStatus.Done, Output = StepOutputs.Next }, new() { StepId = end.Id, Status = StepStatus.Done }],
        });
        Headless.Settle();

        Assert.True(Headless.CountPixels(window, Green) > before + 200);
        window.Close();
    });
}
