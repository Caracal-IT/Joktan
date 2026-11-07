using System;
using Avalonia.Controls;
using Joktan.Agent.ViewModels;

namespace Joktan.Agent.Views;

public partial class RestApiSettingsView : UserControl
{
    public RestApiSettingsView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is RestApiSettingsViewModel page)
        {
            page.CopyText = async text =>
            {
                if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
                    await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(clipboard, text);
            };
            page.OpenUrl = async url =>
            {
                if (TopLevel.GetTopLevel(this) is { } top)
                    await top.Launcher.LaunchUriAsync(new Uri(url));
            };
        }
    }
}
