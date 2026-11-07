using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
using Joktan.Workflows.Model;

namespace Joktan.Workflows.Controls;

/// <summary>
/// Draws a workflow's steps and the links between them. On a diagram that can be edited, steps are dragged
/// about, an output is dragged to a step to link it, types are dropped from the palette, and the empty canvas
/// is dragged to pan; the wheel scrolls, and with Ctrl or Cmd it zooms.
/// </summary>
public sealed class WorkflowCanvas : Control
{
    public static readonly StyledProperty<WorkflowDiagram?> DiagramProperty =
        AvaloniaProperty.Register<WorkflowCanvas, WorkflowDiagram?>(nameof(Diagram));

    private const double MinZoom = 0.3;
    private const double MaxZoom = 2;
    private static readonly Cursor ResizeCursor = new(StandardCursorType.SizeNorthSouth);

    private Vector _offset = new(0, 0);
    private double _zoom = 1;
    private Drag _drag = Drag.None;
    private Point _pressedAt;
    private Point _pointer;
    private WorkflowStep? _dragged;
    private Point _draggedFrom;
    private string? _linkOutput;
    private int _lane = -1;
    private string? _hovered;
    private bool _fitPending = true;

    static WorkflowCanvas()
    {
        AffectsRender<WorkflowCanvas>(DiagramProperty);
        FocusableProperty.OverrideDefaultValue<WorkflowCanvas>(true);
        ClipToBoundsProperty.OverrideDefaultValue<WorkflowCanvas>(true);
    }

