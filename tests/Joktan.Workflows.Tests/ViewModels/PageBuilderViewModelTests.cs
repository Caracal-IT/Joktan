using System.IO;
using System.Linq;
using Joktan.Tests.TestSupport;
using Joktan.Workflows.Engine;
using Joktan.Workflows.Model;
using Joktan.Workflows.ViewModels;
using Xunit;

namespace Joktan.Workflows.Tests.ViewModels;

public class PageBuilderViewModelTests
{
    private readonly TempFolder _folder = new();

    private PageLibrary Library { get; }

    public PageBuilderViewModelTests() => Library = new PageLibrary(_folder.Path);

    private PageItemTypeChoice Choice(PageItemType type) => new PageBuilderViewModel(new PageLibrary(null)).ShownTypes
        .Concat(new PageBuilderViewModel(new PageLibrary(null)).InputTypes).Single(c => c.Type == type);

    [Fact]
    public void A_new_page_is_saved_with_a_sample_and_items_are_added_under_the_one_selected()
    {
        var vm = new PageBuilderViewModel(Library);
        Assert.True(vm.HasNoPages);
        vm.NewPageCommand.Execute(null);

        var page = Assert.Single(Library.All);
        Assert.Same(page, vm.SelectedPage);
        Assert.Equal(3, vm.Items.Count);
        Assert.True(File.Exists(_folder.At("New page.page.json")));

        vm.SelectedItem = vm.Items[0];
        vm.AddItemCommand.Execute(Choice(PageItemType.DropDown));
        Assert.Equal(PageItemType.DropDown, page.Items[1].Type);
        Assert.Equal("field", page.Items[1].Name);
        Assert.Equal(2, page.Items[1].Options.Count);
        Assert.Same(vm.Items[1], vm.SelectedItem);

        vm.AddItemCommand.Execute(Choice(PageItemType.Heading));
        Assert.Equal("", page.Items[2].Name);
        Assert.Equal(5, PageLibrary.TryRead(_folder.At("New page.page.json"))!.Items.Count);
    }

    [Fact]
    public void Items_move_and_are_removed_and_every_change_is_saved()
    {
        var vm = new PageBuilderViewModel(Library);
        vm.NewPageCommand.Execute(null);
        var page = vm.SelectedPage!;
        var first = page.Items[0];

        vm.SelectedItem = vm.Items[0];
        Assert.False(vm.MoveUpCommand.CanExecute(null));
        vm.MoveDownCommand.Execute(null);
        Assert.Same(first, page.Items[1]);
        Assert.Same(first, vm.SelectedItem!.Item);

        vm.SelectedItem.Text = "Order {{input.order}}";
        vm.RemoveItemCommand.Execute(null);
        Assert.DoesNotContain(first, page.Items);
        Assert.Equal(2, vm.Items.Count);

        vm.PageTitle = "Approve the order";
        vm.PageName = "Approve";
        var saved = PageLibrary.TryRead(_folder.At("Approve.page.json"))!;
        Assert.Equal("Approve the order", saved.Title);
        Assert.Equal(2, saved.Items.Count);

        // A page keeps a name to be found by.
        vm.PageName = " ";
        Assert.Equal("Approve", page.Name);
    }

    [Fact]
    public void Options_are_one_to_a_line_and_problems_are_shown()
    {
        var vm = new PageBuilderViewModel(Library);
        vm.NewPageCommand.Execute(null);
        Assert.Null(vm.Problem);
        vm.AddItemCommand.Execute(Choice(PageItemType.DropDown));
        var dropDown = vm.SelectedItem!;

        dropDown.OptionsText = "Red\r\nBlue\n";
        Assert.Equal(["Red", "Blue"], dropDown.Item.Options);
        Assert.Equal("Red\r\nBlue\n", dropDown.OptionsText);

        dropDown.OptionsText = "";
        Assert.Contains("no options", vm.Problem);
        dropDown.OptionsText = "Red";
        dropDown.Name = "approved";
        Assert.Contains("Two fields are called approved", vm.Problem);
        dropDown.Name = "1st";
        Assert.Contains("cannot be a field name", vm.Problem);
        dropDown.Name = "";
        Assert.Contains("field name", vm.Problem);
    }

    [Fact]
    public void Deleting_asks_first_and_a_copy_gets_a_name_of_its_own()
    {
        var vm = new PageBuilderViewModel(Library);
        vm.NewPageCommand.Execute(null);
        vm.DuplicateCommand.Execute(null);
        Assert.Equal(["New page", "New page copy"], vm.Pages.Select(p => p.Name));
        Assert.Equal("New page copy", vm.SelectedPage!.Name);

        vm.AskDeleteCommand.Execute(null);
        Assert.True(vm.IsConfirmingDelete);
        vm.ConfirmDeleteCommand.Execute(null);
        Assert.Equal(["New page"], vm.Pages.Select(p => p.Name));
        Assert.Equal("New page", vm.SelectedPage!.Name);
    }

    [Fact]
    public void The_preview_opens_the_page_on_the_website_while_it_is_on()
    {
        string? opened = null;
        var url = (string?)null;
        var vm = new PageBuilderViewModel(Library) { WebsiteUrl = () => url, OpenUrl = u => { opened = u; return System.Threading.Tasks.Task.CompletedTask; } };
        vm.NewPageCommand.Execute(null);
        vm.PreviewCommand.Execute(null);
        Assert.Null(opened);
        Assert.Contains("Settings / Website", vm.PreviewUrlText);

        url = "http://localhost:47822";
        vm.PreviewCommand.Execute(null);
        Assert.Equal($"http://localhost:47822/preview/{vm.SelectedPage!.Id}", opened);
    }
}
