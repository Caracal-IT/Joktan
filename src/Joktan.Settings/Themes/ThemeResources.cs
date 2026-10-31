using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace Joktan.Settings.Themes;

/// <summary>
/// Lays a theme over Tokens.axaml: its values go in one dictionary merged last into the app's
/// resources, which is looked in first, so everything using them through DynamicResource follows.
/// </summary>
internal sealed class ThemeResources
{
    // Fluent's own brushes that should follow the theme's, so that its controls match the pages.
    private static readonly Dictionary<string, string[]> FluentBrushes = new()
    {
        [ThemeTokens.TextBrush] =
        [
            "SystemControlForegroundBaseHighBrush", "SystemControlPageTextBaseHighBrush",
            "ButtonForeground", "ButtonForegroundPointerOver", "ButtonForegroundPressed",
            "RepeatButtonForeground", "RepeatButtonForegroundPointerOver", "RepeatButtonForegroundPressed",
            "SplitButtonForeground", "SplitButtonForegroundPointerOver", "SplitButtonForegroundPressed",
            "ToggleButtonForeground", "ToggleButtonForegroundPointerOver", "ToggleButtonForegroundPressed",
            "ToggleButtonForegroundIndeterminate", "ToggleButtonForegroundIndeterminatePointerOver", "ToggleButtonForegroundIndeterminatePressed",
            "CheckBoxForegroundChecked", "CheckBoxForegroundCheckedPointerOver", "CheckBoxForegroundCheckedPressed",
            "CheckBoxForegroundUnchecked", "CheckBoxForegroundUncheckedPointerOver", "CheckBoxForegroundUncheckedPressed",
            "CheckBoxForegroundIndeterminate", "CheckBoxForegroundIndeterminatePointerOver", "CheckBoxForegroundIndeterminatePressed",
            "RadioButtonForeground", "RadioButtonForegroundPointerOver", "RadioButtonForegroundPressed",
            "ToggleSwitchContentForeground",
            "ComboBoxForeground", "ComboBoxForegroundFocused", "ComboBoxForegroundFocusedPressed",
            "ComboBoxPlaceHolderForeground", "ComboBoxPlaceHolderForegroundFocusedPressed",
            "ComboBoxItemForeground", "ComboBoxItemForegroundPointerOver", "ComboBoxItemForegroundPressed",
            "ComboBoxItemForegroundSelected", "ComboBoxItemForegroundSelectedPointerOver", "ComboBoxItemForegroundSelectedPressed",
            "TextControlForeground", "TextControlForegroundFocused", "TextControlForegroundPointerOver",
            "MenuFlyoutItemForeground", "MenuFlyoutItemForegroundPointerOver", "MenuFlyoutItemForegroundPressed",
            "TreeViewItemForeground", "TreeViewItemForegroundPointerOver", "TreeViewItemForegroundPressed",
            "TreeViewItemForegroundSelected", "TreeViewItemForegroundSelectedPointerOver", "TreeViewItemForegroundSelectedPressed",
            "TabItemHeaderForegroundSelected", "ToolTipForeground",
        ],
        [ThemeTokens.TextMutedBrush] =
        [
            "TextControlPlaceholderForeground", "TextControlPlaceholderForegroundPointerOver",
            "TabItemHeaderForegroundUnselected",
        ],
        [ThemeTokens.OnAccentBrush] = ["AccentButtonForeground", "AccentButtonForegroundPointerOver", "AccentButtonForegroundPressed"],
        [ThemeTokens.SurfaceBrush] = ["ComboBoxDropDownBackground", "FlyoutPresenterBackground", "MenuFlyoutPresenterBackground", "ToolTipBackground"],
        [ThemeTokens.BorderBrush] = ["ComboBoxDropDownBorderBrush", "FlyoutBorderThemeBrush", "MenuFlyoutPresenterBorderBrush", "ToolTipBorderBrush"],
    };

    private readonly FontFamily _codeFont;
    private ResourceDictionary? _applied;

    /// <param name="codeFont">The monospaced fonts code is shown in unless a theme names another.</param>
    public ThemeResources(FontFamily codeFont)
    {
        _codeFont = codeFont;
    }

    /// <summary>Puts the whole theme in place at once, so that the app is told of one change.</summary>
    public void Apply(Theme theme, ThemeVariant variant)
    {
        if (Application.Current is not { } app)
            return;

        var resources = new ResourceDictionary();
        foreach (var token in ThemeTokens.Colors)
            SetColor(resources, token.Key, theme.Colors[token.Key]);
        SetFonts(resources, theme);
        foreach (var token in ThemeTokens.TextSizes)
            resources[token.Key] = theme.TextSizes[token.Key];

        var merged = app.Resources.MergedDictionaries;
        merged.Add(resources);
        if (_applied is not null)
            merged.Remove(_applied);
        _applied = resources;

        app.RequestedThemeVariant = variant;
    }

    public void UpdateColor(string key, string hex)
    {
        if (_applied is not null)
            SetColor(_applied, key, hex);
    }

    public void UpdateFonts(Theme theme)
    {
        if (_applied is not null)
            SetFonts(_applied, theme);
    }

    public void UpdateTextSize(string key, double size)
    {
        if (_applied is not null)
            _applied[key] = size;
    }

    private static void SetColor(ResourceDictionary resources, string key, string hex)
    {
        var color = Color.Parse(hex);
        resources[key] = new SolidColorBrush(color);

        if (FluentBrushes.TryGetValue(key, out var fluentKeys))
        {
            foreach (var fluentKey in fluentKeys)
                resources[fluentKey] = new SolidColorBrush(color);
        }

        // Meter tracks are the fill at low opacity, so the whole bar reads as one state.
        switch (key)
        {
            case ThemeTokens.AccentBrush:
                resources["AccentTrackBrush"] = new SolidColorBrush(color, 0.18);
                SetAccent(resources, color);
                break;
            case ThemeTokens.WarningBrush:
                resources["WarningTrackBrush"] = new SolidColorBrush(color, 0.22);
                break;
            case ThemeTokens.DangerBrush:
                resources["DangerTrackBrush"] = new SolidColorBrush(color, 0.2);
                break;
        }
    }

    // Fluent's controls use the accent and three steps either side of it.
    private static void SetAccent(ResourceDictionary resources, Color accent)
    {
        resources["SystemAccentColor"] = accent;
        var hsl = accent.ToHsl();
        for (var step = 1; step <= 3; step++)
        {
            resources[$"SystemAccentColorLight{step}"] = WithLightness(hsl, hsl.L + (1 - hsl.L) * step * 0.2);
            resources[$"SystemAccentColorDark{step}"] = WithLightness(hsl, hsl.L * (1 - step * 0.2));
        }
    }

    private static Color WithLightness(HslColor hsl, double lightness) => HslColor.ToRgb(hsl.H, hsl.S, lightness, hsl.A);

    // A font this computer does not have falls back to the one that comes with Joktan.
    private static FontFamily Family(string name) => new(name.Length == 0 ? "$Default" : $"{name},$Default");

    private void SetFonts(ResourceDictionary resources, Theme theme)
    {
        // A font left at its default follows the interface font, except code, which stays monospaced.
        var interfaceFont = theme.Fonts[ThemeTokens.InterfaceFont];
        foreach (var token in ThemeTokens.Fonts)
        {
            var name = theme.Fonts[token.Key];
            if (name.Length == 0 && token.Key == ThemeTokens.CodeFont)
                resources[token.Key] = _codeFont;
            else
                resources[token.Key] = Family(name.Length > 0 ? name : interfaceFont);
        }
    }
}
