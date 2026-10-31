using System.Linq;
using Xunit;

namespace Joktan.Passwords.Tests;

public class PasswordFolderTests
{
    [Fact]
    public void Lists_every_entry_below_it()
    {
        var root = new PasswordFolder
        {
            Entries = [new PasswordEntry { Title = "a" }],
            Folders =
            [
                new PasswordFolder
                {
                    Entries = [new PasswordEntry { Title = "b" }],
                    Folders = [new PasswordFolder { Entries = [new PasswordEntry { Title = "c" }] }],
                },
            ],
        };

        Assert.Equal(["a", "b", "c"], root.AllEntries().Select(e => e.Title));
    }

    [Fact]
    public void A_copy_has_the_same_IDs_and_changes_separately()
    {
        var entry = new PasswordEntry { Title = "a", Password = "p" };
        var inner = new PasswordFolder { Name = "inner", Entries = [entry], IsExpanded = false };
        var root = new PasswordFolder { Name = "root", Folders = [inner] };

        var copy = root.Copy();
        var copied = copy.AllEntries().Single();
        copied.Password = null;
        copy.Folders[0].Name = "changed";

        Assert.Equal(root.Id, copy.Id);
        Assert.Equal(inner.Id, copy.Folders[0].Id);
        Assert.False(copy.Folders[0].IsExpanded);
        Assert.Equal(entry.Id, copied.Id);
        Assert.Equal("p", entry.Password);
        Assert.Equal("inner", inner.Name);
    }

    [Fact]
    public void New_folders_and_entries_have_IDs_of_their_own()
    {
        Assert.NotEqual(new PasswordFolder().Id, new PasswordFolder().Id);
        Assert.NotEqual(new PasswordEntry().Id, new PasswordEntry().Id);
    }
}
