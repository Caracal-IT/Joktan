using System;
using Avalonia.Controls;
using Joktan.Mqtt.ViewModels;

namespace Joktan.Mqtt.Views;

public partial class RecorderView : UserControl
{
    public RecorderView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is RecorderViewModel vm)
            vm.OpenWindow = () => OpenWindow(vm);
    }

    // The window has no owner, so it can sit on another screen or behind the main window.
    private void OpenWindow(RecorderViewModel vm)
    {
        if (vm.IsPoppedOut)
            return;

        var window = new RecorderWindow { DataContext = vm };
        window.Closed += (_, _) =>
        {
            PageHost.Content = new RecorderPanel();
            vm.IsPoppedOut = false;
            vm.CloseWindow = null;
        };
        PageHost.Content = null;
        vm.CloseWindow = window.Close;
        vm.IsPoppedOut = true;
        window.Show();
    }
}
