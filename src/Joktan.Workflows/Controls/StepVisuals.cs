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
    private static readonly Geometry Split = Geometry.Parse("M14,4L16.29,6.29L13.41,9.17L14.83,10.59L17.71,7.71L20,10V4M10,4H4V10L6.29,7.71L11,12.41V20H13V11.59L7.71,6.29");
    private static readonly Geometry Merge = Geometry.Parse("M17,20.41L18.41,19L15,15.59L13.59,17M7.5,8H11V13.59L5.59,19L7,20.41L13,14.41V8H16.5L12,3.5");
    private static readonly Geometry Fork = Geometry.Parse("M3,11H9L14,5H18V2L22,6L18,10V7H15L10.5,12L15,17H18V14L22,18L18,22V19H14L9,13H3Z");
    private static readonly Geometry Alert = Geometry.Parse("M1,21H23L12,2L1,21M13,18H11V16H13V18M13,14H11V10H13V14Z");

    // A database, small at the top left, with what is done to it at the bottom right.
    private const string Cylinder = "M8.5,2C5.4,2 2.9,3.3 2.9,4.9C2.9,6.4 5.4,7.7 8.5,7.7C11.6,7.7 14.1,6.4 14.1,4.9C14.1,3.3 11.6,2 8.5,2M2.9,6.3V8.5C2.9,10.1 5.4,11.3 8.5,11.3C11.6,11.3 14.1,10.1 14.1,8.5V6.3C14.1,7.9 11.6,9.1 8.5,9.1C5.4,9.1 2.9,7.9 2.9,6.3M2.9,9.9V12.1C2.9,13.7 5.4,14.9 8.5,14.9C9.6,14.9 10.6,14.8 11.5,14.5V12.9C10.6,13.3 9.6,13.5 8.5,13.5C5.4,13.5 2.9,12.3 2.9,10.7Z";
    private static readonly Geometry DatabaseRead = Geometry.Parse(Cylinder + " M17,12A5,5 0 1,1 17,22A5,5 0 1,1 17,12M17,14A3,3 0 1,0 17,20A3,3 0 1,0 17,14Z M20.5,20.1L23.6,23.2L22.2,24.6L19.1,21.5Z");
    private static readonly Geometry DatabaseInsert = Geometry.Parse(Cylinder + " M16,13H19V17H23V20H19V24H16V20H12V17H16Z");
    private static readonly Geometry DatabaseUpdate = Geometry.Parse(Cylinder + " M12,21.2V24H14.8L21.6,17.2L18.8,14.4Z M23.6,15.2C23.9,14.9 23.9,14.4 23.6,14.1L21.9,12.4C21.6,12.1 21.1,12.1 20.8,12.4L19.6,13.6L22.4,16.4Z");
    private static readonly Geometry DatabaseDelete = Geometry.Parse(Cylinder + " M12,17H24V20H12Z");
    private static readonly Geometry DatabaseUpsert = Geometry.Parse(Cylinder + " M18,12A6,6 0 0,1 24,18H22A4,4 0 0,0 18,14V16L15,13L18,10Z M18,24A6,6 0 0,1 12,18H14A4,4 0 0,0 18,22V20L21,23L18,26Z");

    /// <summary>The steps palette's tab: blocks of different shapes.</summary>
    public static readonly Geometry PaletteIcon = Geometry.Parse("M3,3H11V11H3Z M13,3H21V11H13Z M3,13H11V21H3Z M17,13A4,4 0 0,1 21,17A4,4 0 0,1 17,21A4,4 0 0,1 13,17A4,4 0 0,1 17,13Z");

    /// <summary>A step on the favourites strip, and one that could be.</summary>
    public static readonly Geometry StarIcon = Geometry.Parse("M12,17.27L18.18,21L16.54,13.97L22,9.24L14.81,8.62L12,2L9.19,8.62L2,9.24L7.45,13.97L5.82,21L12,17.27Z");

    public static readonly Geometry StarOutlineIcon = Geometry.Parse("M12,15.39L8.24,17.66L9.23,13.38L5.91,10.5L10.29,10.13L12,6.09L13.71,10.13L18.09,10.5L14.77,13.38L15.76,17.66M22,9.24L14.81,8.63L12,2L9.19,8.63L2,9.24L7.45,13.97L5.82,21L12,17.27L18.18,21L16.54,13.97L22,9.24Z");

    public static readonly Geometry ChevronRightIcon = Geometry.Parse("M8.59,16.58L13.17,12L8.59,7.41L10,6L16,12L10,18L8.59,16.58Z");

    public static readonly Geometry ChevronLeftIcon = Geometry.Parse("M15.41,16.58L10.83,12L15.41,7.41L14,6L8,12L14,18L15.41,16.58Z");

    // The sections of the properties, down the strip at their side.
    public static readonly Geometry InfoIcon = Geometry.Parse("M13,9H11V7H13M13,17H11V11H13M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2Z");

    public static readonly Geometry VariableIcon = Geometry.Parse("M20.41,3C21.8,5.71 22.35,8.84 22,12C21.8,15.16 20.7,18.29 18.83,21L17.3,20C18.91,17.57 19.85,14.8 20,12C20.34,9.2 19.89,6.43 18.7,4L20.41,3M5.17,3L6.87,4C5.67,6.43 5.22,9.2 5.5,12C5.65,14.8 6.59,17.57 8.2,20L6.66,21C4.8,18.29 3.7,15.17 3.5,12C3.15,8.84 3.69,5.71 5.17,3M12.08,10.68L14.4,7.45H16.93L13.15,12.45L15.35,17.37H13.09L11.71,14L9.28,17.33H6.76L10.66,12.21L8.53,7.45H10.8L12.08,10.68Z");

    public static readonly Geometry BoltIcon = Geometry.Parse("M11,15H6L13,1V9H18L11,23V15Z");

    public static readonly Geometry LanesIcon = Geometry.Parse("M3,4H21V8H3V4M3,10H21V14H3V10M3,16H21V20H3V16Z");

    public static readonly Geometry ResultIcon = Geometry.Parse("M5,20H19V18H5M19,9H15V3H9V9H5L12,16L19,9Z");

    public static readonly Geometry RetryIcon = Repeat;

    public static readonly Geometry WarningIcon = Alert;


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
        StepType.Parallel => Split,
        StepType.Join => Merge,
        StepType.DatabaseRead => DatabaseRead,
        StepType.DatabaseInsert => DatabaseInsert,
        StepType.DatabaseUpdate => DatabaseUpdate,
        StepType.DatabaseDelete => DatabaseDelete,
        StepType.DatabaseUpsert => DatabaseUpsert,
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
            StepTypes.Group.Database => "WarningBrush",
            StepTypes.Group.Messages => "InfoBrush",
            _ => "PinkBrush",
        },
    };

    /// <summary>The brush of a step type, from the app's resources, for views to bind to.</summary>
    public static readonly IValueConverter BrushConverter = new FuncValueConverter<StepType, IBrush?>(type =>
        Application.Current?.TryFindResource(BrushKey(type), Application.Current.ActualThemeVariant, out var value) == true ? value as IBrush : Brushes.Gray);

    public static readonly IValueConverter IconConverter = new FuncValueConverter<StepType, Geometry>(Icon);
}
