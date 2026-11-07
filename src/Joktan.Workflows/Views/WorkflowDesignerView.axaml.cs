using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls.Primitives;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Joktan.Workflows.Model;
using Joktan.Workflows.ViewModels;

namespace Joktan.Workflows.Views;

/// <summary>
/// A step is dragged from the palette within the app, a card following the pointer, and dropped where it is let go
/// on the canvas; a click adds it after the step selected. The system's drag and drop is not used, as on macOS it
/// refuses a drag that carries nothing other apps can read. The side panels are resized with their splitters,
/// collapsed into their tabs, or floated in windows of their own, as in Visual Studio.
/// </summary>
public partial class WorkflowDesignerView : UserControl
{
    // How far the pointer moves on a palette step before it is a drag rather than a click.
    private const double DragDistance = 6;

    // One editor window per step, brought to the front when asked for again.
    private readonly Dictionary<WorkflowStep, CodeEditorWindow> _editors = [];
    private readonly Dictionary<WorkflowStep, DatabaseStepWindow> _databaseEditors = [];

    private WorkflowDesignerViewModel? _vm;
    private Window? _leftWindow;
    private Window? _rightWindow;
    private PaletteItem? _pressed;
    private bool _scrollingToSection;

    // What the properties column takes besides the properties: the strip of sections.
    private const double StripWidth = 52;
    private PixelPoint _pressedAt;
    private bool _dragging;

