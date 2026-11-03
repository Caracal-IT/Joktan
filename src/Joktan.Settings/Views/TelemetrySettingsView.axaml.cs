using Avalonia;
using Avalonia.Controls;
using Joktan.Settings.ViewModels;

namespace Joktan.Settings.Views;

public partial class TelemetrySettingsView : UserControl
{
    public TelemetrySettingsView()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        (DataContext as TelemetrySettingsViewModel)?.Refresh();
    }
}
