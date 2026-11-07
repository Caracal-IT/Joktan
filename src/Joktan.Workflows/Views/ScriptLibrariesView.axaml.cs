using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Joktan.Workflows.Resources;
using Joktan.Workflows.ViewModels;

namespace Joktan.Workflows.Views;

public partial class ScriptLibrariesView : UserControl
{
    public ScriptLibrariesView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is StepViewModel step)
                step.PickFolder = PickFolderAsync;
        };

        // Enter adds what was typed, as the button does.
        NewLibraryBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && DataContext is StepViewModel step)
            {
                step.AddLibraryCommand.Execute(null);
                e.Handled = true;
            }
        };
    }

    private async Task<string?> PickFolderAsync()
    {
        if (TopLevel.GetTopLevel(this) is not { } top)
            return null;
        var picked = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = Strings.Step_ChooseFolder, AllowMultiple = false });
        return picked.Select(i => i.TryGetLocalPath()).OfType<string>().FirstOrDefault();
    }
}
