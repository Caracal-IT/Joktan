using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Joktan.ViewModels;

namespace Joktan.Views;

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
        {
            vm.PickFolder = PickFolderAsync;
            vm.EditSettings = EditSettingsAsync;
        }
    }

    private void OnManualBodyLostFocus(object? sender, RoutedEventArgs e)
        => (DataContext as MqttViewModel)?.FormatManualBody();

    private async Task<string?> PickFolderAsync()
    {
        if (TopLevel.GetTopLevel(this) is not { } topLevel)
            return null;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose the folder that holds the messages",
            AllowMultiple = false,
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    private async Task<bool> EditSettingsAsync(MessageSettingsViewModel vm)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner)
            return false;

        return await new MessageSettingsWindow { DataContext = vm }.ShowDialog<bool>(owner);
    }
}
