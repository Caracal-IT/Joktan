using System.Linq;
using Avalonia;
using Avalonia.Controls;

namespace Joktan.Api.Controls;

/// <summary>
/// Gives a TextBlock the style class of a request's method, such as "post" for POST, which the
/// styles colour. A method that is not a plain word gets no class and keeps the default colour.
/// </summary>
public static class MethodColor
{
    public static readonly AttachedProperty<string?> MethodProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, string?>("Method", typeof(MethodColor));

    static MethodColor()
    {
        MethodProperty.Changed.AddClassHandler<TextBlock>((text, e) =>
        {
            if (ClassOf(e.OldValue as string) is { } previous)
                text.Classes.Remove(previous);
            if (ClassOf(e.NewValue as string) is { } current)
                text.Classes.Add(current);
        });
    }

    public static string? GetMethod(TextBlock text) => text.GetValue(MethodProperty);

    public static void SetMethod(TextBlock text, string? value) => text.SetValue(MethodProperty, value);

    private static string? ClassOf(string? method)
        => string.IsNullOrEmpty(method) || !method.All(char.IsAsciiLetter) ? null : method.ToLowerInvariant();
}