    public WorkflowCanvas()
    {
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    private enum Drag
    {
        None,
        Pan,
        Move,
        Link,
        Lane,
    }

    public WorkflowDiagram? Diagram
    {
        get => GetValue(DiagramProperty);
        set => SetValue(DiagramProperty, value);
    }

    public double Zoom => _zoom;

    /// <summary>Where a point of the control is on the canvas, before zooming.</summary>
    public Point ToCanvas(Point point) => new((point.X - _offset.X) / _zoom, (point.Y - _offset.Y) / _zoom);

    public Point FromCanvas(Point point) => new(point.X * _zoom + _offset.X, point.Y * _zoom + _offset.Y);

    /// <summary>Scales and moves the view so that every step shows.</summary>
    public void FitToView()
    {
        if (Diagram?.Workflow is not { } workflow || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            _fitPending = true;
            return;
        }

        _fitPending = false;
        var area = DiagramLayout.Bounds(workflow).Inflate(40);
        // Tiles stay big enough to read: a tall compact workflow fits across, and is scrolled down.
        var whole = Math.Min(Bounds.Width / area.Width, Bounds.Height / area.Height);
        _zoom = Math.Clamp(workflow.Look == WorkflowLook.Compact ? Math.Max(whole, Math.Min(Bounds.Width / area.Width, 0.75)) : whole, MinZoom, 1);
        // What is too tall to fit even at the smallest zoom shows from its top, where a run begins.
        _offset = new Vector(
            (Bounds.Width - area.Width * _zoom) / 2 - area.X * _zoom,
            Math.Max(0, (Bounds.Height - area.Height * _zoom) / 2) - area.Y * _zoom);
        InvalidateVisual();
    }

    public void ZoomBy(double factor) => ZoomAt(new Point(Bounds.Width / 2, Bounds.Height / 2), factor);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DiagramProperty)
        {
            if (change.OldValue is WorkflowDiagram old)
            {
                old.Changed -= InvalidateVisual;
                old.FitRequested -= FitToView;
            }

            if (change.NewValue is WorkflowDiagram diagram)
            {
                diagram.Changed += InvalidateVisual;
                diagram.FitRequested += FitToView;
            }

            FitToView();
        }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (_fitPending)
            FitToView();
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brush("SurfaceAltBrush"), new Rect(Bounds.Size));
        DrawGrid(context);
        if (Diagram?.Workflow is not { } workflow)
            return;

        using (context.PushTransform(Matrix.CreateScale(_zoom, _zoom) * Matrix.CreateTranslation(_offset.X, _offset.Y)))
        {
            var compact = workflow.Look == WorkflowLook.Compact;
            DrawLanes(context, workflow);
            foreach (var link in workflow.Links)
                DrawLink(context, workflow, link);
            if (_drag == Drag.Link && _dragged is not null && _linkOutput is not null)
            {
                var from = DiagramLayout.Output(_dragged, _linkOutput, workflow.Look);
                var direction = DiagramLayout.Direction(_dragged, _linkOutput, workflow.Look);
                var pen = new Pen(Brush("AccentBrush"), 2, new DashStyle([4, 3], 0));
                if (compact)
                    DrawRoute(context, TileLayout.Route(from, direction, ToCanvas(_pointer), TileLayout.Tile(_dragged)), pen);
                else
                    DrawCurve(context, DiagramLayout.Curve(from, ToCanvas(_pointer), direction), pen);
            }

            foreach (var step in workflow.Steps)
            {
                if (compact)
                    DrawTile(context, step);
                else
                    DrawStep(context, step);
            }

            // The labels of the lines go over the tiles, so none is hidden.
            if (compact)
            {
                foreach (var link in workflow.Links)
                    DrawLineLabel(context, workflow, link);
            }
        }
    }

    private void DrawGrid(DrawingContext context)
    {
        var step = DiagramLayout.Grid * _zoom;
        if (step < 8)
            return;
        var dot = Brush("BorderBrush");
        for (var x = _offset.X % step; x < Bounds.Width; x += step)
        {
            for (var y = _offset.Y % step; y < Bounds.Height; y += step)
                context.FillRectangle(dot, new Rect(x, y, 1.5, 1.5));
        }
    }

    // Swimlanes go right across the view, with their names in a strip down its left side that stays there as it pans.
    private void DrawLanes(DrawingContext context, Workflow workflow)
    {
        if (workflow.Lanes.Count == 0)
            return;
        var left = ToCanvas(new Point(0, 0)).X;
        var right = ToCanvas(new Point(Bounds.Width, 0)).X;
        var strip = DiagramLayout.LaneHeader / _zoom;
        var line = new Pen(Brush("BorderBrush"), 1 / _zoom);
        var tint = Brush("AccentBrush");
        var top = workflow.LanesTop;
        for (var i = 0; i < workflow.Lanes.Count; i++)
        {
            var lane = workflow.Lanes[i];
            var band = new Rect(left, top, right - left, lane.Height);
            if (i % 2 == 1)
            {
                using (context.PushOpacity(0.05))
                    context.FillRectangle(tint, band);
            }

            using (context.PushOpacity(0.12))
                context.FillRectangle(tint, new Rect(left, top, strip, lane.Height));
            context.DrawLine(line, new Point(left, top), new Point(right, top));
            context.DrawLine(line, new Point(left + strip, top), new Point(left + strip, top + lane.Height));

            // The name reads upwards, in the middle of the strip.
            var size = 12 / _zoom;
            var name = new FormattedText(lane.Name, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface(FontWeight.SemiBold), size, Brush("TextBrush"))
            {
                MaxTextWidth = Math.Max(1, lane.Height - 8 / _zoom),
                MaxLineCount = 1,
                Trimming = TextTrimming.CharacterEllipsis,
            };
            var middle = new Point(left + strip / 2, top + lane.Height / 2);
            using (context.PushTransform(Matrix.CreateTranslation(-name.Width / 2, -name.Height / 2) * Matrix.CreateRotation(-Math.PI / 2)
                                         * Matrix.CreateTranslation(middle.X, middle.Y)))
                context.DrawText(name, new Point(0, 0));
            top += lane.Height;
        }

        context.DrawLine(line, new Point(left, top), new Point(right, top));
    }

    private void DrawLink(DrawingContext context, Workflow workflow, WorkflowLink link)
    {
        if (workflow.Look == WorkflowLook.Compact)
        {
            DrawLine(context, workflow, link);
            return;
        }

        if (DiagramLayout.Ends(workflow, link) is not { } ends)
            return;
        var diagram = Diagram!;
        var taken = diagram.Taken.Contains(WorkflowDiagram.KeyOf(link));
        var selected = diagram.SelectedLink == link;
        var failing = link.Output is StepOutputs.Error or StepOutputs.Timeout;
        var brush = selected ? Brush("AccentBrush")
            : taken ? Brush("SuccessBrush")
            : failing ? Brush("DangerBrush")
            : Brush("TextMutedBrush");
        var pen = new Pen(brush, selected || taken ? 3 : 1.6, failing && !taken ? new DashStyle([5, 3], 0) : null);
        var curve = DiagramLayout.Curve(ends.From, ends.To, ends.Direction);
        DrawCurve(context, curve, pen);

        // An arrowhead at the step it leads to.
        var tip = ends.To;
        var arrow = new StreamGeometry();
        using (var g = arrow.Open())
        {
            g.BeginFigure(tip);
            g.LineTo(new Point(tip.X - 8, tip.Y - 4.5));
            g.LineTo(new Point(tip.X - 8, tip.Y + 4.5));
            g.EndFigure(true);
        }

        context.DrawGeometry(brush, null, arrow);

        // Halfway along, a link shows its label, or which way it goes when it is not a plain "next".
        var from = workflow.Steps.FirstOrDefault(s => s.Id == link.From);
        var label = from is not null && StepTypes.Label(from, link.Output) is { Length: > 0 } written ? written
            : link.Output != StepOutputs.Next ? StepTypes.OutputText(link.Output, from?.Type)
            : "";
        if (label.Length > 0)
        {
            var width = Math.Min(Measure(label, 10.5, FontWeight.SemiBold), 160);
            var middle = DiagramLayout.At(curve, 0.5);
            var pill = new Rect(middle.X - width / 2 - 6, middle.Y - 9, width + 12, 18);
            context.DrawRectangle(Brush("SurfaceBrush"), new Pen(brush, 1.2), pill, 9, 9);
            DrawText(context, label, new Point(pill.X + 6, pill.Y + 2), width + 2, 10.5, brush, FontWeight.SemiBold);
        }
    }

    private static void DrawCurve(DrawingContext context, (Point Start, Point Control1, Point Control2, Point End) curve, Pen pen)
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(curve.Start, false);
            g.CubicBezierTo(curve.Control1, curve.Control2, curve.End);
            g.EndFigure(false);
        }

        context.DrawGeometry(null, pen, geometry);
    }

    private void DrawStep(DrawingContext context, WorkflowStep step)
    {
        var diagram = Diagram!;
        var box = DiagramLayout.Box(step);
        var shape = DiagramLayout.Shape(step.Type);
        var outline = DiagramLayout.Outline(step);
        var accent = TypeBrush(step.Type);
        var (mark, times) = diagram.Marks.TryGetValue(step.Id, out var m) ? m : (StepMark.None, 0);
        var showsRun = diagram.Marks.Count > 0;

        // Steps a run never reached fade, so the path it took stands out.
        using var faded = showsRun && mark == StepMark.None ? context.PushOpacity(0.45) : default(DrawingContext.PushedState?);

        var pen = mark switch
        {
            StepMark.Done => new Pen(Brush("SuccessBrush"), 2.5),
            StepMark.Failed => new Pen(Brush("DangerBrush"), 3),
            StepMark.Running => new Pen(Brush("AccentBrush"), 3),
            StepMark.Waiting or StepMark.Input => new Pen(Brush("WarningBrush"), 3),
            _ => new Pen(accent, 1.5),
        };

        // A glow round the shape of the step selected.
        if (diagram.SelectedStep == step.Id)
        {
            using (context.PushOpacity(0.45))
                context.DrawGeometry(null, new Pen(Brush("AccentBrush"), 8, lineJoin: PenLineJoin.Round), outline);
        }

        context.DrawGeometry(Brush("SurfaceBrush"), null, outline);
        Point corner;
        if (shape == StepShape.Decision)
        {
            // A diamond is tinted all over, with its picture, name and what it compares in the middle.
            using (context.PushGeometryClip(outline))
            using (context.PushOpacity(0.3))
                context.FillRectangle(accent, box);
            var middle = box.Center;
            var nameWidth = Math.Min(Measure(step.Name, 12.5, FontWeight.SemiBold), 104);
            var left = middle.X - (nameWidth + 22) / 2;
            DrawIcon(context, step.Type, new Point(left, middle.Y - 24), Brush("TextBrush"));
            DrawText(context, step.Name, new Point(left + 22, middle.Y - 24), nameWidth + 2, 12.5, Brush("TextBrush"), FontWeight.SemiBold);
            var summary = diagram.Summary(step);
            var summaryWidth = Math.Min(Measure(summary, 11), 112);
            DrawText(context, summary, new Point(middle.X - summaryWidth / 2, middle.Y - 2), summaryWidth + 2, 11, Brush("TextMutedBrush"));
            if (step.Description.Trim() is { Length: > 0 } description)
            {
                var width = Math.Min(Measure(description, 10.5, FontWeight.Normal, FontStyle.Italic), 104);
                DrawText(context, description, new Point(middle.X - width / 2, middle.Y + 14), width + 2, 10.5, Brush("TextMutedBrush"), style: FontStyle.Italic);
            }
            corner = new Point(middle.X, box.Y + 22);
        }
        else
        {
            // The header, in the colour of the step's kind, cut to its shape.
            const double headerMiddle = DiagramLayout.Header / 2;
            var left = box.X + DiagramLayout.LeftEdge(step, headerMiddle);
            var right = box.X + DiagramLayout.RightEdge(step, headerMiddle);
            using (context.PushGeometryClip(outline))
                context.FillRectangle(accent, new Rect(box.X, box.Y, box.Width, DiagramLayout.Header));
            if (shape == StepShape.Subroutine)
            {
                var bar = new Pen(accent, 1.5);
                context.DrawLine(bar, new Point(box.X + 8, box.Y + DiagramLayout.Header), new Point(box.X + 8, box.Bottom));
                context.DrawLine(bar, new Point(box.Right - 8, box.Y + DiagramLayout.Header), new Point(box.Right - 8, box.Bottom));
            }

            var top = shape == StepShape.ManualInput ? box.Y + 8 : box.Y + 6;
            DrawIcon(context, step.Type, new Point(left + 8, top), Brushes.White);
            DrawText(context, step.Name, new Point(left + 30, top), right - left - 62, 12.5, Brushes.White, FontWeight.SemiBold);

            var summaryAt = DiagramLayout.Header + 11;
            var inset = shape == StepShape.Subroutine ? 18 : 10;
            var summaryLeft = box.X + DiagramLayout.LeftEdge(step, summaryAt) + inset;
            DrawText(context, diagram.Summary(step), new Point(summaryLeft, box.Y + DiagramLayout.Header + 4),
                box.X + DiagramLayout.RightEdge(step, summaryAt) - inset - summaryLeft, 11, Brush("TextMutedBrush"));

            // The description under the summary, in up to two lines, so what a step is for shows at a glance.
            if (step.Description.Trim() is { Length: > 0 } description)
            {
                var descriptionTop = DiagramLayout.Header + DiagramLayout.SummaryHeight;
                var middleOfIt = descriptionTop + DiagramLayout.DescriptionHeight(step) / 2;
                var descriptionLeft = box.X + DiagramLayout.LeftEdge(step, middleOfIt) + inset;
                DrawText(context, description, new Point(descriptionLeft, box.Y + descriptionTop), box.X + DiagramLayout.RightEdge(step, middleOfIt) - inset - descriptionLeft, 10.5,
                    Brush("TextMutedBrush"), style: FontStyle.Italic, lines: (int)DiagramLayout.DescriptionLines);
            }
            corner = new Point(right - 16, box.Y + headerMiddle);
        }

        context.DrawGeometry(null, pen, outline);

        // A tick, a cross, or how many times it ran, at the corner.
        if (mark is not StepMark.None)
            DrawBadge(context, corner, mark, times);
        else if (diagram.Problems.ContainsKey(step.Id))
        {
            context.DrawEllipse(Brush("WarningBrush"), new Pen(Brushes.White, 1.5), corner, 8, 8);
            DrawText(context, "!", new Point(corner.X - 2, corner.Y - 7.5), 10, 11, Brushes.White, FontWeight.Bold);
        }

        if (StepTypes.HasInput(step.Type))
            DrawPort(context, DiagramLayout.Input(step), Brush("TextMutedBrush"));
        foreach (var output in StepTypes.Outputs(step))
        {
            var port = DiagramLayout.Output(step, output);
            var brush = output is StepOutputs.Error or StepOutputs.Timeout ? Brush("DangerBrush")
                : output == StepOutputs.False ? Brush("WarningBrush")
                : Brush("SuccessBrush");
            var label = StepTypes.OutputText(output, step.Type);
            var width = Measure(label, 11);

            // Inside the box, before its port; outside a diamond, beside its point.
            var at = shape != StepShape.Decision ? new Point(port.X - 12 - width, port.Y - 7.5)
                : output == StepOutputs.True ? new Point(port.X + 4, port.Y - 22)
                : new Point(port.X + 10, port.Y - 4);
            DrawText(context, label, at, width + 2, 11, Brush("TextBrush"));
            DrawPort(context, port, brush);
        }
    }

    // ---- The compact look ----

    // A link as straight lines with rounded bends, an arrow into the top of the step it leads to, and its label in a chip.
    private void DrawLine(DrawingContext context, Workflow workflow, WorkflowLink link)
    {
        if (DiagramLayout.Route(workflow, link) is not { Count: >= 2 } route)
            return;
        var diagram = Diagram!;
        var taken = diagram.Taken.Contains(WorkflowDiagram.KeyOf(link));
        var selected = diagram.SelectedLink == link;
        var failing = TileLayout.IsFailure(link.Output);
        var brush = selected ? Brush("AccentBrush")
            : taken ? Brush("SuccessBrush")
            : failing ? Brush("DangerBrush")
            : Brush("TextMutedBrush");
        DrawRoute(context, route, new Pen(brush, selected || taken ? 2.6 : 1.4, failing && !taken ? new DashStyle([5, 3], 0) : null));

        var tip = route[^1];
        var along = tip - route[^2];
        var unit = along / Math.Max(0.001, Math.Sqrt(along.X * along.X + along.Y * along.Y));
        var side = new Vector(-unit.Y, unit.X);
        var arrow = new StreamGeometry();
        using (var g = arrow.Open())
        {
            g.BeginFigure(tip);
            g.LineTo(tip - unit * 8 + side * 4.5);
            g.LineTo(tip - unit * 8 - side * 4.5);
            g.EndFigure(true);
        }

        context.DrawGeometry(brush, null, arrow);
    }

    // A line's label, or which way it goes when it is not a plain "next", in a chip on its first long stretch.
    private void DrawLineLabel(DrawingContext context, Workflow workflow, WorkflowLink link)
    {
        if (DiagramLayout.Route(workflow, link) is not { Count: >= 2 } route)
            return;
        var diagram = Diagram!;
        var taken = diagram.Taken.Contains(WorkflowDiagram.KeyOf(link));
        var selected = diagram.SelectedLink == link;
        var failing = TileLayout.IsFailure(link.Output);
        var from = workflow.Steps.FirstOrDefault(s => s.Id == link.From);
        var label = from is not null && StepTypes.Label(from, link.Output) is { Length: > 0 } written ? written
            : link.Output != StepOutputs.Next ? StepTypes.OutputText(link.Output, from?.Type)
            : "";
        if (label.Length > 0)
        {
            var chip = selected ? Brush("AccentBrush") : taken ? Brush("SuccessBrush") : failing ? Brush("DangerBrush") : Brush("WarningBrush");
            var width = Math.Min(Measure(label, 10.5, FontWeight.SemiBold), 140);
            var middle = TileLayout.LabelAt(route);
            var box = new Rect(middle.X - width / 2 - 5, middle.Y - 9, width + 10, 18);
            context.DrawRectangle(Brush("SurfaceBrush"), new Pen(chip), box, 3, 3);
            DrawText(context, label, new Point(box.X + 5, box.Y + 2), width + 2, 10.5, chip, FontWeight.SemiBold);
        }
    }

    private static void DrawRoute(DrawingContext context, IReadOnlyList<Point> route, Pen pen)
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(route[0], false);
            for (var i = 1; i + 1 < route.Count; i++)
            {
                var (before, corner, after) = (route[i - 1], route[i], route[i + 1]);
                var r = Math.Min(TileLayout.Bend, Math.Min(Length(before, corner), Length(corner, after)) / 2);
                g.LineTo(corner + Unit(before - corner) * r);
                g.QuadraticBezierTo(corner, corner + Unit(after - corner) * r);
            }

            g.LineTo(route[^1]);
            g.EndFigure(false);
        }

        context.DrawGeometry(null, pen, geometry);

        static double Length(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

        static Vector Unit(Vector v) => v / Math.Max(0.001, Math.Sqrt(v.X * v.X + v.Y * v.Y));
    }

    // A tile with the step's picture in the colour of its kind, a circle for the start and the end, and its name underneath.
    private void DrawTile(DrawingContext context, WorkflowStep step)
    {
        var diagram = Diagram!;
        var tile = TileLayout.Tile(step);
        var accent = TypeBrush(step.Type);
        var (mark, times) = diagram.Marks.TryGetValue(step.Id, out var m) ? m : (StepMark.None, 0);
        var showsRun = diagram.Marks.Count > 0;
        var selected = diagram.SelectedStep == step.Id;
        using var faded = showsRun && mark == StepMark.None ? context.PushOpacity(0.45) : default(DrawingContext.PushedState?);

        Geometry outline = TileLayout.IsRound(step.Type)
            ? new EllipseGeometry(tile)
            : new RectangleGeometry(tile, 10, 10);
        if (selected)
        {
            using (context.PushOpacity(0.35))
                context.DrawGeometry(null, new Pen(Brush("AccentBrush"), 8, lineJoin: PenLineJoin.Round), outline);
        }

        var pen = mark switch
        {
            StepMark.Done => new Pen(Brush("SuccessBrush"), 2.5),
            StepMark.Failed => new Pen(Brush("DangerBrush"), 3),
            StepMark.Running => new Pen(Brush("AccentBrush"), 3),
            StepMark.Waiting or StepMark.Input => new Pen(Brush("WarningBrush"), 3),
            _ when selected => new Pen(Brush("AccentBrush"), 2),
            _ => new Pen(Brush("BorderBrush"), 1.2),
        };
        context.DrawGeometry(Brush("SurfaceBrush"), pen, outline);

        // A faint wash of the kind's colour behind the picture.
        using (context.PushGeometryClip(outline))
        using (context.PushOpacity(0.1))
            context.FillRectangle(accent, tile);
        DrawIcon(context, step.Type, new Point(tile.Center.X - 14, tile.Center.Y - 14), accent, 28);

        var name = new FormattedText(string.Join(" ", step.Name.Split((char[])['\n', '\r'], StringSplitOptions.RemoveEmptyEntries)), CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, Typeface(FontWeight.Normal), 11.5, Brush("TextBrush"))
        {
            MaxTextWidth = TileLayout.LabelWidth,
            MaxLineCount = 2,
            Trimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center,
        };
        context.DrawText(name, new Point(tile.Center.X - TileLayout.LabelWidth / 2, tile.Bottom + 4));

        var corner = new Point(tile.Right - 3, tile.Y + 3);
        if (mark is not StepMark.None)
            DrawBadge(context, corner, mark, times);
        else if (diagram.Problems.ContainsKey(step.Id))
        {
            context.DrawEllipse(Brush("WarningBrush"), new Pen(Brushes.White, 1.5), corner, 8, 8);
            DrawText(context, "!", new Point(corner.X - 2, corner.Y - 7.5), 10, 11, Brushes.White, FontWeight.Bold);
        }

        // Small ports to drag a link from, on the step selected or under the pointer; red for when something goes wrong.
        if (!diagram.IsEditable || (!selected && _hovered != step.Id && _dragged != step))
            return;
        foreach (var output in StepTypes.Outputs(step))
        {
            var fill = TileLayout.IsFailure(output) ? Brush("DangerBrush")
                : output == StepOutputs.False ? Brush("WarningBrush")
                : Brush("TextMutedBrush");
            context.DrawEllipse(fill, new Pen(Brush("SurfaceBrush"), 1.5), DiagramLayout.Output(step, output, WorkflowLook.Compact),
                TileLayout.PortRadius, TileLayout.PortRadius);
        }
    }

    // A tick, a cross, or how many times it ran, in a circle.
    private void DrawBadge(DrawingContext context, Point corner, StepMark mark, int times)
    {
        var fill = mark switch
        {
            StepMark.Done => Brush("SuccessBrush"),
            StepMark.Failed => Brush("DangerBrush"),
            StepMark.Running => Brush("AccentBrush"),
            _ => Brush("WarningBrush"),
        };
        context.DrawEllipse(fill, new Pen(Brushes.White, 1.5), corner, 8, 8);
        var symbol = mark switch
        {
            StepMark.Done => times > 1 ? times.ToString(CultureInfo.CurrentCulture) : "✓",
            StepMark.Failed => "✕",
            StepMark.Input => "?",
            StepMark.Waiting => "…",
            _ => "▶",
        };
        DrawText(context, symbol, new Point(corner.X - 4, corner.Y - 7.5), 16, 10.5, Brushes.White, FontWeight.Bold);
    }

    // A step's picture, 16 across unless another size is given, with its top left at a point.
    private static void DrawIcon(DrawingContext context, StepType type, Point at, IBrush brush, double size = 16)
    {
        using (context.PushTransform(Matrix.CreateScale(size / 24.0, size / 24.0) * Matrix.CreateTranslation(at.X, at.Y)))
            context.DrawGeometry(brush, null, StepVisuals.Icon(type));
    }

    private void DrawPort(DrawingContext context, Point at, IBrush fill)
        => context.DrawEllipse(fill, new Pen(Brush("SurfaceBrush"), 2), at, DiagramLayout.PortRadius, DiagramLayout.PortRadius);

    private void DrawText(DrawingContext context, string text, Point at, double maxWidth, double size, IBrush brush, FontWeight weight = FontWeight.Normal,
        FontStyle style = FontStyle.Normal, int lines = 1)
    {
        if (text.Length == 0 || maxWidth <= 0)
            return;

        // More than one line wraps; one line keeps to the first line of the text.
        var shown = lines > 1 ? string.Join(" ", text.Split((char[])['\n', '\r'], StringSplitOptions.RemoveEmptyEntries)) : text.Split('\n')[0];
        var formatted = new FormattedText(shown, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface(weight, style), size, brush)
        {
            MaxTextWidth = maxWidth,
            MaxLineCount = lines,
            Trimming = TextTrimming.CharacterEllipsis,
        };
        context.DrawText(formatted, at);
    }

    private double Measure(string text, double size, FontWeight weight = FontWeight.Normal, FontStyle style = FontStyle.Normal)
        => new FormattedText(text.Split('\n')[0], CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface(weight, style), size, Brushes.Black).Width;

    private Typeface Typeface(FontWeight weight, FontStyle style = FontStyle.Normal) => new(GetValue(TextElement.FontFamilyProperty), style, weight);

    private IBrush TypeBrush(StepType type) => Brush(StepVisuals.BrushKey(type));

    private IBrush Brush(string key)
        => this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush ? brush : Brushes.Gray;

    // ---- Pointer ----

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        if (Diagram is not { Workflow: { } workflow } diagram)
            return;

        _pressedAt = _pointer = e.GetPosition(this);
        var properties = e.GetCurrentPoint(this).Properties;
        var hit = DiagramLayout.HitTest(workflow, ToCanvas(_pointer));

        if (hit is null && diagram.IsEditable && !properties.IsMiddleButtonPressed
            && DiagramLayout.LaneEdgeAt(workflow, ToCanvas(_pointer).Y, 5 / _zoom) is >= 0 and var lane)
        {
            _drag = Drag.Lane;
            _lane = lane;
        }
        else if (properties.IsMiddleButtonPressed || hit is null)
        {
            if (hit is null && (diagram.SelectedStep is not null || diagram.SelectedLink is not null))
                Select(null, null);
            _drag = Drag.Pan;
        }
        else if (hit.Output is { } output && diagram.IsEditable)
        {
            _drag = Drag.Link;
            _dragged = hit.Step;
            _linkOutput = output;
        }
        else if (hit.Step is { } step)
        {
            Select(step.Id, null);
            if (diagram.IsEditable)
            {
                _drag = Drag.Move;
                _dragged = step;
                _draggedFrom = new Point(step.X, step.Y);
            }
        }
        else if (hit.Link is { } link)
        {
            Select(null, link);
        }

        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var position = e.GetPosition(this);
        var delta = position - _pointer;
        _pointer = position;

        switch (_drag)
        {
            case Drag.Pan:
                _offset += delta;
                InvalidateVisual();
                break;
            case Drag.Move when _dragged is not null:
                var moved = DiagramLayout.Snap(_draggedFrom + (position - _pressedAt) / _zoom);
                (_dragged.X, _dragged.Y) = (moved.X, moved.Y);
                InvalidateVisual();
                break;
            case Drag.Link:
                InvalidateVisual();
                break;
            case Drag.Lane when Diagram?.Workflow is { } lanes && _lane < lanes.Lanes.Count:
                DiagramLayout.ResizeLane(lanes, _lane, ToCanvas(position).Y - DiagramLayout.LaneTop(lanes, _lane));
                InvalidateVisual();
                break;
            default:
                ShowTip(position);
                break;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var diagram = Diagram;
        var workflow = diagram?.Workflow;
        if (diagram is not null && workflow is not null)
        {
            if (_drag == Drag.Move && _dragged is not null && (_dragged.X != _draggedFrom.X || _dragged.Y != _draggedFrom.Y))
                diagram.Moved?.Invoke(_dragged.Id, new Point(_dragged.X, _dragged.Y));
            else if (_drag == Drag.Link && _dragged is not null && _linkOutput is not null
                     && DiagramLayout.HitTest(workflow, ToCanvas(e.GetPosition(this))) is { Step: { } target }
                     && target != _dragged && StepTypes.HasInput(target.Type))
                diagram.Linked?.Invoke(_dragged.Id, _linkOutput, target.Id);
            else if (_drag == Drag.Lane)
                diagram.LanesChanged?.Invoke();
        }

        _drag = Drag.None;
        _lane = -1;
        _dragged = null;
        _linkOutput = null;
        e.Pointer.Capture(null);
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta))
            ZoomAt(e.GetPosition(this), e.Delta.Y > 0 ? 1.1 : 1 / 1.1);
        else
        {
            _offset += new Vector(e.Delta.X, e.Delta.Y) * 40;
            InvalidateVisual();
        }

        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Diagram is { IsEditable: true } diagram && e.Key is Key.Delete or Key.Back
            && (diagram.SelectedStep is not null || diagram.SelectedLink is not null))
        {
            diagram.DeleteRequested?.Invoke();
            e.Handled = true;
        }
    }

    private void ZoomAt(Point at, double factor)
    {
        var zoom = Math.Clamp(_zoom * factor, MinZoom, MaxZoom);
        var anchor = ToCanvas(at);
        _zoom = zoom;
        _offset = new Vector(at.X - anchor.X * zoom, at.Y - anchor.Y * zoom);
        InvalidateVisual();
    }

    private void Select(string? step, WorkflowLink? link)
    {
        if (Diagram is not { } diagram)
            return;
        diagram.SelectedStep = step;
        diagram.SelectedLink = link;
        diagram.SelectionChanged?.Invoke();
        InvalidateVisual();
    }

    // What is wrong with a step shows as a tip when the pointer is over it.
    private void ShowTip(Point position)
    {
        if (Diagram is not { Workflow: { } workflow } diagram)
            return;
        var hit = DiagramLayout.HitTest(workflow, ToCanvas(position));
        if (hit?.Step?.Id != _hovered && workflow.Look == WorkflowLook.Compact)
        {
            _hovered = hit?.Step?.Id;
            InvalidateVisual();
        }

        var onEdge = hit is null && diagram.IsEditable && DiagramLayout.LaneEdgeAt(workflow, ToCanvas(position).Y, 5 / _zoom) >= 0;
        Cursor = onEdge ? ResizeCursor : Cursor.Default;
        var tip = hit is { Step: { } port, Output: { } output } && workflow.Look == WorkflowLook.Compact
                ? StepTypes.Label(port, output) is { Length: > 0 } label ? label : StepTypes.OutputText(output, port.Type)
            : hit?.Step is { } tile && workflow.Look == WorkflowLook.Compact ? TileTip(diagram, tile)
            : hit?.Step is { } step && diagram.Problems.TryGetValue(step.Id, out var problem) ? problem
            : hit?.Step is { Description.Length: > 0 } described ? described.Description
            : null;
        if (!Equals(ToolTip.GetTip(this), tip))
            ToolTip.SetTip(this, tip);
    }

    // A tile shows only its name, so its tip says the rest: its kind, what it does, its description and what is wrong with it.
    public static string TileTip(WorkflowDiagram diagram, WorkflowStep step)
    {
        var lines = new List<string> { step.Name.Trim().Length > 0 ? $"{step.Name.Trim()} · {step.Type.Text()}" : step.Type.Text() };
        if (diagram.Summary(step).Trim() is { Length: > 0 } summary && summary != step.Type.Text())
            lines.Add(summary);
        if (step.Description.Trim() is { Length: > 0 } description)
            lines.Add(description);
        if (diagram.Problems.TryGetValue(step.Id, out var problem))
            lines.Add("⚠ " + problem);
        return string.Join("\n\n", lines);
    }

    // ---- Dropping a step from the palette ----

    /// <summary>Whether a point of the control, such as where a step from the palette is let go, is on the canvas.</summary>
    public bool IsOver(Point point) => new Rect(Bounds.Size).Contains(point);

    /// <summary>Adds a step of a type centred where it was let go, given as a point of this control.</summary>
    public void DropStep(StepType type, Point point)
    {
        if (Diagram is not { IsEditable: true } diagram)
            return;
        var at = ToCanvas(point);
        var middle = diagram.Workflow?.Look == WorkflowLook.Compact ? new Vector(TileLayout.Size / 2, TileLayout.Size / 2)
            : new Vector(DiagramLayout.Width / 2, DiagramLayout.Header / 2);
        diagram.Dropped?.Invoke(type, DiagramLayout.Snap(at - middle));
    }
}
