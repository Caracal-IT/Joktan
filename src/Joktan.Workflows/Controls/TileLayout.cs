using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Joktan.Workflows.Model;

namespace Joktan.Workflows.Controls;

/// <summary>
/// Where things are in the compact look: a small tile per step with its name underneath, running from top to
/// bottom. Links come in at the top of a tile and leave from its bottom; failures leave from the right, and an
/// If sends true out of its left and false out of its right. Lines run straight, with rounded bends.
/// </summary>
public static class TileLayout
{
    /// <summary>The width and height of a tile.</summary>
    public const double Size = 56;

    /// <summary>The room under a tile for its name, in up to two lines.</summary>
    public const double LabelRoom = 36;

    /// <summary>How wide a name under a tile can be.</summary>
    public const double LabelWidth = 140;

    public const double PortRadius = 4.5;

    /// <summary>Between the tops of rows, and the lefts of tiles side by side, when steps are arranged.</summary>
    public const double RowGap = 140;

    public const double ColumnGap = 160;

    /// <summary>The rounding of a line's bends.</summary>
    public const double Bend = 8;

    // How far a line goes straight out of a port, and straight into a tile, before it turns.
    private const double Stub = 18;
    private const double PortGap = 16;

    // How far a line that goes back up keeps clear of the tiles it passes.
    private const double Clearance = 36;

    public static Rect Tile(WorkflowStep step) => new(step.X, step.Y, Size, Size);

    /// <summary>The tile and the room for the name under it.</summary>
    public static Rect Box(WorkflowStep step) => new(step.X, step.Y, Size, Size + LabelRoom);

    /// <summary>Everything drawn for a step, with its name as wide as it may be.</summary>
    public static Rect Frame(WorkflowStep step) => new(step.X + Size / 2 - LabelWidth / 2, step.Y, LabelWidth, Size + LabelRoom);

    /// <summary>Whether a step is drawn as a circle: where a run begins or ends.</summary>
    public static bool IsRound(StepType type) => type is StepType.Start or StepType.End;

    /// <summary>Whether an output is a way out for when something goes wrong.</summary>
    public static bool IsFailure(string output) => output is StepOutputs.Error or StepOutputs.Timeout;

    /// <summary>Where links come in: the middle of the top.</summary>
    public static Point Input(WorkflowStep step) => new(step.X + Size / 2, step.Y);

    /// <summary>Where an output leaves the tile.</summary>
    public static Point Output(WorkflowStep step, string output)
    {
        var (side, index, count) = Place(step, output);
        return side switch
        {
            Side.Left => new Point(step.X, step.Y + Along(index, count)),
            Side.Right => new Point(step.X + Size, step.Y + Along(index, count)),
            _ => new Point(step.X + Along(index, count), step.Y + Size),
        };
    }

    /// <summary>Where the line of an output starts: under the name for the outputs at the bottom, as in a flowchart; at the port for the others.</summary>
    public static Point LineStart(WorkflowStep step, string output)
    {
        var port = Output(step, output);
        return Place(step, output).Side == Side.Bottom ? new Point(port.X, step.Y + Size + LabelRoom) : port;
    }

    /// <summary>Which way a link leaves an output: down from the bottom, or out of the side it is on.</summary>
    public static Vector Direction(WorkflowStep step, string output) => Place(step, output).Side switch
    {
        Side.Left => new Vector(-1, 0),
        Side.Right => new Vector(1, 0),
        _ => new Vector(0, 1),
    };

    private enum Side
    {
        Bottom,
        Left,
        Right,
    }

    // The side an output is on, and its place among the outputs on that side.
    private static (Side Side, int Index, int Count) Place(WorkflowStep step, string output)
    {
        var outputs = StepTypes.Outputs(step);
        Side SideOf(string o) => step.Type == StepType.Condition
            ? o == StepOutputs.True ? Side.Left : Side.Right
            : IsFailure(o) ? Side.Right : Side.Bottom;

        var side = SideOf(output);
        var same = outputs.Where(o => SideOf(o) == side).ToList();
        return (side, Math.Max(0, same.IndexOf(output)), Math.Max(1, same.Count));
    }

    // Where along a side the port of an output is: spread evenly when they fit, side by side round the middle when not.
    private static double Along(int index, int count)
        => count * PortGap <= Size ? Size * (index + 1) / (count + 1) : Size / 2 + (index - (count - 1) / 2.0) * PortGap;

