using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace Joktan.Mqtt.Controls;

/// <summary>
/// A text colour that a TextBlock or a button uses only when it is set. Bound to null, the control
/// keeps the colour its styles give it. Binding Foreground itself to an unset value gives the default
/// black instead, which cannot be read on the dark theme.
/// </summary>
public static class TabText
{
    public static readonly AttachedProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.RegisterAttached<Control, IBrush?>("Foreground", typeof(TabText));

    static TabText()
    {
        ForegroundProperty.Changed.AddClassHandler<Control>((control, e) =>
        {
            if (e.NewValue is IBrush brush)
                control.SetValue(TextElement.ForegroundProperty, brush);
            else
                control.ClearValue(TextElement.ForegroundProperty);
        });
    }

    public static IBrush? GetForeground(Control control) => control.GetValue(ForegroundProperty);

    public static void SetForeground(Control control, IBrush? value) => control.SetValue(ForegroundProperty, value);
}
