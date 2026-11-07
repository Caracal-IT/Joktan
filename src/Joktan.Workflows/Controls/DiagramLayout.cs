using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Joktan.Workflows.Model;

namespace Joktan.Workflows.Controls;

/// <summary>The flowchart shape a step is drawn in.</summary>
public enum StepShape
{
    /// <summary>A box, for a step that works on data.</summary>
    Process,

    /// <summary>A box with rounded ends, where a run begins or ends.</summary>
    Terminator,

    /// <summary>A diamond, for a choice.</summary>
    Decision,

    /// <summary>Flat on the left and round on the right, for a wait.</summary>
    Delay,

    /// <summary>A parallelogram, for what goes out or comes in.</summary>
    InputOutput,

    /// <summary>A box with bars at its sides, for another workflow.</summary>
    Subroutine,

    /// <summary>A box whose top slopes, for what a person types.</summary>
    ManualInput,

    /// <summary>A hexagon, for code.</summary>
    Hexagon,

    /// <summary>An octagon, like a stop sign, for a failure.</summary>
    Octagon,

    /// <summary>A page with a wavy foot, for a line in the log.</summary>
    Document,

    /// <summary>A box with its top corners cut off, the flowchart's loop limit, for a for each or while.</summary>
    Loop,

    /// <summary>A box pointed on its left, for a switch that branches many ways.</summary>
    Branch,
}

/// <summary>What is at a point of the canvas.</summary>
public sealed record DiagramHit(WorkflowStep? Step, string? Output = null, WorkflowLink? Link = null);

/// <summary>Where a step's box, its input and its outputs are drawn, in the canvas's own units before zooming.</summary>
public static class DiagramLayout
{
    public const double Width = 200;
    public const double Header = 28;
    public const double SummaryHeight = 22;
    public const double Row = 20;
    public const double PortRadius = 6;
    public const double Grid = 20;

    /// <summary>How far the slanted sides of a parallelogram, hexagon or octagon lean in.</summary>
    public const double Slant = 16;

    /// <summary>The height of the diamond of a condition, which is taller than the boxes so its words fit.</summary>
    public const double DecisionHeight = 140;

    /// <summary>The rounding of a start or end, and how deep the wave under a log is.</summary>
    private const double TerminatorRadius = 22;
    private const double Wave = 6;

    public static StepShape Shape(StepType type) => type switch
    {
        StepType.Start or StepType.End => StepShape.Terminator,
        StepType.Condition => StepShape.Decision,
        StepType.Delay => StepShape.Delay,
        StepType.Http or StepType.Grpc or StepType.MqttPublish or StepType.MqttWait => StepShape.InputOutput,
        StepType.CallWorkflow => StepShape.Subroutine,
        StepType.UserInput => StepShape.ManualInput,
        StepType.Script => StepShape.Hexagon,
        StepType.Fail => StepShape.Octagon,
        StepType.Log => StepShape.Document,
        StepType.ForEach or StepType.While => StepShape.Loop,
        StepType.Switch => StepShape.Branch,
        _ => StepShape.Process,
    };

    /// <summary>The room a step's description takes under its summary: up to two lines, or none when it has none.</summary>
    public const double DescriptionLines = 2;

    public const double DescriptionLine = 14;

    public static double DescriptionHeight(WorkflowStep step)
        => step.Description.Trim().Length == 0 || Shape(step.Type) == StepShape.Decision ? 0 : DescriptionLines * DescriptionLine + 4;

    // Where the outputs start, below the header, summary and description.
    private static double Body(WorkflowStep step) => Header + SummaryHeight + DescriptionHeight(step);

    public static double Height(WorkflowStep step) => Shape(step.Type) switch
    {
        StepShape.Decision => DecisionHeight,
        StepShape.Document => Body(step) + StepTypes.Outputs(step).Count * Row + 8 + Wave,
        _ => Body(step) + StepTypes.Outputs(step).Count * Row + 8,
    };

    public static Rect Box(WorkflowStep step) => new(step.X, step.Y, Width, Height(step));

    /// <summary>Where links come in: the left of the header, or the left point of a diamond.</summary>
    public static Point Input(WorkflowStep step)
    {
        var y = Shape(step.Type) == StepShape.Decision ? Height(step) / 2 : Header / 2;
        return new Point(step.X + LeftEdge(step, y), step.Y + y);
    }

    /// <summary>Where an output leaves: down the right side, one row each; a diamond's from its points.</summary>
    public static Point Output(WorkflowStep step, string output)
    {
        if (Shape(step.Type) == StepShape.Decision)
        {
            var height = Height(step);
            return output switch
            {
                StepOutputs.True => new Point(step.X + Width, step.Y + height / 2),
                StepOutputs.False => new Point(step.X + Width / 2, step.Y + height),
                _ => new Point(step.X + Width * 0.75, step.Y + height * 0.75),
            };
        }

        var index = Math.Max(0, IndexOf(StepTypes.Outputs(step), output));
        var y = Body(step) + index * Row + Row / 2;
        return new Point(step.X + RightEdge(step, y), step.Y + y);
    }

