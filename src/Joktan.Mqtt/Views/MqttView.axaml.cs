using System;
using Avalonia;
using Avalonia.Controls;
using Joktan.Mqtt.ViewModels;

namespace Joktan.Mqtt.Views;

public partial class MqttView : UserControl
{
    // The clients' own window, while they are in it, so the client editor opens over it.
    private MqttWindow? _window;

    public MqttView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is MqttViewModel vm)
        {
            vm.EditClientSettings = editor => ClientSettingsWindow.ShowOverAsync(_window ?? (Visual)this, editor);
            vm.OpenWindow = () => OpenWindow(vm);
        }
    }

    // The window has no owner, so it can sit on another screen or behind the main window.
    private void OpenWindow(MqttViewModel vm)
    {
        if (vm.IsPoppedOut)
            return;

        var window = new MqttWindow { DataContext = vm };
        window.Closed += (_, _) =>
        {
            PageHost.Content = new MqttPanel();
            _window = null;
            vm.IsPoppedOut = false;
            vm.CloseWindow = null;
        };
        _window = window;
        PageHost.Content = null;
        vm.CloseWindow = window.Close;
        vm.IsPoppedOut = true;
        window.Show();
    }
}
