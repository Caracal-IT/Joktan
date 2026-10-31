using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Joktan.Api.ViewModels;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Api.Tests.ViewModels;

[Collection(ProcessStateCollection.Name)]
public class ApiClientViewModelTests : DataFolderTest
{
    private string Workspace => Data.At("workspace");

    private ApiClientViewModel New()
    {
        Directory.CreateDirectory(Workspace);
        return new ApiClientViewModel(new ApiWorkspace(Workspace));
    }

    private string Put(string relative, ApiRequest? request = null)
    {
        var path = Path.Combine(Workspace, relative + ApiWorkspace.RequestExtension);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        ApiWorkspace.Write(path, request ?? new ApiRequest());
        return path;
    }

    private static ApiNodeViewModel Node(ApiClientViewModel vm, string name) =>
        vm.Roots.SelectMany(r => r.SelfAndDescendants()).First(n => n.Name == name);

    [Fact]
    public Task An_empty_folder_shows_nothing() => Headless.Run(() =>
    {
        var vm = New();

        Assert.True(vm.IsEmpty);
        Assert.False(vm.HasSelection);
        Assert.False(vm.HasRequest);
        Assert.False(vm.IsFolderSelected);
        Assert.Equal("", vm.FolderSummary);
        Assert.Equal("", vm.DeleteText);
        Assert.Equal(Path.GetFullPath(Workspace), vm.Folder);
        Assert.Equal(ApiClientViewModel.NoEnvironment, vm.SelectedEnvironment);
        Assert.False(vm.HasEnvironments);
    });

    [Fact]
    public Task The_default_page_opens_the_chosen_folder() => Headless.Run(() =>
    {
        var vm = new ApiClientViewModel();

        Assert.Equal(ApiWorkspace.ChosenFolder, vm.Folder);
        Assert.Equal(ApiWorkspace.DefaultFolder, vm.DefaultFolder);
    });

    [Fact]
    public Task Lists_folders_and_requests_and_selects_the_first_request() => Headless.Run(() =>
    {
        Put("Sub/Inner", new ApiRequest { Method = "DELETE" });
        Put("Top", new ApiRequest { Kind = ApiKind.Grpc });
        Put("Options", new ApiRequest { Method = "options" });
        var vm = New();

        Assert.False(vm.IsEmpty);
        Assert.Equal("Inner", vm.Selected!.Name);
        Assert.True(vm.HasRequest);
        Assert.Equal("DEL", Node(vm, "Inner").Badge);
        Assert.Equal("DELETE", Node(vm, "Inner").Method);
        Assert.Equal("gRPC", Node(vm, "Top").Badge);
        Assert.Equal("OPT", Node(vm, "Options").Badge);
        Assert.True(Node(vm, "Sub").IsFolder);
        Assert.False(Node(vm, "Sub").IsRequest);
        Assert.Equal(["(Top level)", "Sub"], vm.FolderChoices.Select(c => c.ToString()));
    });

    [Fact]
    public Task The_request_open_last_is_opened_again() => Headless.Run(() =>
    {
        Put("A");
        Put("B");
        var vm = New();
        vm.Selected = Node(vm, "B");

        Assert.Equal("B", New().Selected!.Name);
    });

    [Fact]
    public Task A_damaged_note_of_what_was_open_is_ignored() => Headless.Run(() =>
    {
        Put("A");
        Data.Write("api-state.json", "{ not json");

        Assert.Equal("A", New().Selected!.Name);
    });

    [Fact]
    public Task A_note_that_cannot_be_written_is_ignored() => Headless.Run(() =>
    {
        Put("A");
        Directory.CreateDirectory(Data.At("api-state.json"));

        Assert.Equal("A", New().Selected!.Name);
    });

