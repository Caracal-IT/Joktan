using System;
using Avalonia.Controls;
using Joktan.Mqtt.ViewModels;

namespace Joktan.Mqtt.Views;

public partial class MessageSettingsWindow : Window
{
    public MessageSettingsWindow()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is MessageSettingsViewModel vm)
            vm.CloseRequested += saved => Close(saved);
    }
}
