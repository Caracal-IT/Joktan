using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Joktan.TopicDashboards.Controls;

/// <summary>A dot in one of the <see cref="GraphColors"/>, beside the name of what it stands for.</summary>
public class ColorDot : Control
{
    public static readonly StyledProperty<int> SlotProperty =
        AvaloniaProperty.Register<ColorDot, int>(nameof(Slot));

    static ColorDot()
    {
        AffectsRender<ColorDot>(SlotProperty);
    }

    public ColorDot()
    {
        Width = Height = 10;
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
    }

    public int Slot
    {
        get => GetValue(SlotProperty);
        set => SetValue(SlotProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ThemeVariantScope.ActualThemeVariantProperty)
            InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var radius = System.Math.Min(Bounds.Width, Bounds.Height) / 2;
        context.DrawEllipse(GraphColors.Brush(this, Slot), null, new Point(Bounds.Width / 2, Bounds.Height / 2), radius, radius);
    }
}
