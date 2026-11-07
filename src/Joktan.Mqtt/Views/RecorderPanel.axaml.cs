using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Joktan.Mqtt.Resources;
using Joktan.Mqtt.ViewModels;

namespace Joktan.Mqtt.Views;

/// <summary>
/// The recorder itself, hosted by the page in the main window or by its own window. The file and folder
/// pickers open over whichever window it is in.
/// </summary>
public partial class RecorderPanel : UserControl
{
    public RecorderPanel()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is RecorderViewModel vm)
        {
            vm.PickFolder = PickFolderAsync;
            vm.PickRecording = PickRecordingAsync;
        }
    }

    private async Task<string?> PickFolderAsync()
    {
        if (TopLevel.GetTopLevel(this) is not { } top)
            return null;

        var picked = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Strings.RecorderView_ChooseAFolder,
            AllowMultiple = false,
        });
        return picked.FirstOrDefault()?.TryGetLocalPath();
    }

    private async Task<string?> PickRecordingAsync()
    {
        if (TopLevel.GetTopLevel(this) is not { } top)
            return null;

        var picked = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Strings.RecorderView_ChooseARecording,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(Strings.RecorderView_Recordings_Files) { Patterns = ["*.json"] }],
        });
        return picked.FirstOrDefault()?.TryGetLocalPath();
    }
}
