using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Joktan.Api.ViewModels;
using Joktan.Api.Views;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Api.Tests.Views;

[Collection(ProcessStateCollection.Name)]
public class ApiClientViewTests : DataFolderTest
{
    private ApiClientViewModel New()
    {
        var workspace = Data.At("workspace");
        Directory.CreateDirectory(workspace);
        return new ApiClientViewModel(new ApiWorkspace(workspace));
    }

    [Fact]
    public Task Shows_rest_and_grpc_requests_their_responses_and_every_band() => Headless.Run(async () =>
    {
        await using var server = await FakeServer.StartAsync();
        var vm = New();
        var window = Headless.Show(new ApiClientView(), vm);

        vm.NewRestRequestCommand.Execute(null);
        vm.Request!.Url = server.RestAddress + "/ok";
        vm.Request.AuthKind = ApiAuthKind.Basic;
        vm.Request.BodyKind = ApiBodyKind.Form;
        vm.Request.Headers.AddCommand.Execute(null);
        Headless.Settle();
        await vm.Request.SendCommand.ExecuteAsync(null);
        Headless.Settle();
        window.UpdateLayout();
        Assert.Contains("POST", vm.Request.MethodChoices);

        vm.Request.AuthKind = ApiAuthKind.Bearer;
        vm.Request.BodyKind = ApiBodyKind.Json;
        Headless.Settle();

        vm.NewGrpcRequestCommand.Execute(null);
        vm.Request!.Address = server.GrpcAddress;
        vm.Request.Metadata.AddCommand.Execute(null);
        vm.Request.Captures.AddCommand.Execute(null);
        vm.Request.PickFiles = _ => Task.FromResult<System.Collections.Generic.IReadOnlyList<string>>(["/a.proto"]);
        await vm.Request.AddImportPathCommand.ExecuteAsync(null);
        await vm.Request.LoadServicesCommand.ExecuteAsync(null);
        Headless.Settle();
        await vm.Request.SendCommand.ExecuteAsync(null);
        Headless.Settle();
        window.UpdateLayout();

        // What sits on tabs not showing is only made once they are.
        foreach (var tabs in Headless.All<TabControl>(window).ToList())
        {
            for (var i = 0; i < tabs.ItemCount; i++)
            {
                tabs.SelectedIndex = i;
                Headless.Settle();
                window.UpdateLayout();
            }
        }

        vm.IsEditingEnvironments = true;
        vm.NewEnvironmentCommand.Execute(null);
        vm.IsConfirmingDelete = true;
        vm.IsChoosingWorkspace = true;
        vm.Error = "An error";
        vm.Message = "A message";
        vm.WorkspaceMessage = "A folder message";
        vm.Selected = vm.Roots[0];
        Headless.Settle();
        window.UpdateLayout();

        Assert.NotEmpty(Headless.All<TextBlock>(window));
        vm.SaveState();
        window.Close();
    });

    [Fact]
    public Task A_new_name_is_applied_on_enter_or_on_leaving_the_box() => Headless.Run(() =>
    {
        var vm = New();
        vm.NewRestRequestCommand.Execute(null);
        var window = Headless.Show(new ApiClientView(), vm);
        Headless.Settle();
        var box = Headless.Find<TextBox>(window, t => t.Name == "NameBox");
        box.Focus();

        vm.EditName = "On enter";
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Assert.Equal("On enter", vm.Selected!.Name);

        // Renaming reads the tree again, which took the focus from the box.
        box.Focus();
        vm.EditName = "On leaving";
        Headless.Find<Button>(window, b => b.IsEffectivelyVisible).Focus();
        Headless.Settle();
        Assert.Equal("On leaving", vm.Selected!.Name);
        window.Close();
    });

    [Fact]
    public Task The_view_lends_the_file_picker_and_clipboard() => Headless.Run(async () =>
    {
        var vm = New();
        var view = new ApiClientView { DataContext = vm };

        Assert.Empty(await vm.PickFiles!(true));
        await vm.CopyText!("nowhere to go");

        var window = Headless.Show(view);
        Assert.Empty(await vm.PickFiles!(true));
        Assert.Empty(await vm.PickFiles!(false));
        await vm.CopyText!("copied");

        Assert.Equal("copied", await window.Clipboard!.TryGetTextAsync());
        window.Close();
    });
}
