using System;
using Avalonia;
using Avalonia.Controls;
using Joktan.Data.ViewModels;

namespace Joktan.Data.Views;

public partial class DataView : UserControl
{
    private DataViewModel? _vm;

    public DataView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _vm = DataContext as DataViewModel;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _vm?.Activate();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _vm?.Deactivate();
    }
}
