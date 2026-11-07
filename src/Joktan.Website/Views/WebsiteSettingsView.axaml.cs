using System;
using Avalonia;
using Avalonia.Controls;
using Joktan.Website.ViewModels;

namespace Joktan.Website.Views;

public partial class WebsiteSettingsView : UserControl
{
    public WebsiteSettingsView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is WebsiteSettingsViewModel page)
        {
            page.OpenUrl = async url =>
            {
                if (TopLevel.GetTopLevel(this) is { } top)
                    await top.Launcher.LaunchUriAsync(new Uri(url));
            };
        }
    }

    // The users may have been given a database, or passwords, since the page was last shown.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        (DataContext as WebsiteSettingsViewModel)?.Refresh();
    }
}
