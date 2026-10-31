using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Joktan.Passwords.ViewModels;
using Joktan.Passwords.Views;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Passwords.Tests.Views;

[Collection(ProcessStateCollection.Name)]
public class PasswordsViewTests : DataFolderTest
{
    private static PasswordsViewModel Open()
    {
        var vm = new PasswordsViewModel(new PasswordSettings(), new FakeCredentialStore()) { SetupIsKeychain = true };
        vm.SetupCommand.Execute(null);
        return vm;
    }

    [Fact]
    public Task Shows_setting_up_then_the_entries_and_every_band() => Headless.Run(() =>
    {
        var vm = new PasswordsViewModel(new PasswordSettings(), new FakeCredentialStore());
        var window = Headless.Show(new PasswordsView(), vm);
        Assert.NotEmpty(Headless.All<RadioButton>(window));

        vm.SetupIsKeychain = true;
        vm.SetupCommand.Execute(null);
        Headless.Settle();
        Assert.True(vm.IsOpen);

        vm.NewFolderCommand.Execute(null);
        vm.EditTitle = "Work";
        vm.SaveCommand.Execute(null);
        vm.NewEntryCommand.Execute(null);
        vm.EditTitle = "One";
        vm.EditUsername = "me";
        vm.EditUrl = "example.com";
        vm.EditNotes = "Notes";
        Headless.Settle();
        window.UpdateLayout();
        vm.SaveCommand.Execute(null);
        vm.ToggleRevealCommand.Execute(null);
        vm.IsChangingStorage = true;
        vm.IsConfirmingDelete = true;
        vm.Error = "An error";
        vm.Message = "A message";
        vm.SearchText = "One";
        Headless.Settle();
        window.UpdateLayout();
        window.CaptureRenderedFrame();

        Assert.Single(vm.SearchResults);
        window.Close();
    });

    [Fact]
    public Task A_locked_vault_puts_the_focus_in_the_password_box() => Headless.Run(() =>
    {
        var path = Data.At("my.vault");
        var vault = new VaultStorage(path);
        vault.Create("correct horse");
        vault.Close();
        var vm = new PasswordsViewModel(new PasswordSettings { Storage = PasswordStorageKind.Vault, VaultPath = path }, new FakeCredentialStore());

        var window = Headless.Show(new PasswordsView(), vm);

        Assert.True(vm.IsLocked);
        var box = Headless.Find<TextBox>(window, t => t.Name == "UnlockBox");
        Assert.True(box.IsFocused);
        window.Close();
    });

    [Fact]
    public Task Using_the_page_keeps_it_from_locking() => Headless.Run(() =>
    {
        var vm = Open();
        var last = typeof(PasswordsViewModel).GetField("_lastActivity", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var window = Headless.Show(new PasswordsView(), vm);
        var longAgo = DateTime.UtcNow.AddDays(-1);

        last.SetValue(vm, longAgo);
        var box = Headless.Find<TextBox>(window, t => t.IsEffectivelyVisible);
        window.MouseMove(box.TranslatePoint(new Point(5, 5), window)!.Value);
        Assert.True((DateTime)last.GetValue(vm)! > longAgo);

        box.Focus();
        last.SetValue(vm, longAgo);
        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.None);
        Assert.True((DateTime)last.GetValue(vm)! > longAgo);
        window.Close();
    });

    [Fact]
    public Task The_view_lends_the_clipboard_browser_and_file_picker() => Headless.Run(async () =>
    {
        var vm = Open();
        var view = new PasswordsView { DataContext = vm };

        // Not on screen, there is nothing to use.
        await vm.CopyText!("text");
        await vm.OpenUri!(new Uri("https://example.com"));
        Assert.Null(await vm.PickVaultFile!());

        var window = Headless.Show(view);

        await vm.CopyText!("copied");
        Assert.Equal("copied", await window.Clipboard!.TryGetTextAsync());
        await vm.OpenUri!(new Uri("https://example.com"));
        Assert.Null(await vm.PickVaultFile!());
        window.Close();
    });
}