    [Fact]
    public Task The_tree_cannot_clear_a_selection_still_there() => Headless.Run(() =>
    {
        Put("A");
        var vm = New();
        var a = vm.Selected;

        vm.TreeSelection = null;
        Assert.Same(a, vm.TreeSelection);

        vm.Selected = null;
        vm.TreeSelection = null;
        Assert.Null(vm.Selected);

        vm.TreeSelection = a;
        Assert.Same(a, vm.Selected);
    });

    [Fact]
    public Task A_folder_selected_says_what_it_holds_and_cannot_go_inside_itself() => Headless.Run(() =>
    {
        Put("Folder/Inner/Deep");
        Put("Other/Thing");
        var vm = New();

        var folder = Node(vm, "Folder");
        folder.IsExpanded = false;
        vm.Selected = folder;
        Assert.True(vm.IsFolderSelected);
        Assert.False(vm.HasRequest);
        Assert.Equal("1 request", vm.FolderSummary);
        Assert.Equal("Delete the folder Folder and the 1 request in it? The files are deleted.", vm.DeleteText);
        Assert.Equal(["(Top level)", "Other"], vm.FolderChoices.Select(c => c.Label));

        Put("Folder/Second");
        vm.RefreshCommand.Execute(null);
        Assert.Equal("2 requests", vm.FolderSummary);
        Assert.False(Node(vm, "Folder").IsExpanded);

        vm.Selected = Node(vm, "Deep");
        Assert.True(Node(vm, "Folder").IsExpanded);
        Assert.Equal(["Folder", "Inner"], Node(vm, "Deep").Ancestors().Select(a => a.Name));
        Assert.Contains("    Inner", vm.FolderChoices.Select(c => c.Label));
    });

    [Fact]
    public Task New_requests_go_in_the_selected_folder_or_the_folder_of_the_selected_request() => Headless.Run(() =>
    {
        var vm = New();

        vm.NewRestRequestCommand.Execute(null);
        Assert.Equal("New request", vm.Selected!.Name);
        Assert.Equal("{{baseUrl}}/", vm.Request!.Url);
        Assert.Equal(Workspace, Path.GetDirectoryName(vm.Selected.Path));

        vm.NewFolderCommand.Execute(null);
        Assert.Equal("New folder", vm.Selected!.Name);

        vm.NewGrpcRequestCommand.Execute(null);
        Assert.Equal("New gRPC request", vm.Selected!.Name);
        Assert.Equal("{{grpcAddress}}", vm.Request!.Address);
        Assert.Equal(Data.At("workspace", "New folder"), Path.GetDirectoryName(vm.Selected.Path));

        vm.NewRestRequestCommand.Execute(null);
        Assert.Equal(Data.At("workspace", "New folder"), Path.GetDirectoryName(vm.Selected!.Path));
    });

    [Fact]
    public Task A_request_that_cannot_be_made_is_reported() => Headless.Run(() =>
    {
        var vm = New();
        Directory.Delete(Workspace, recursive: true);
        Data.Write("workspace", "a file in the way");

        vm.NewRestRequestCommand.Execute(null);

        Assert.NotNull(vm.Error);
        File.Delete(Workspace);
    });

    [Fact]
    public Task A_request_can_be_duplicated() => Headless.Run(() =>
    {
        Put("A", new ApiRequest { Url = "http://x" });
        var vm = New();

        vm.DuplicateCommand.Execute(null);

        Assert.Equal("A copy", vm.Selected!.Name);
        Assert.Equal("http://x", vm.Request!.Url);

        vm.Selected = null;
        vm.DuplicateCommand.Execute(null);
        Assert.Null(vm.Selected);
    });

    [Fact]
    public Task Requests_and_folders_can_be_renamed() => Headless.Run(() =>
    {
        Put("A");
        Put("Folder/Inner");
        var vm = New();

        vm.Selected = Node(vm, "A");
        vm.RenameCommand.Execute(null);
        Assert.Equal("A", vm.Selected!.Name);

        vm.EditName = "Renamed";
        vm.RenameCommand.Execute(null);
        Assert.Equal("Renamed", vm.Selected!.Name);
        Assert.Equal("Renamed", vm.EditName);

        vm.Selected = Node(vm, "Folder");
        vm.EditName = "Moved";
        vm.RenameCommand.Execute(null);
        Assert.Equal("Moved", vm.Selected!.Name);

        vm.Selected = null;
        vm.RenameCommand.Execute(null);
    });

