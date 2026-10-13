using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Joktan.ViewModels;

namespace Joktan.Views;

/// <summary>
/// The topic explorer itself. It is hosted by the page in the main window or by its own window.
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

    private async void OnCopyTopicClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is TopicExplorerViewModel { SelectedNode: { } node }
            && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(node.FullTopic);
        }
    }
}
