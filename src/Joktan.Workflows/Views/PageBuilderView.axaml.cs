using System;
using Avalonia.Controls;
using Joktan.Workflows.ViewModels;

namespace Joktan.Workflows.Views;

public partial class PageBuilderView : UserControl
{
    public PageBuilderView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is PageBuilderViewModel page)
        {
            page.OpenUrl = async url =>
            {
                if (TopLevel.GetTopLevel(this) is { } top)
                    await top.Launcher.LaunchUriAsync(new Uri(url));
            };
        }
    }
}
