using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Joktan.Containers.Resources;
using Joktan.Containers.ViewModels;

namespace Joktan.Containers.Views;

public partial class ContainerSettingsView : UserControl
{
    public ContainerSettingsView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is ContainerSettingsViewModel vm)
        {
            vm.Containers.PickComposeFiles = PickComposeFilesAsync;
            vm.Brokers.PickFolder = PickFolderAsync;
        }
    }

    private async Task<IReadOnlyList<string>> PickComposeFilesAsync()
    {
        if (TopLevel.GetTopLevel(this) is not { } top)
            return [];

        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Strings.ContainerSettingsView_ChooseComposeFiles,
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType(Strings.ContainerSettingsView_ComposeFiles) { Patterns = ["*.yml", "*.yaml"] },
                FilePickerFileTypes.All,
            ],
        });
        return files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
    }

    private async Task<string?> PickFolderAsync()
    {
        if (TopLevel.GetTopLevel(this) is not { } top)
            return null;

        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Strings.ContainerSettingsView_ChooseTheBrokersFolder,
            AllowMultiple = false,
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }
}