    [Fact]
    public Task A_rename_that_fails_goes_back_to_the_old_name() => Headless.Run(() =>
    {
        var path = Put("A");
        var vm = New();
        File.Delete(path);

        vm.EditName = "Renamed";
        vm.RenameCommand.Execute(null);

        Assert.NotNull(vm.Error);
        Assert.Equal("A", vm.EditName);
    });

    [Fact]
    public Task Choosing_another_folder_moves_a_request_or_a_folder() => Headless.Run(() =>
    {
        Put("A");
        Put("Target/T");
        Put("Sub/S");
        var vm = New();

        vm.Selected = Node(vm, "A");
        vm.EditFolder = vm.FolderChoices.First(c => c.Label == "Target");
        Assert.Equal(Data.At("workspace", "Target"), Path.GetDirectoryName(vm.Selected!.Path));

        vm.Selected = Node(vm, "Sub");
        vm.EditFolder = vm.FolderChoices.First(c => c.Label == "Target");
        Assert.Equal(Data.At("workspace", "Target", "Sub"), vm.Selected!.Path);

        // Its own folder again is not a move.
        vm.EditFolder = vm.FolderChoices.First(c => c.Label == "Target");
        Assert.Equal(Data.At("workspace", "Target", "Sub"), vm.Selected!.Path);

        vm.EditFolder = null;
    });

    [Fact]
    public Task A_move_that_fails_is_reported() => Headless.Run(() =>
    {
        var path = Put("A");
        Put("Target/T");
        var vm = New();
        vm.Selected = Node(vm, "A");
        File.Delete(path);

        vm.EditFolder = vm.FolderChoices.First(c => c.Label == "Target");

        Assert.NotNull(vm.Error);
    });

    [Fact]
    public Task Delete_asks_first_and_can_be_cancelled() => Headless.Run(() =>
    {
        var vm = New();
        vm.DeleteCommand.Execute(null);
        Assert.False(vm.IsConfirmingDelete);
        vm.ConfirmDeleteCommand.Execute(null);

        Put("A");
        vm.RefreshCommand.Execute(null);
        Assert.Equal("Delete A? Its file is deleted.", vm.DeleteText);

        vm.DeleteCommand.Execute(null);
        Assert.True(vm.IsConfirmingDelete);
        vm.CancelDeleteCommand.Execute(null);
        Assert.False(vm.IsConfirmingDelete);
        Assert.True(File.Exists(vm.Selected!.Path));
    });

    [Fact]
    public Task Deleting_selects_the_next_or_the_one_before_or_the_folder() => Headless.Run(() =>
    {
        Put("F/A");
        Put("F/B");
        Put("F/C");
        var vm = New();

        vm.Selected = Node(vm, "A");
        vm.ConfirmDeleteCommand.Execute(null);
        Assert.Equal("B", vm.Selected!.Name);

        vm.Selected = Node(vm, "C");
        vm.ConfirmDeleteCommand.Execute(null);
        Assert.Equal("B", vm.Selected!.Name);

        vm.ConfirmDeleteCommand.Execute(null);
        Assert.Equal("F", vm.Selected!.Name);

        vm.ConfirmDeleteCommand.Execute(null);
        Assert.True(vm.IsEmpty);
    });

    [Fact]
    public Task A_delete_that_fails_is_reported() => Headless.Run(() =>
    {
        Put("Folder/A");
        var vm = New();
        vm.Selected = Node(vm, "Folder");
        Directory.Delete(vm.Selected!.Path, recursive: true);

        vm.ConfirmDeleteCommand.Execute(null);

        Assert.NotNull(vm.Error);
    });

