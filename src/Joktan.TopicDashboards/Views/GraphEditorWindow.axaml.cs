using System;
using Avalonia.Controls;
using Joktan.TopicDashboards.ViewModels;

namespace Joktan.TopicDashboards.Views;

public partial class GraphEditorWindow : Window
{
    public GraphEditorWindow()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is GraphEditorViewModel vm)
            vm.CloseRequested += saved => Close(saved);
    }

    // Closing by the window's own controls has to stop the editor's subscription too.
    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (DataContext is GraphEditorViewModel vm)
            _ = vm.StopAsync();
    }
}