    public WorkflowDesignerView()
    {
        InitializeComponent();
        ZoomInButton.Click += (_, _) => Canvas.ZoomBy(1.2);
        ZoomOutButton.Click += (_, _) => Canvas.ZoomBy(1 / 1.2);

        // Tunnelling, so the palette sees the pointer before its buttons do; the buttons still get it for clicks.
        // The toolbox and the favourites strip both drag steps onto the canvas.
        foreach (var source in new Control[] { Palette, Favourites })
        {
            source.AddHandler(PointerPressedEvent, OnPalettePressed, RoutingStrategies.Tunnel);
            source.AddHandler(PointerMovedEvent, OnPaletteMoved, RoutingStrategies.Tunnel);
            source.AddHandler(PointerReleasedEvent, OnPaletteReleased, RoutingStrategies.Tunnel);
            source.AddHandler(PointerCaptureLostEvent, (_, _) => EndDrag(), RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
        }

        // A case is renamed when it is finished, on Enter or on leaving its box, so its link is not lost while typing.
        AddHandler(LostFocusEvent, (_, e) => ((e.Source as Control)?.DataContext as CaseViewModel)?.Apply(), RoutingStrategies.Bubble);
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Enter)
                return;
            if ((e.Source as Control)?.DataContext is CaseViewModel @case)
                @case.Apply();
            else if (e.Source is TextBox { Name: "NewCaseBox", DataContext: StepViewModel step })
                step.AddCaseCommand.Execute(null);
        }, RoutingStrategies.Tunnel);

        // A variable chosen above the code goes in at the caret. The box is in a template, so it is found by its name.
        AddHandler(SelectingItemsControl.SelectionChangedEvent, (_, e) =>
        {
            if (e.Source is ComboBox { Name: "InsertVariableBox" } box
                && box.Parent is Panel panel && panel.Children.OfType<Controls.CodeEditor>().FirstOrDefault() is { } editor)
                CodeEditorWindow.InsertVariable(box, editor);
        });

        // The section scrolled to is lit on the strip.
        RightContent.ScrollChanged += (_, _) =>
        {
            if (!_scrollingToSection)
                LightSection();
        };

        // A width dragged to is kept for next time.
        LeftSplitter.DragCompleted += (_, _) =>
        {
            if (_vm is { IsLeftDocked: true })
                _vm.LeftWidth = Panels.ColumnDefinitions[0].ActualWidth;
        };
        RightSplitter.DragCompleted += (_, _) =>
        {
            if (_vm is { IsRightDocked: true })
                _vm.RightWidth = Panels.ColumnDefinitions[4].ActualWidth - Extras();
        };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnLayoutChanged;
            _vm.FloatingRequested -= BringForward;
            _vm.SectionRequested -= ScrollToSection;
        }

        _vm = DataContext as WorkflowDesignerViewModel;
        if (_vm is null)
            return;
        _vm.OpenEditor = ShowEditor;
        _vm.OpenDatabaseEditor = ShowDatabaseEditor;
        _vm.PropertyChanged += OnLayoutChanged;
        _vm.FloatingRequested += BringForward;
        _vm.SectionRequested += ScrollToSection;
        ApplyLayout();
    }

    // Floating windows go back into the page when it is left, so nothing is left behind that belongs to it.
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _leftWindow?.Close();
        _rightWindow?.Close();
    }

    private void OnLayoutChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(WorkflowDesignerViewModel.IsLeftFloating) when _vm is { IsLeftFloating: true } && _leftWindow is null:
                _leftWindow = Float(left: true);
                break;
            case nameof(WorkflowDesignerViewModel.IsRightFloating) when _vm is { IsRightFloating: true } && _rightWindow is null:
                _rightWindow = Float(left: false);
                break;
        }

        if (e.PropertyName is nameof(WorkflowDesignerViewModel.IsLeftCollapsed) or nameof(WorkflowDesignerViewModel.IsRightCollapsed)
            or nameof(WorkflowDesignerViewModel.IsLeftFloating) or nameof(WorkflowDesignerViewModel.IsRightFloating)
            or nameof(WorkflowDesignerViewModel.LeftWidth) or nameof(WorkflowDesignerViewModel.RightWidth))
            ApplyLayout();
    }

    // A panel in its column takes its width; collapsed or floating, only its tabs show.
    private void ApplyLayout()
    {
        if (_vm is null)
            return;
        Panels.ColumnDefinitions[0].Width = new GridLength(_vm.IsLeftDocked ? _vm.LeftWidth : WorkflowDesignerViewModel.CollapsedWidth);
        Panels.ColumnDefinitions[4].Width = new GridLength(_vm.IsRightDocked ? _vm.RightWidth + Extras() : WorkflowDesignerViewModel.CollapsedWidth);
    }

    private static double Extras() => StripWidth;

    // ---- The sections of the properties ----

    // The cards of the sections, found by name as some are in a template.
    private Control? Section(string key)
        => RightContent.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.Name == key && c.IsEffectivelyVisible);

    private void ScrollToSection(string key)
    {
        if (Section(key) is not { } card || RightContent.Content is not Visual content || card.TranslatePoint(default, content) is not { } at)
            return;
        _scrollingToSection = true;
        RightContent.Offset = new Vector(RightContent.Offset.X, Math.Min(at.Y, Math.Max(0, RightContent.Extent.Height - RightContent.Viewport.Height)));
        Avalonia.Threading.Dispatcher.UIThread.Post(() => _scrollingToSection = false, Avalonia.Threading.DispatcherPriority.Background);
    }

    private void LightSection()
    {
        if (_vm is null || RightContent.Content is not Visual content)
            return;
        var top = RightContent.Offset.Y + 24;
        var current = _vm.Sections.LastOrDefault(s => Section(s.Key)?.TranslatePoint(default, content) is { } at && at.Y <= top) ?? _vm.Sections.FirstOrDefault();
        if (current is not null && !current.IsCurrent)
            _vm.ShowSection(current.Key);
    }



    // Moves a panel's content into a window of its own; closing the window puts it back.
    private Window Float(bool left)
    {
        var host = left ? LeftHost : RightHost;
        var content = host.Child!;
        host.Child = null;
        var frame = new Border { Padding = new Thickness(16), Child = content };
        var window = new Window
        {
            Width = (left ? _vm!.LeftWidth : _vm!.RightWidth + Extras()) + 32,
            Height = Math.Max(500, Bounds.Height),
            MinWidth = 200,
            Content = frame,
            DataContext = _vm,
        };
        window.Bind(BackgroundProperty, window.GetResourceObservable("AppBackgroundBrush"));
        if (left)
            window.Bind(Window.TitleProperty, new Binding(nameof(WorkflowDesignerViewModel.LeftPanelTitle)));
        else
            window.Title = Joktan.Workflows.Resources.Strings.Designer_Properties;
        window.Closed += (_, _) =>
        {
            frame.Child = null;
            host.Child = content;
            if (left)
            {
                _leftWindow = null;
                if (_vm is not null)
                    _vm.IsLeftFloating = false;
            }
            else
            {
                _rightWindow = null;
                if (_vm is not null)
                    _vm.IsRightFloating = false;
            }
        };
        if (TopLevel.GetTopLevel(this) is Window owner)
            window.Show(owner);
        else
            window.Show();
        return window;
    }

    private void BringForward(bool left) => (left ? _leftWindow : _rightWindow)?.Activate();

    private void ShowEditor(StepViewModel step)
    {
        if (_editors.TryGetValue(step.Step, out var open))
        {
            open.Activate();
            return;
        }

        var window = new CodeEditorWindow { DataContext = step };
        _editors[step.Step] = window;
        window.Closed += (_, _) => _editors.Remove(step.Step);
        if (TopLevel.GetTopLevel(this) is Window owner)
            window.Show(owner);
        else
            window.Show();
    }

    private void ShowDatabaseEditor(StepViewModel step)
    {
        if (_databaseEditors.TryGetValue(step.Step, out var open))
        {
            open.Activate();
            return;
        }

        var window = new DatabaseStepWindow { DataContext = new DatabaseStepViewModel(step) };
        _databaseEditors[step.Step] = window;
        window.Closed += (_, _) => _databaseEditors.Remove(step.Step);
        if (TopLevel.GetTopLevel(this) is Window owner)
            window.Show(owner);
        else
            window.Show();
    }

    // ---- Dragging a step from the palette, which may be in a window of its own ----

    // Where the pointer is on the screen, so that a drag can go from a floating palette to the canvas.
    private static PixelPoint? OnScreen(PointerEventArgs e)
        => (e.Source as Visual) is { } source && TopLevel.GetTopLevel(source) is { } top ? top.PointToScreen(e.GetPosition(top)) : null;

    private void OnPalettePressed(object? sender, PointerPressedEventArgs e)
    {
        EndDrag();
        if (e.GetCurrentPoint(null).Properties.IsLeftButtonPressed
            && (e.Source as Visual)?.FindAncestorOfType<Button>(includeSelf: true)?.DataContext is PaletteItem item
            && OnScreen(e) is { } at)
        {
            _pressed = item;
            _pressedAt = at;
        }
    }

    private void OnPaletteMoved(object? sender, PointerEventArgs e)
    {
        if (_pressed is null || OnScreen(e) is not { } screen)
            return;
        if (!_dragging)
        {
            var (dx, dy) = (screen.X - _pressedAt.X, screen.Y - _pressedAt.Y);
            if (dx * dx + dy * dy < DragDistance * DragDistance)
                return;
            _dragging = true;
            DragGhost.DataContext = _pressed;
            DragGhost.IsVisible = true;
        }

        var position = this.PointToClient(screen);
        Avalonia.Controls.Canvas.SetLeft(DragGhost, position.X + 12);
        Avalonia.Controls.Canvas.SetTop(DragGhost, position.Y + 8);
        Cursor = new Cursor(Canvas.IsOver(Canvas.PointToClient(screen)) ? StandardCursorType.DragCopy : StandardCursorType.No);
    }

    // The button still sees the release, but as the pointer has left it, it does not count as a click.
    private void OnPaletteReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragging && _pressed is { } item && Canvas.IsVisible && OnScreen(e) is { } screen
            && Canvas.PointToClient(screen) is var point && Canvas.IsOver(point))
            Canvas.DropStep(item.Type, point);
        EndDrag();
    }

    private void EndDrag()
    {
        _pressed = null;
        _dragging = false;
        DragGhost.IsVisible = false;
        Cursor = Cursor.Default;
    }
}