    [Fact]
    public Task A_change_in_the_method_shows_in_the_tree() => Headless.Run(() =>
    {
        Put("A");
        var vm = New();

        vm.Request!.Method = "PATCH";

        Assert.Equal("PATCH", vm.Selected!.Badge);
    });

    [Fact]
    public Task The_helpers_are_looked_up_when_a_request_uses_them() => Headless.Run(async () =>
    {
        Put("A", new ApiRequest { Kind = ApiKind.Grpc });
        var vm = New();
        var request = vm.Request!;

        Assert.Empty(await request.PickFiles!(false));
        await request.CopyText!("x");

        var copied = new List<string>();
        vm.CopyText = text =>
        {
            copied.Add(text);
            return Task.CompletedTask;
        };
        vm.PickFiles = _ => Task.FromResult<IReadOnlyList<string>>(["picked"]);

        Assert.Equal(["picked"], await request.PickFiles(false));
        await request.CopyText("x");
        Assert.Equal(["x"], copied);
    });

    // ---- Environments ----

    [Fact]
    public Task Environments_are_read_with_the_one_in_use() => Headless.Run(() =>
    {
        new ApiWorkspace(Workspace).WriteEnvironments(new ApiEnvironments
        {
            Environments = [new ApiEnvironment { Name = "Dev", Variables = [new ApiPair { Name = "a", Value = "1" }] }, new ApiEnvironment { Name = "Prod" }],
            Selected = "Prod",
        });

        var vm = New();

        Assert.True(vm.HasEnvironments);
        Assert.Equal([ApiClientViewModel.NoEnvironment, "Dev", "Prod"], vm.EnvironmentChoices);
        Assert.Equal("Prod", vm.SelectedEnvironment);
        Assert.Equal("Prod", vm.EditedEnvironment!.Name);
        Assert.Equal("Prod", vm.EditedEnvironment.ToString());
    });

    [Fact]
    public Task Choosing_an_environment_is_saved_after_a_pause() => Headless.Run(async () =>
    {
        new ApiWorkspace(Workspace).WriteEnvironments(new ApiEnvironments { Environments = [new ApiEnvironment { Name = "Dev" }] });
        var vm = New();

        vm.SelectedEnvironment = "Dev";
        await Headless.Until(() => new ApiWorkspace(Workspace).ReadEnvironments().Selected == "Dev");

        vm.SelectedEnvironment = ApiClientViewModel.NoEnvironment;
        await Headless.Until(() => new ApiWorkspace(Workspace).ReadEnvironments().Selected == "");
    });

    [Fact]
    public Task A_list_clearing_its_choice_goes_back_to_no_environment() => Headless.Run(() =>
    {
        var vm = New();

        vm.SelectedEnvironment = null!;

        Assert.Equal(ApiClientViewModel.NoEnvironment, vm.SelectedEnvironment);
    });

    [Fact]
    public Task The_editor_opens_on_the_one_in_use_or_the_first() => Headless.Run(() =>
    {
        var vm = New();
        vm.IsChoosingWorkspace = true;

        vm.ToggleEnvironmentsCommand.Execute(null);

        Assert.True(vm.IsEditingEnvironments);
        Assert.False(vm.IsChoosingWorkspace);
        Assert.Null(vm.EditedEnvironment);
    });

    [Fact]
    public Task New_environments_get_a_free_name_and_the_usual_variables() => Headless.Run(() =>
    {
        var vm = New();

        vm.NewEnvironmentCommand.Execute(null);
        vm.NewEnvironmentCommand.Execute(null);
        vm.NewEnvironmentCommand.Execute(null);

        Assert.Equal(["Environment", "Environment 2", "Environment 3"], vm.Environments.Select(e => e.Name));
        Assert.Equal("Environment 3", vm.EditedEnvironment!.Name);
        Assert.Equal(["baseUrl", "grpcAddress"], vm.EditedEnvironment.Variables.Select(v => v.Name));
        Assert.Contains("Environment 2", vm.EnvironmentChoices);
    });

