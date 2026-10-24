using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Joktan.Controls;

/// <summary>
/// A text colour that a TextBlock uses only when it is set. Bound to null, the TextBlock keeps the
/// colour its styles give it. Binding Foreground itself to an unset value gives the default black
/// instead, which cannot be read on the dark theme.
/// </summary>
public static class TabText
{
    public static readonly AttachedProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, IBrush?>("Foreground", typeof(TabText));

    static TabText()
    {
        ForegroundProperty.Changed.AddClassHandler<TextBlock>((text, e) =>
        {
            if (e.NewValue is IBrush brush)
                text.Foreground = brush;
            else
                text.ClearValue(TextBlock.ForegroundProperty);
        });
    }

    public static IBrush? GetForeground(TextBlock text) => text.GetValue(ForegroundProperty);

    public static void SetForeground(TextBlock text, IBrush? value) => text.SetValue(ForegroundProperty, value);
}
