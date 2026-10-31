using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Joktan.Passwords.ViewModels;

namespace Joktan.Passwords.Views;

public partial class PasswordsView : UserControl
{
    // How long a copied password stays on the clipboard.
    private static readonly TimeSpan ClipboardLifetime = TimeSpan.FromSeconds(30);

    public PasswordsView()
    {
        InitializeComponent();

        // Using the page keeps an open vault from locking.
        AddHandler(PointerMovedEvent, (_, _) => (DataContext as PasswordsViewModel)?.Touch(), RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(KeyDownEvent, (_, _) => (DataContext as PasswordsViewModel)?.Touch(), RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is PasswordsViewModel vm)
        {
            vm.CopyText = CopyTextAsync;
            vm.OpenUri = OpenUriAsync;
            vm.PickVaultFile = PickVaultFileAsync;
        }
    }

    protected override void OnAttachedToVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is PasswordsViewModel { IsLocked: true })
            Dispatcher.UIThread.Post(() => UnlockBox.Focus());
    }

    // The clipboard is cleared after a while, unless something else has been copied since.
    private async Task CopyTextAsync(string text)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
            return;

        await clipboard.SetTextAsync(text);
        DispatcherTimer.RunOnce(async () =>
        {
            try
            {
                if (await clipboard.TryGetTextAsync() == text)
                    await clipboard.ClearAsync();
            }
            catch (Exception)
            {
                // The clipboard may be busy with another app; there is nothing more to do.
            }
        }, ClipboardLifetime);
    }

    private async Task OpenUriAsync(Uri uri)
    {
        if (TopLevel.GetTopLevel(this) is { } top)
            await top.Launcher.LaunchUriAsync(uri);
    }

    private async Task<string?> PickVaultFileAsync()
    {
        if (TopLevel.GetTopLevel(this) is not { } top)
            return null;

        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Choose a vault file, or where to make one",
            SuggestedFileName = VaultStorage.FileName,
            DefaultExtension = "vault",
            ShowOverwritePrompt = false,
            FileTypeChoices = [new FilePickerFileType("Joktan vault") { Patterns = ["*.vault"] }],
        });
        return file?.TryGetLocalPath();
    }
}
