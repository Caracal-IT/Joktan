using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Platform.Storage;
using Joktan.Logs.Resources;
using Joktan.Logs.ViewModels;

namespace Joktan.Logs.Views;

public partial class ErrorsView : UserControl
{
    /// <summary>Frames that are not in the code are shown fainter.</summary>
    public static readonly IValueConverter FoundOpacity = new FuncValueConverter<bool, double>(found => found ? 1 : 0.55);

    public ErrorsView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is ErrorsViewModel vm)
        {
            vm.PickFolder = PickFolderAsync;
            vm.OpenFile = OpenFileAsync;
        }
    }

    private async Task<string?> PickFolderAsync()
    {
        if (TopLevel.GetTopLevel(this) is not { } top)
            return null;

        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Strings.ErrorsView_ChooseTheSourceFolder,
            AllowMultiple = false,
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    private async Task OpenFileAsync(string path)
    {
        if (TopLevel.GetTopLevel(this) is { } top)
            await top.Launcher.LaunchFileInfoAsync(new FileInfo(path));
    }
}