    /// <summary>Which way a link leaves an output: to the right, but down from the bottom of a diamond.</summary>
    public static Vector Direction(WorkflowStep step, string output)
        => Shape(step.Type) != StepShape.Decision ? new Vector(1, 0)
            : output switch
            {
                StepOutputs.True => new Vector(1, 0),
                StepOutputs.False => new Vector(0, 1),
                _ => new Vector(0.7071, 0.7071),
            };

    /// <summary>How far in from the left of the box the outline is, at a height within the step.</summary>
    public static double LeftEdge(WorkflowStep step, double y)
    {
        var height = Height(step);
        return Shape(step.Type) switch
        {
            StepShape.Terminator => Rounded(y, height, TerminatorRadius),
            StepShape.Decision => Width / 2 * Math.Abs(y - height / 2) / (height / 2),
            StepShape.InputOutput => Slant * (1 - y / height),
            StepShape.Hexagon => Slant * Math.Abs(y - height / 2) / (height / 2),
            StepShape.Octagon => Math.Max(0, Math.Max(Slant - y, y - (height - Slant))),
            StepShape.Loop => Math.Max(0, Slant - y),
            StepShape.Branch => Slant * Math.Abs(y - height / 2) / (height / 2),
            _ => 0,
        };
    }

    /// <summary>Where the outline is on the right, from the left of the box, at a height within the step.</summary>
    public static double RightEdge(WorkflowStep step, double y)
    {
        var height = Height(step);
        return Shape(step.Type) switch
        {
            StepShape.Terminator => Width - Rounded(y, height, TerminatorRadius),
            StepShape.Decision => Width - LeftEdge(step, y),
            StepShape.Delay => Width - Rounded(y, height, Math.Min(height / 2, Width / 2)),
            StepShape.InputOutput => Width - Slant * y / height,
            StepShape.Hexagon => Width - LeftEdge(step, y),
            StepShape.Octagon => Width - LeftEdge(step, y),
            StepShape.Loop => Width - LeftEdge(step, y),
            _ => Width,
        };
    }

    // How far a corner of a radius pulls the side in, at a height.
    private static double Rounded(double y, double height, double radius)
    {
        var from = y < radius ? radius - y : y > height - radius ? y - (height - radius) : 0;
        return radius - Math.Sqrt(Math.Max(0, radius * radius - from * from));
    }

    /// <summary>The outline of a step in its shape, on the canvas.</summary>
    public static Geometry Outline(WorkflowStep step)
    {
        var (x, y, w, h) = (step.X, step.Y, Width, Height(step));
        var shape = Shape(step.Type);
        switch (shape)
        {
            case StepShape.Process:
            case StepShape.Subroutine:
                return new RectangleGeometry(new Rect(x, y, w, h), 8, 8);
            case StepShape.Terminator:
                return new RectangleGeometry(new Rect(x, y, w, h), TerminatorRadius, TerminatorRadius);
        }

        var geometry = new StreamGeometry();
        using var g = geometry.Open();
        switch (shape)
        {
            case StepShape.Decision:
                Polygon(g, (x + w / 2, y), (x + w, y + h / 2), (x + w / 2, y + h), (x, y + h / 2));
                break;
            case StepShape.Delay:
                var radius = Math.Min(h / 2, w / 2);
                g.BeginFigure(new Point(x, y), true);
                g.LineTo(new Point(x + w - radius, y));
                g.ArcTo(new Point(x + w - radius, y + h), new Size(radius, h / 2), 0, false, SweepDirection.Clockwise);
                g.LineTo(new Point(x, y + h));
                g.EndFigure(true);
                break;
            case StepShape.InputOutput:
                Polygon(g, (x + Slant, y), (x + w, y), (x + w - Slant, y + h), (x, y + h));
                break;
            case StepShape.Hexagon:
                Polygon(g, (x + Slant, y), (x + w - Slant, y), (x + w, y + h / 2), (x + w - Slant, y + h), (x + Slant, y + h), (x, y + h / 2));
                break;
            case StepShape.Octagon:
                Polygon(g, (x + Slant, y), (x + w - Slant, y), (x + w, y + Slant), (x + w, y + h - Slant),
                    (x + w - Slant, y + h), (x + Slant, y + h), (x, y + h - Slant), (x, y + Slant));
                break;
            case StepShape.Loop:
                Polygon(g, (x + Slant, y), (x + w - Slant, y), (x + w, y + Slant), (x + w, y + h), (x, y + h), (x, y + Slant));
                break;
            case StepShape.Branch:
                Polygon(g, (x + Slant, y), (x + w, y), (x + w, y + h), (x + Slant, y + h), (x, y + h / 2));
                break;
            case StepShape.ManualInput:
                Polygon(g, (x, y + 10), (x + w, y), (x + w, y + h), (x, y + h));
                break;
            case StepShape.Document:
                g.BeginFigure(new Point(x, y), true);
                g.LineTo(new Point(x + w, y));
                g.LineTo(new Point(x + w, y + h - Wave));
                g.CubicBezierTo(new Point(x + w * 0.75, y + h - Wave * 3), new Point(x + w * 0.25, y + h + Wave), new Point(x, y + h - Wave));
                g.EndFigure(true);
                break;
        }

        return geometry;
    }

