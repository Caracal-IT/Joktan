using System.Linq;
using Joktan.Passwords.ViewModels;
using Xunit;

namespace Joktan.Passwords.Tests.ViewModels;

public class PasswordNodeViewModelTests
{
    private static PasswordNodeViewModel Tree() => new(new PasswordFolder
    {
        Folders =
        [
            new PasswordFolder
            {
                Name = "Work",
                Folders = [new PasswordFolder { Name = "Servers", Entries = [new PasswordEntry { Title = "Deep" }] }],
            },
            new PasswordFolder { Name = "Bank", IsExpanded = false },
        ],
        Entries =
        [
            new PasswordEntry { Title = "zebra", Username = "zed" },
            new PasswordEntry { Title = "Apple", Url = "https://apple.example", Notes = "fruit" },
        ],
    }, null);

    [Fact]
    public void Folders_come_first_then_entries_each_by_name()
    {
        Assert.Equal(["Bank", "Work", "Apple", "zebra"], Tree().Children.Select(c => c.Name));
    }

    [Fact]
    public void Sort_puts_a_renamed_node_back_in_order()
    {
        var root = Tree();
        root.Children.Single(c => c.Name == "Apple").Entry!.Title = "zzz";

        root.Sort();

        Assert.Equal(["Bank", "Work", "zebra", "zzz"], root.Children.Select(c => c.Name));
    }

    [Fact]
    public void Knows_what_it_is_and_where_it_sits()
    {
        var root = Tree();
        var work = root.Children.Single(c => c.Name == "Work");
        var deep = work.Children[0].Children[0];

        Assert.True(work.IsFolder);
        Assert.False(work.IsEntry);
        Assert.True(deep.IsEntry);
        Assert.False(deep.IsFolder);
        Assert.Equal("Work / Servers", deep.Path);
        Assert.Equal("", work.Path);
        Assert.Equal(["Servers", "Work"], deep.Ancestors().Select(a => a.Name));
        Assert.Equal(4, work.SelfAndDescendants().Count() + 1);
    }

    [Fact]
    public void A_blank_name_shows_as_untitled_and_only_entries_have_detail()
    {
        var root = new PasswordNodeViewModel(new PasswordFolder { Name = "  " }, null);
        var entry = new PasswordNodeViewModel(new PasswordEntry { Username = "me" }, root);

        Assert.Equal(PasswordNodeViewModel.Untitled, root.Name);
        Assert.Equal(PasswordNodeViewModel.Untitled, entry.Name);
        Assert.Equal("", root.Detail);
        Assert.Equal("me", entry.Detail);
    }

    [Fact]
    public void Expanding_is_kept_in_the_folder_but_means_nothing_for_an_entry()
    {
        var root = Tree();
        var bank = root.Children.Single(c => c.Name == "Bank");
        Assert.False(bank.IsExpanded);

        bank.IsExpanded = true;
        Assert.True(bank.Folder!.IsExpanded);

        var entry = root.Children.First(c => c.IsEntry);
        entry.IsExpanded = true;
        Assert.Null(entry.Folder);
    }

    [Fact]
    public void Refresh_announces_the_name_and_detail()
    {
        var node = Tree().Children.First();
        var changed = new System.Collections.Generic.List<string?>();
        node.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        node.Refresh();

        Assert.Equal(["Name", "Detail"], changed);
    }

    [Theory]
    [InlineData("ZEB", true)]
    [InlineData("zed", true)]
    [InlineData("apple.example", true)]
    [InlineData("FRUIT", true)]
    [InlineData("nothing", false)]
    public void Search_looks_through_entries_only(string text, bool found)
    {
        var root = Tree();

        Assert.Equal(found, root.SelfAndDescendants().Any(n => n.Matches(text)));
        Assert.False(root.Children.Single(c => c.Name == "Work").Matches("Work"));
    }

    [Fact]
    public void A_folder_choice_shows_its_label()
    {
        Assert.Equal("  Work", new PasswordFolderChoice(Tree(), "  Work").ToString());
        Assert.Equal("Never", new LockChoice(0, "Never").ToString());
    }
}
