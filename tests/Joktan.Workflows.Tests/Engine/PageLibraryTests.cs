using System.IO;
using System.Linq;
using Joktan.Tests.TestSupport;
using Joktan.Workflows.Engine;
using Joktan.Workflows.Model;
using Xunit;

namespace Joktan.Workflows.Tests.Engine;

public class PageLibraryTests
{
    private static WorkflowPage Page(string name) => new()
    {
        Id = WorkflowLibrary.NewId(),
        Name = name,
        Items = [new PageItem { Type = PageItemType.CheckBox, Name = "approved", Required = true }],
    };

    [Fact]
    public void A_saved_page_is_a_file_that_is_read_back_and_renamed_with_the_page()
    {
        using var folder = new TempFolder();
        var library = new PageLibrary(folder.Path);
        var page = Page("Approve order");
        library.Save(page);
        Assert.True(File.Exists(folder.At("Approve order.page.json")));

        page.Name = "Approve";
        library.Save(page);
        Assert.False(File.Exists(folder.At("Approve order.page.json")));

        var again = new PageLibrary(folder.Path);
        var read = Assert.Single(again.All);
        Assert.Equal(page.Id, read.Id);
        Assert.Equal("Approve", read.Name);
        Assert.Equal(PageItemType.CheckBox, read.Items.Single().Type);
        Assert.True(read.Items.Single().Required);
        Assert.Same(read, again.Find("approve"));
        Assert.Same(read, again.Find(page.Id));
    }

    [Fact]
    public void Deleting_a_page_removes_its_file_and_names_are_kept_free()
    {
        using var folder = new TempFolder();
        var library = new PageLibrary(folder.Path);
        var page = Page("New page");
        library.Save(page);
        Assert.Equal("New page 2", library.FreeName("New page"));

        library.Delete(page.Id);
        Assert.Empty(library.All);
        Assert.Empty(Directory.GetFiles(folder.Path));
    }

    [Fact]
    public void Without_a_folder_pages_are_kept_in_memory()
    {
        var library = new PageLibrary(null);
        var changes = 0;
        library.Changed += () => changes++;
        library.Save(Page("Only here"));
        Assert.Single(library.All);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void A_question_without_a_page_is_shown_with_its_message_and_a_box_for_each_field()
    {
        var page = WorkflowPage.For(new PendingInput
        {
            Message = "Approve?",
            Fields = [new InputField { Name = "ok", Label = "OK", Default = "true" }],
        });
        Assert.Equal("Approve?", page.Title);
        var item = Assert.Single(page.Inputs);
        Assert.Equal(PageItemType.TextBox, item.Type);
        Assert.Equal(("ok", "OK", "true"), (item.Name, item.Label, item.Default));
    }
}