    private static void Polygon(StreamGeometryContext g, params (double X, double Y)[] points)
    {
        g.BeginFigure(new Point(points[0].X, points[0].Y), true);
        foreach (var (px, py) in points.Skip(1))
            g.LineTo(new Point(px, py));
        g.EndFigure(true);
    }

    /// <summary>The control points of a link's curve, from an output, leaving in a direction, to an input.</summary>
    public static (Point Start, Point Control1, Point Control2, Point End) Curve(Point from, Point to, Vector? direction = null)
    {
        var reach = Math.Max(40, Math.Max(Math.Abs(to.X - from.X), Math.Abs(to.Y - from.Y)) / 2);
        var leave = direction ?? new Vector(1, 0);
        return (from, from + leave * reach, new Point(to.X - reach, to.Y), to);
    }

    /// <summary>A step, an output, or a link at a point; outputs come first, then steps, then links.</summary>
    public static DiagramHit? HitTest(Workflow workflow, Point point)
    {
        // The last drawn is on top, so it is found first.
        for (var i = workflow.Steps.Count - 1; i >= 0; i--)
        {
            var step = workflow.Steps[i];
            foreach (var output in StepTypes.Outputs(step))
            {
                if (Distance(Output(step, output), point) <= PortRadius + 4)
                    return new DiagramHit(step, output);
            }
        }

        for (var i = workflow.Steps.Count - 1; i >= 0; i--)
        {
            if (Box(workflow.Steps[i]).Inflate(2).Contains(point))
                return new DiagramHit(workflow.Steps[i]);
        }

        foreach (var link in workflow.Links)
        {
            if (Ends(workflow, link) is { } ends && NearCurve(Curve(ends.From, ends.To, ends.Direction), point, 6))
                return new DiagramHit(null, null, link);
        }

        return null;
    }

    /// <summary>Where a link starts and ends, and which way it leaves, or null when a step it joins is missing.</summary>
    public static (Point From, Point To, Vector Direction)? Ends(Workflow workflow, WorkflowLink link)
    {
        var from = workflow.Steps.FirstOrDefault(s => s.Id == link.From);
        var to = workflow.Steps.FirstOrDefault(s => s.Id == link.To);
        return from is null || to is null ? null : (Output(from, link.Output), Input(to), Direction(from, link.Output));
    }

    /// <summary>The smallest box around every step.</summary>
    public static Rect Bounds(Workflow workflow)
        => workflow.Steps.Count == 0
            ? new Rect(0, 0, Width, Header)
            : workflow.Steps.Select(Box).Aggregate((a, b) => a.Union(b));

    /// <summary>A free place for a new step: to the right of the one selected, or below everything.</summary>
    public static Point FreePlace(Workflow workflow, WorkflowStep? after)
    {
        var place = after is null
            ? new Point(Grid * 2, workflow.Steps.Count == 0 ? Grid * 2 : Bounds(workflow).Bottom + Grid * 2)
            : new Point(after.X + Width + Grid * 3, after.Y);
        while (workflow.Steps.Any(s => Box(s).Intersects(new Rect(place, new Size(Width, Header + SummaryHeight)))))
            place = new Point(place.X, place.Y + Header + SummaryHeight + Grid * 2);
        return Snap(place);
    }

    public static Point Snap(Point point) => new(Math.Round(point.X / Grid) * Grid, Math.Round(point.Y / Grid) * Grid);

    private static bool NearCurve((Point P0, Point P1, Point P2, Point P3) curve, Point point, double tolerance)
    {
        var previous = curve.P0;
        for (var i = 1; i <= 24; i++)
        {
            var next = At(curve, i / 24.0);
            if (DistanceToSegment(point, previous, next) <= tolerance)
                return true;
            previous = next;
        }

        return false;
    }

    public static Point At((Point P0, Point P1, Point P2, Point P3) c, double t)
    {
        var u = 1 - t;
        return new Point(
            u * u * u * c.P0.X + 3 * u * u * t * c.P1.X + 3 * u * t * t * c.P2.X + t * t * t * c.P3.X,
            u * u * u * c.P0.Y + 3 * u * u * t * c.P1.Y + 3 * u * t * t * c.P2.Y + t * t * t * c.P3.Y);
    }

    private static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static double DistanceToSegment(Point p, Point a, Point b)
    {
        var length = (b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y);
        if (length == 0)
            return Distance(p, a);
        var t = Math.Clamp(((p.X - a.X) * (b.X - a.X) + (p.Y - a.Y) * (b.Y - a.Y)) / length, 0, 1);
        return Distance(p, new Point(a.X + t * (b.X - a.X), a.Y + t * (b.Y - a.Y)));
    }

    private static int IndexOf(IReadOnlyList<string> list, string item)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] == item)
                return i;
        }

        return -1;
    }
}