    [Fact]
    public Task Renaming_the_one_in_use_loses_the_choice_unless_it_stays_the_same() => Headless.Run(() =>
    {
        var vm = New();
        vm.NewEnvironmentCommand.Execute(null);
        vm.SelectedEnvironment = "Environment";

        vm.Environments[0].Variables[0].Value = "changed";
        Assert.Equal("Environment", vm.SelectedEnvironment);

        vm.EditedEnvironment!.Name = "Renamed";

        Assert.Contains("Renamed", vm.EnvironmentChoices);
        Assert.Equal(ApiClientViewModel.NoEnvironment, vm.SelectedEnvironment);
    });

    [Fact]
    public Task Deleting_an_environment_moves_to_the_next() => Headless.Run(() =>
    {
        var vm = New();
        vm.DeleteEnvironmentCommand.Execute(null);

        vm.NewEnvironmentCommand.Execute(null);
        vm.NewEnvironmentCommand.Execute(null);
        vm.DeleteEnvironmentCommand.Execute(null);

        Assert.Equal("Environment", vm.EditedEnvironment!.Name);
        Assert.DoesNotContain("Environment 2", vm.EnvironmentChoices);
    });

    [Fact]
    public Task Requests_use_the_variables_of_the_environment_in_use_and_captures_are_saved_there() => Headless.Run(async () =>
    {
        await using var server = await FakeServer.StartAsync();
        var vm = New();
        vm.NewEnvironmentCommand.Execute(null);
        vm.SelectedEnvironment = "Environment";
        vm.EditedEnvironment!.Variables[0].Value = server.RestAddress;
        vm.EditedEnvironment.Variables[1].Enabled = false;
        vm.EditedEnvironment.Variables[1].Name = "token";
        vm.NewRestRequestCommand.Execute(null);
        vm.Request!.Url = "{{baseUrl}}/ok";
        vm.Request.Captures.AddCommand.Execute(null);
        vm.Request.Captures[0].Name = "token";
        vm.Request.Captures[0].Value = "token";
        vm.Request.Captures.AddCommand.Execute(null);
        vm.Request.Captures[1].Name = "method";
        vm.Request.Captures[1].Value = "method";

        await vm.Request.SendCommand.ExecuteAsync(null);

        Assert.True(vm.Request.Response!.IsSuccess);
        var variables = vm.EditedEnvironment.Variables;
        Assert.Equal("from the server", variables.Single(v => v.Name == "token").Value);
        Assert.True(variables.Single(v => v.Name == "token").Enabled);
        Assert.Contains(variables, v => v.Name == "method");

        vm.SelectedEnvironment = ApiClientViewModel.NoEnvironment;
        vm.Request.Url = server.RestAddress + "/ok";
        await vm.Request.SendCommand.ExecuteAsync(null);
        Assert.Contains("Choose an environment", vm.Request.Error);
    });

    // ---- The workspace folder ----

    [Fact]
    public Task The_folder_band_starts_from_the_current_folder() => Headless.Run(() =>
    {
        var vm = New();
        vm.IsEditingEnvironments = true;
        vm.WorkspaceMessage = "old";

        vm.ToggleWorkspaceCommand.Execute(null);

        Assert.True(vm.IsChoosingWorkspace);
        Assert.False(vm.IsEditingEnvironments);
        Assert.Equal(vm.Folder, vm.WorkspaceText);
        Assert.Null(vm.WorkspaceMessage);

        vm.UseDefaultWorkspaceCommand.Execute(null);
        Assert.Equal(vm.DefaultFolder, vm.WorkspaceText);
    });

