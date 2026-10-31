using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Joktan.TopicDashboards.ViewModels;

namespace Joktan.TopicDashboards.Views;

public partial class TopicDashboardsView : UserControl
{
    public TopicDashboardsView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is TopicDashboardsViewModel vm)
            vm.ShowEditor = ShowEditorAsync;
    }

    private Task ShowEditorAsync(GraphEditorViewModel editor)
        => TopLevel.GetTopLevel(this) is Window owner
            ? new GraphEditorWindow { DataContext = editor }.ShowDialog(owner)
            : Task.CompletedTask;
}
