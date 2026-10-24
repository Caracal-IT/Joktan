using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;

namespace Joktan.ViewModels;

/// <summary>A colour offered for a client's tab or page.</summary>
public sealed record Swatch(string Name, string Hex)
{
    public IBrush Brush { get; } = new SolidColorBrush(Color.Parse(Hex));
}

/// <summary>
/// The colours a client's tab and page can be given. A colour is kept as text, <c>#RRGGBB</c>,
/// and empty means the theme's own.
/// </summary>
public static class TabColors
{
    public static IReadOnlyList<Swatch> Palette { get; } =
    [
        new("Purple", "#5747B5"),
        new("Blue", "#0D8BF2"),
        new("Teal", "#00A3A3"),
        new("Green", "#3BB001"),
        new("Amber", "#E0920B"),
        new("Red", "#DC3545"),
        new("Pink", "#F10075"),
        new("Navy", "#1C273C"),
        new("White", "#FFFFFF"),
        new("Lavender", "#E8E5F8"),
        new("Sky", "#DCEEFE"),
        new("Mint", "#D6F5F5"),
        new("Leaf", "#E2F3D9"),
        new("Sand", "#FCEFD8"),
        new("Rose", "#F9DCDF"),
        new("Blush", "#FDD9EA"),
        new("Grey", "#EDEFF4"),
    ];

    private static readonly IBrush DarkText = new SolidColorBrush(Color.Parse("#1C273C"));
    private static readonly IBrush LightText = Brushes.White;

    /// <summary>The colour, or null when the text is empty or not a colour.</summary>
    public static Color? Parse(string? text)
        => text?.Trim() is { Length: > 0 } value && Color.TryParse(value, out var color) ? color : null;

    /// <summary>Whether the text is empty or a colour such as #3BB001.</summary>
    public static bool IsValid(string? text) => string.IsNullOrWhiteSpace(text) || Parse(text) is not null;

    /// <summary>The colour as #RRGGBB, for saving.</summary>
    public static string Normalize(string? text)
        => Parse(text) is { } color ? $"#{color.R:X2}{color.G:X2}{color.B:X2}" : "";

    /// <summary>A brush for the colour, or unset so the theme's own is used.</summary>
    public static object BrushOrUnset(string? text)
        => Parse(text) is { } color ? new SolidColorBrush(color) : AvaloniaProperty.UnsetValue;

    /// <summary>
    /// The tab's text colour: the one chosen, or dark or light text to stand out on a chosen fill,
    /// or unset for the theme's own.
    /// </summary>
    public static object TextBrush(string? text, string? fill)
    {
        if (Parse(text) is { } color)
            return new SolidColorBrush(color);
        if (Parse(fill) is not { } background)
            return AvaloniaProperty.UnsetValue;

        // Relative luminance, roughly, to pick the text that reads better on the fill.
        var luminance = (0.2126 * background.R + 0.7152 * background.G + 0.0722 * background.B) / 255;
        return luminance > 0.55 ? DarkText : LightText;
    }
}
