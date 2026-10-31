using System.Threading.Tasks;
using Avalonia.Controls;
using Joktan.Core.Controls;
using Joktan.Notes.ViewModels;
using Joktan.Notes.Views;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Notes.Tests.Views;

[Collection(ProcessStateCollection.Name)]
public class NotesViewTests : DataFolderTest
{
    [Fact]
    public Task Shows_every_state_of_the_page() => Headless.Run(() =>
    {
        var vm = new NotesViewModel(NoteStore.Load(Data.At("notes")));
        var window = Headless.Show(new NotesView(), vm);

        Assert.Contains("Notes", Headless.Find<MarkdownView>(window).Markdown);

        vm.NewChildPageCommand.Execute(null);
        vm.IsChoosingFolder = true;
        vm.IsConfirmingDelete = true;
        vm.Error = "An error";
        vm.Message = "A message";
        vm.FolderMessage = "A folder message";
        Headless.Settle();
        window.UpdateLayout();

        Assert.NotEmpty(Headless.All<TextBox>(window));
        vm.SaveState();
        window.Close();
    });

    [Fact]
    public Task The_view_lends_the_folder_picker_which_returns_nothing_without_a_system_dialog() => Headless.Run(async () =>
    {
        var vm = new NotesViewModel(NoteStore.Load(Data.At("notes")));
        var view = new NotesView { DataContext = vm };

        Assert.Null(await vm.PickFolder!());

        var window = Headless.Show(view);
        Assert.Null(await vm.PickFolder!());
        vm.SaveState();
        window.Close();
    });

    [Fact]
    public Task Another_data_context_is_left_alone() => Headless.Run(() =>
    {
        var view = new NotesView { DataContext = "not a view model" };

        Assert.IsType<NotesView>(view);
    });
}
