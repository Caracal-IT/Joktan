using System;
using Avalonia.Controls;
using Joktan.TopicExplorer.ViewModels;

namespace Joktan.TopicExplorer.Views;

public partial class TopicExplorerView : UserControl
{
    public TopicExplorerView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is TopicExplorerViewModel vm)
            vm.OpenWindow = () => OpenWindow(vm);
    }

    // The window has no owner, so it can sit on another screen or behind the main window.
    private static void OpenWindow(TopicExplorerViewModel vm)
    {
        if (vm.IsPoppedOut)
            return;

        var window = new TopicExplorerWindow { DataContext = vm };
        window.Closed += (_, _) =>
        {
            vm.IsPoppedOut = false;
            vm.CloseWindow = null;
        };
        vm.CloseWindow = window.Close;
        vm.IsPoppedOut = true;
        window.Show();
    }
}
