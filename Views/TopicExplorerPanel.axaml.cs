using Avalonia;
using Avalonia.Controls;
using Joktan.ViewModels;

namespace Joktan.Views;

/// <summary>
/// The topic explorer itself, with a tab per client. It is hosted by the page in the main window
/// or by its own window.
/// </summary>
public partial class TopicExplorerPanel : UserControl
{
    public TopicExplorerPanel()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        (DataContext as TopicExplorerViewModel)?.Activate();
    }
}
