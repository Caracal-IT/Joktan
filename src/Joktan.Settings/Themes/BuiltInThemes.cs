using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;

namespace Joktan.Settings.Themes;

/// <summary>The themes that come with Joktan. Light and Dark are what Tokens.axaml holds.</summary>
public static class BuiltInThemes
{
    public const string LightName = "Light";
    public const string DarkName = "Dark";
    public const string GreenDarkName = "Green Dark";

    // Dark's graph and status colours carry over: their order was checked for colour blindness,
    // and each of them stays above 3:1 on this surface.
    private static readonly Dictionary<string, string> GreenDarkColors = new()
    {
        ["SidebarBackgroundBrush"] = "#0F2A1E",
        ["SidebarActiveBrush"] = "#1C4A34",
        ["SidebarHoverBrush"] = "#163A29",
        ["SidebarDividerBrush"] = "#1F4633",
        ["SidebarTextBrush"] = "#A9C9B7",
        ["SidebarTextActiveBrush"] = "#FFFFFF",
        ["AppBackgroundBrush"] = "#0E1512",
        ["SurfaceBrush"] = "#17211C",
        ["SurfaceAltBrush"] = "#131C18",
        ["BorderBrush"] = "#2F4238",
        ["HoverBrush"] = "#22302A",
        ["TextBrush"] = "#E4EEE8",
        ["TextMutedBrush"] = "#8FA89A",
        ["AccentBrush"] = "#1E8449",
    };

    /// <summary>Tokens.axaml on its own, without the theme laid over it.</summary>
    public static ResourceDictionary LoadTokens()
        => (ResourceDictionary)AvaloniaXamlLoader.Load(new Uri("avares://Joktan.Core/Styles/Tokens.axaml"));

    public static IReadOnlyList<Theme> Load(ResourceDictionary tokens)
    {
        var light = Read(tokens, LightName, ThemeVariant.Light, isDark: false);
        var dark = Read(tokens, DarkName, ThemeVariant.Dark, isDark: true);

        var greenDark = dark.Clone();
        greenDark.Name = GreenDarkName;
        foreach (var (key, color) in GreenDarkColors)
            greenDark.Colors[key] = color;

        return [light, dark, greenDark];
    }

    private static Theme Read(ResourceDictionary tokens, string name, ThemeVariant variant, bool isDark)
    {
        var theme = new Theme { Name = name, IsDark = isDark };

        foreach (var token in ThemeTokens.Colors)
        {
            if (tokens.TryGetResource(token.Key, variant, out var brush) && brush is ISolidColorBrush solid)
                theme.Colors[token.Key] = Theme.ToHex(solid.Color);
            else
                throw new InvalidOperationException($"Tokens.axaml has no colour {token.Key}");
        }

        foreach (var token in ThemeTokens.Fonts)
            theme.Fonts[token.Key] = "";

        foreach (var token in ThemeTokens.TextSizes)
        {
            if (tokens.TryGetResource(token.Key, variant, out var size) && size is double value)
                theme.TextSizes[token.Key] = value;
            else
                throw new InvalidOperationException($"Tokens.axaml has no text size {token.Key}");
        }

        return theme;
    }
}
