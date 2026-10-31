using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Joktan.Mqtt.ViewModels;

namespace Joktan.Mqtt.Views;

public partial class MqttView : UserControl
{
    public MqttView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is MqttViewModel vm)
            vm.EditClientSettings = EditClientSettingsAsync;
    }

    private async Task<bool> EditClientSettingsAsync(ClientSettingsViewModel vm)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner)
            return false;

        return await new ClientSettingsWindow { DataContext = vm }.ShowDialog<bool>(owner);
    }
}
