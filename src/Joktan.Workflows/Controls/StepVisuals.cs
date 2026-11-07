using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Joktan.Core;
using Joktan.Workflows.Model;

namespace Joktan.Workflows.Controls;

/// <summary>The picture and colour of each kind of step, shared by the palette and the canvas.</summary>
public static class StepVisuals
{
    private static readonly Geometry Play = Geometry.Parse("M8,5V19L19,12Z");
    private static readonly Geometry Flag = Geometry.Parse("M5,3H7V21H5Z M8,4H19L16,8L19,12H8Z");
    private static readonly Geometry EqualsSign = Geometry.Parse("M4,7H20V10H4Z M4,14H20V17H4Z");
    private static readonly Geometry Diamond = Geometry.Parse("M12,2L22,12L12,22L2,12Z M12,6L6,12L12,18L18,12Z");
    private static readonly Geometry Exchange = Geometry.Parse("M4,7H16V4L21,8.5L16,13V10H4Z M20,17H8V20L3,15.5L8,11V14H20Z");
    private static readonly Geometry Send = Geometry.Parse("M2,21L23,12L2,3V10L17,12L2,14V21Z");
    private static readonly Geometry Receive = Geometry.Parse("M5,20H19V18H5Z M19,9H15V3H9V9H5L12,16Z");
    private static readonly Geometry Person = Geometry.Parse("M12,4A4,4 0 0,1 16,8A4,4 0 0,1 12,12A4,4 0 0,1 8,8A4,4 0 0,1 12,4M12,14C16.42,14 20,15.79 20,18V20H4V18C4,15.79 7.58,14 12,14Z");
    private static readonly Geometry Cycle = Geometry.Parse("M12,4V1L8,5L12,9V6A6,6 0 0,1 18,12C18,13 17.75,13.97 17.3,14.8L18.76,16.26C19.54,15.03 20,13.57 20,12A8,8 0 0,0 12,4M12,18A6,6 0 0,1 6,12C6,11 6.25,10.03 6.7,9.2L5.24,7.74C4.46,8.97 4,10.43 4,12A8,8 0 0,0 12,20V23L16,19L12,15V18Z");
    private static readonly Geometry Repeat = Geometry.Parse("M17,17H7V14L3,18L7,22V19H19V13H17M7,7H17V10L21,6L17,2V5H5V11H7V7Z");
    private static readonly Geometry Fork = Geometry.Parse("M3,11H9L14,5H18V2L22,6L18,10V7H15L10.5,12L15,17H18V14L22,18L18,22V19H14L9,13H3Z");
    private static readonly Geometry Alert = Geometry.Parse("M1,21H23L12,2L1,21M13,18H11V16H13V18M13,14H11V10H13V14Z");

    /// <summary>The steps palette's tab: blocks of different shapes.</summary>
    public static readonly Geometry PaletteIcon = Geometry.Parse("M3,3H11V11H3Z M13,3H21V11H13Z M3,13H11V21H3Z M17,13A4,4 0 0,1 21,17A4,4 0 0,1 17,21A4,4 0 0,1 13,17A4,4 0 0,1 17,13Z");

    /// <summary>Floating a panel in a window of its own: a window over another.</summary>
    public static readonly Geometry FloatIcon = Geometry.Parse("M4,8H8V4H20V16H16V20H4V8M16,8V14H18V6H10V8H16M6,10V18H14V10H6Z");

    /// <summary>Collapsing a panel towards its side.</summary>
    public static readonly Geometry CollapseLeftIcon = Geometry.Parse("M18.41,7.41L17,6L11,12L17,18L18.41,16.59L13.83,12L18.41,7.41M12.41,7.41L11,6L5,12L11,18L12.41,16.59L7.83,12L12.41,7.41Z");

    public static readonly Geometry CollapseRightIcon = Geometry.Parse("M5.59,7.41L7,6L13,12L7,18L5.59,16.59L10.17,12L5.59,7.41M11.59,7.41L13,6L19,12L13,18L11.59,16.59L16.17,12L11.59,7.41Z");

    public static Geometry Icon(StepType type) => type switch
    {
        StepType.Start => Play,
        StepType.End => Flag,
        StepType.SetVariable => EqualsSign,
        StepType.Condition => Diamond,
        StepType.Delay => Icons.Timer,
        StepType.Http => Exchange,
        StepType.Grpc => Icons.Prompt,
        StepType.MqttPublish => Send,
        StepType.MqttWait => Receive,
        StepType.Script => Icons.DevTools,
        StepType.CallWorkflow => Icons.Workflow,
        StepType.UserInput => Person,
        StepType.Log => Icons.Notes,
        StepType.ForEach => Cycle,
        StepType.While => Repeat,
        StepType.Switch => Fork,
        _ => Alert,
    };

    /// <summary>The theme brush of a step's kind: the start green, the end the accent, a failure red, then a colour per palette group.</summary>
    public static string BrushKey(StepType type) => type switch
    {
        StepType.Start => "SuccessBrush",
        StepType.End => "AccentBrush",
        StepType.Fail => "DangerBrush",
        _ => StepTypes.GroupOf(type) switch
        {
            StepTypes.Group.Flow => "VioletBrush",
            StepTypes.Group.Data => "TealBrush",
            StepTypes.Group.Messages => "InfoBrush",
            _ => "PinkBrush",
        },
    };

    /// <summary>The brush of a step type, from the app's resources, for views to bind to.</summary>
    public static readonly IValueConverter BrushConverter = new FuncValueConverter<StepType, IBrush?>(type =>
        Application.Current?.TryFindResource(BrushKey(type), Application.Current.ActualThemeVariant, out var value) == true ? value as IBrush : Brushes.Gray);

    public static readonly IValueConverter IconConverter = new FuncValueConverter<StepType, Geometry>(Icon);
}
