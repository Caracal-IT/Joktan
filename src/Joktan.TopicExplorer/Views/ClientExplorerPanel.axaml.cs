using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Joktan.TopicExplorer.ViewModels;

namespace Joktan.TopicExplorer.Views;

/// <summary>
/// One client's tab of the topic explorer.
/// </summary>
public partial class ClientExplorerPanel : UserControl
{
    public ClientExplorerPanel()
    {
        InitializeComponent();
    }

    private async void OnCopyTopicClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ClientExplorerViewModel { SelectedNode: { } node }
            && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(node.FullTopic);
        }
    }
}