    /// <summary>
    /// The corners of the line from an output, leaving in a direction, to the top of a step: straight lines that turn
    /// at right angles, going round the side when the step is above.
    /// </summary>
    public static IReadOnlyList<Point> Route(Point from, Vector direction, Point to, Rect? fromTile = null, Rect? toTile = null)
    {
        var above = new Point(to.X, to.Y - Stub);
        List<Point> points;
        if (direction.Y > 0)
        {
            var below = new Point(from.X, from.Y + Stub);
            if (to.Y > from.Y)
            {
                var middle = (from.Y + to.Y) / 2;
                points = [from, new Point(from.X, middle), new Point(to.X, middle), to];
            }
            else
            {
                // Up through the gap between the tiles when they are side by side; round the right of both when not.
                var side = fromTile is { } f && toTile is { } t && t.Left > f.Right + Stub ? (f.Right + t.Left) / 2
                    : fromTile is { } f2 && toTile is { } t2 && t2.Right < f2.Left - Stub ? (t2.Right + f2.Left) / 2
                    : Math.Max(fromTile?.Right ?? from.X, toTile?.Right ?? to.X) + Clearance;
                points = [from, below, new Point(side, below.Y), new Point(side, above.Y), above, to];
            }
        }
        else
        {
            var outward = (to.X - from.X) * direction.X;
            if (above.Y >= from.Y && outward > Stub)
                points = [from, new Point(to.X, from.Y), to];
            else if (above.Y >= from.Y)
            {
                var beside = from + direction * Stub;
                points = [from, beside, new Point(beside.X, above.Y), above, to];
            }
            else
            {
                var side = direction.X > 0
                    ? Math.Max(fromTile?.Right ?? from.X, toTile?.Right ?? to.X) + Clearance
                    : Math.Min(fromTile?.Left ?? from.X, toTile?.Left ?? to.X) - Clearance;
                points = [from, new Point(side, from.Y), new Point(side, above.Y), above, to];
            }
        }

        return Simplify(points);
    }

    // Drops corners that are not corners: the same point twice, or one in a straight line between its neighbours.
    private static List<Point> Simplify(List<Point> points)
    {
        var kept = new List<Point>();
        foreach (var point in points)
        {
            if (kept.Count > 0 && Near(kept[^1], point))
                continue;
            if (kept.Count >= 2 && Collinear(kept[^2], kept[^1], point))
                kept[^1] = point;
            else
                kept.Add(point);
        }

        return kept;
    }

    private static bool Near(Point a, Point b) => Math.Abs(a.X - b.X) < 0.5 && Math.Abs(a.Y - b.Y) < 0.5;

    private static bool Collinear(Point a, Point b, Point c)
        => (Math.Abs(a.X - b.X) < 0.5 && Math.Abs(b.X - c.X) < 0.5) || (Math.Abs(a.Y - b.Y) < 0.5 && Math.Abs(b.Y - c.Y) < 0.5);

    /// <summary>Where a line's label goes: halfway along its first stretch long enough to hold one, or its longest.</summary>
    public static Point LabelAt(IReadOnlyList<Point> route)
    {
        var longest = 0;
        for (var i = 0; i + 1 < route.Count; i++)
        {
            if (Length(route[i], route[i + 1]) >= 48)
                return Middle(route[i], route[i + 1]);
            if (Length(route[i], route[i + 1]) > Length(route[longest], route[longest + 1]))
                longest = i;
        }

        return route.Count < 2 ? route.FirstOrDefault() : Middle(route[longest], route[longest + 1]);
    }

    public static bool NearRoute(IReadOnlyList<Point> route, Point point, double tolerance)
    {
        for (var i = 0; i + 1 < route.Count; i++)
        {
            if (DistanceToSegment(point, route[i], route[i + 1]) <= tolerance)
                return true;
        }

        return false;
    }

    /// <summary>A free place for a new step: under the one selected, or under everything.</summary>
    public static Point FreePlace(Workflow workflow, WorkflowStep? after)
    {
        var place = after is not null ? new Point(after.X, after.Y + RowGap)
            : workflow.Steps.Count == 0 ? new Point(DiagramLayout.Grid * 6, DiagramLayout.Grid * 2)
            : new Point(workflow.Steps.Min(s => s.X), workflow.Steps.Max(s => Box(s).Bottom) + DiagramLayout.Grid * 2);
        while (workflow.Steps.Any(s => Frame(s).Intersects(new Rect(place.X + Size / 2 - LabelWidth / 2, place.Y, LabelWidth, Size + LabelRoom))))
            place = new Point(place.X + ColumnGap, place.Y);
        return DiagramLayout.Snap(place);
    }

    private static double Length(Point a, Point b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

    private static Point Middle(Point a, Point b) => new((a.X + b.X) / 2, (a.Y + b.Y) / 2);

    private static double DistanceToSegment(Point p, Point a, Point b)
    {
        var length = (b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y);
        var t = length == 0 ? 0 : Math.Clamp(((p.X - a.X) * (b.X - a.X) + (p.Y - a.Y) * (b.Y - a.Y)) / length, 0, 1);
        var (x, y) = (a.X + t * (b.X - a.X), a.Y + t * (b.Y - a.Y));
        return Math.Sqrt((p.X - x) * (p.X - x) + (p.Y - y) * (p.Y - y));
    }
}
