using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Joktan.Mqtt.Resources;
using Joktan.Mqtt.ViewModels;

namespace Joktan.Mqtt.Views;

public partial class ProxiesView : UserControl
{
    public ProxiesView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is not ProxiesViewModel vm)
            return;

        vm.EditClientSettings = editor => ClientSettingsWindow.ShowOverAsync(this, editor);
        vm.EditSettings = EditSettingsAsync;
        vm.PickFolder = PickFolderAsync;
    }

    private async Task<bool> EditSettingsAsync(MessageSettingsViewModel vm)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner)
            return false;

        return await new MessageSettingsWindow { DataContext = vm }.ShowDialog<bool>(owner);
    }

    private async Task<string?> PickFolderAsync()
    {
        if (TopLevel.GetTopLevel(this) is not { } top)
            return null;

        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Strings.MqttClientView_ChooseTheFolderThatHoldsThe,
            AllowMultiple = false,
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }
}
