using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Joktan.TopicDashboards;

/// <summary>
/// The colours a graph's values are drawn in. They are <c>Chart1Brush</c> to <c>Chart8Brush</c> in
/// Tokens.axaml, in an order checked for colour blindness, with their own steps for the dark theme.
/// </summary>
public static class GraphColors
{
    public const int Count = 8;

    public static IReadOnlyList<string> Names { get; } = ["Blue", "Orange", "Aqua", "Yellow", "Magenta", "Green", "Violet", "Red"];

    public static IBrush Brush(StyledElement element, int slot)
        => element.TryFindResource($"Chart{slot % Count + 1}Brush", (element as Control)?.ActualThemeVariant, out var found) && found is IBrush brush
            ? brush
            : Brushes.Gray;
}
