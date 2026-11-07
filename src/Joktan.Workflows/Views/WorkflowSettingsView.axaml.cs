using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Joktan.Workflows.Resources;
using Joktan.Workflows.ViewModels;

namespace Joktan.Workflows.Views;

public partial class WorkflowSettingsView : UserControl
{
    public WorkflowSettingsView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is WorkflowSettingsViewModel vm)
            vm.PickFolder = PickFolderAsync;
    }

    private async Task<string?> PickFolderAsync()
    {
        if (TopLevel.GetTopLevel(this) is not { } top)
            return null;
        var picked = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = Strings.Settings_ChooseFolder, AllowMultiple = false });
        return picked.Select(i => i.TryGetLocalPath()).OfType<string>().FirstOrDefault();
    }
}