    [Fact]
    public Task Browsing_uses_the_folder_picked_if_any() => Headless.Run(async () =>
    {
        var vm = New();
        vm.WorkspaceText = "before";

        await vm.BrowseWorkspaceCommand.ExecuteAsync(null);
        vm.PickFiles = _ => Task.FromResult<IReadOnlyList<string>>([]);
        await vm.BrowseWorkspaceCommand.ExecuteAsync(null);
        Assert.Equal("before", vm.WorkspaceText);

        vm.PickFiles = _ => Task.FromResult<IReadOnlyList<string>>(["picked"]);
        await vm.BrowseWorkspaceCommand.ExecuteAsync(null);
        Assert.Equal("picked", vm.WorkspaceText);
    });

    [Fact]
    public Task A_relative_folder_is_refused_and_the_same_one_just_closes_the_band() => Headless.Run(() =>
    {
        var vm = New();
        vm.IsChoosingWorkspace = true;

        vm.WorkspaceText = "some/relative";
        vm.ApplyWorkspaceCommand.Execute(null);
        Assert.Contains("whole path", vm.WorkspaceMessage);
        Assert.True(vm.IsChoosingWorkspace);

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        vm.WorkspaceText = "~/" + Path.GetRelativePath(home, vm.Folder);
        vm.ApplyWorkspaceCommand.Execute(null);
        Assert.False(vm.IsChoosingWorkspace);
    });

    [Fact]
    public Task A_blank_folder_means_the_default_one() => Headless.Run(() =>
    {
        var vm = new ApiClientViewModel(new ApiWorkspace(ApiWorkspace.DefaultFolder));
        vm.IsChoosingWorkspace = true;
        vm.WorkspaceText = "  ";

        vm.ApplyWorkspaceCommand.Execute(null);

        Assert.False(vm.IsChoosingWorkspace);
    });

    [Fact]
    public Task Requests_are_copied_to_a_folder_without_any() => Headless.Run(() =>
    {
        Put("A");
        var vm = New();
        var target = Data.At("elsewhere");
        vm.IsChoosingWorkspace = true;
        vm.WorkspaceText = target;

        vm.ApplyWorkspaceCommand.Execute(null);

        Assert.False(vm.IsChoosingWorkspace);
        Assert.Equal(target, vm.Folder);
        Assert.Contains("copied", vm.Message);
        Assert.Equal("A", vm.Selected!.Name);
        Assert.Equal(target, ApiWorkspace.ChosenFolder);
    });

    [Fact]
    public Task A_folder_holding_requests_is_opened_as_it_is() => Headless.Run(() =>
    {
        Put("A");
        var target = Data.At("elsewhere");
        ApiWorkspace.Write(Path.Combine(target, "B" + ApiWorkspace.RequestExtension), new ApiRequest());
        var vm = New();
        vm.WorkspaceText = target;

        vm.ApplyWorkspaceCommand.Execute(null);

        Assert.Contains("Opened", vm.Message);
        Assert.Equal("B", Assert.Single(vm.Roots).Name);
    });

    [Fact]
    public Task A_folder_that_cannot_be_used_says_so() => Headless.Run(() =>
    {
        var file = Data.Write("a-file", "");
        var vm = New();
        var before = vm.Folder;
        vm.WorkspaceText = Path.Combine(file, "below");

        vm.ApplyWorkspaceCommand.Execute(null);

        Assert.StartsWith("Could not use ", vm.WorkspaceMessage);
        Assert.Equal(before, vm.Folder);
    });

    [Fact]
    public Task Saving_state_writes_what_is_waiting() => Headless.Run(() =>
    {
        Put("A");
        var vm = New();
        vm.Request!.Url = "http://changed";
        vm.NewEnvironmentCommand.Execute(null);

        vm.SaveState();

        Assert.Equal("http://changed", ApiWorkspace.Read(vm.Selected!.Path).Url);
        Assert.Single(new ApiWorkspace(Workspace).ReadEnvironments().Environments);

        // With nothing waiting, nothing more is written.
        vm.SaveState();
    });
}
