using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Joktan.Settings.ViewModels;

namespace Joktan.Settings.Views;

public partial class BackupView : UserControl
{
    public BackupView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is BackupViewModel vm)
        {
            vm.PickFolder = PickFolderAsync;
            vm.PickZip = PickZipAsync;
        }
    }

    // Folders change on the pages that own them, so they are read again each time this one is shown.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        (DataContext as BackupViewModel)?.Refresh();
    }

    private async Task<string?> PickFolderAsync()
    {
        if (TopLevel.GetTopLevel(this) is not { } top)
            return null;

        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose a folder", AllowMultiple = false });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    private async Task<string?> PickZipAsync()
    {
        if (TopLevel.GetTopLevel(this) is not { } top)
            return null;

        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a Joktan backup",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Zip files") { Patterns = ["*.zip"] }],
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }
}
