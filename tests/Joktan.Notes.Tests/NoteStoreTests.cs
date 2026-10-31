using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Notes.Tests;

public class NoteStoreTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public void A_folder_without_an_index_is_new()
    {
        var store = NoteStore.Load(_folder.At("notes"));

        Assert.True(store.IsNew);
        Assert.Empty(store.Pages);
        Assert.Equal("", store.Selected);
        Assert.False(NoteStore.HasNotes(_folder.At("notes")));
    }

    [Fact]
    public async Task The_index_is_written_and_read_back()
    {
        var folder = _folder.At("notes");
        var store = NoteStore.Load(folder);
        var child = new NotePage { Title = "Child" };
        var page = new NotePage { Title = "Page", IsExpanded = false, Children = [child] };

        store.SaveIndex([page], child.Id);
        await store.FlushAsync();
        var loaded = NoteStore.Load(folder);

        Assert.False(store.IsNew);
        Assert.False(loaded.IsNew);
        Assert.True(NoteStore.HasNotes(folder));
        Assert.Equal(child.Id, loaded.Selected);
        var read = Assert.Single(loaded.Pages);
        Assert.Equal(("Page", false, page.Id), (read.Title, read.IsExpanded, read.Id));
        Assert.Equal("Child", Assert.Single(read.Children).Title);
    }

    [Fact]
    public async Task A_page_text_is_written_and_read_back()
    {
        var store = NoteStore.Load(_folder.At("notes"));

        store.WriteText("abc", "# Hello");
        await store.FlushAsync();

        Assert.Equal("# Hello", store.ReadText("abc"));
        Assert.Equal(_folder.At("notes", "abc.md"), store.PagePath("abc"));
        Assert.Equal("", store.ReadText("missing"));
    }

    [Fact]
    public void An_unreadable_index_gives_no_notes_and_leaves_the_pages_alone()
    {
        _folder.Write("index.json", "not json");
        var page = _folder.Write("abc.md", "text");

        var store = NoteStore.Load(_folder.Path);

        Assert.Empty(store.Pages);
        Assert.False(store.IsNew);
        Assert.True(File.Exists(page));
    }

    [Fact]
    public void Tidies_what_it_reads_so_IDs_are_safe_as_file_names_and_used_once()
    {
        _folder.Write("index.json", """
            {"Pages":[
              null,
              {"Id":"good","Title":"a"},
              {"Id":"GOOD","Title":"same id in another case"},
              {"Id":"../evil","Title":"path"},
              {"Id":"","Title":null,"Children":[{"Id":"good"},null]}
            ],"Selected":null}
            """);

        var store = NoteStore.Load(_folder.Path);

        Assert.Equal(4, store.Pages.Count);
        Assert.Equal("", store.Selected);
        Assert.Equal("good", store.Pages[0].Id);
        var ids = store.Pages.Concat(store.Pages[3].Children).Select(p => p.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(ids, id => Assert.True(id.All(char.IsAsciiLetterOrDigit), id));
        Assert.Equal("", store.Pages[3].Title);
        Assert.Single(store.Pages[3].Children);
    }

    [Fact]
    public async Task Deleting_removes_the_files_even_with_a_write_queued()
    {
        var store = NoteStore.Load(_folder.Path);
        store.WriteText("gone", "x");
        store.WriteText("kept", "y");

        store.DeleteText(["gone", "missing"]);

        // The deleting happens in the background once the writes are done, with nothing to wait on.
        await store.FlushAsync();
        var path = store.PagePath("gone");
        for (var i = 0; i < 500 && File.Exists(path); i++)
            await Task.Delay(10, Cancel);
        Assert.False(File.Exists(path));
        Assert.True(File.Exists(store.PagePath("kept")));
    }

    [Fact]
    public async Task Copying_takes_the_index_and_pages_and_keeps_what_is_there()
    {
        var store = NoteStore.Load(_folder.At("notes"));
        var child = new NotePage { Title = "Child" };
        var page = new NotePage { Title = "Page", Children = [child] };
        store.WriteText(page.Id, "page");
        store.WriteText(child.Id, "child");
        store.SaveIndex([page], "");
        _folder.Write("target/" + child.Id + ".md", "already here");
        _folder.Write("notes/stray.md", "not in the index");

        await store.CopyToAsync(_folder.At("target"));

        Assert.True(File.Exists(_folder.At("target", "index.json")));
        Assert.Equal("page", await File.ReadAllTextAsync(_folder.At("target", page.Id + ".md"), Cancel));
        Assert.Equal("already here", await File.ReadAllTextAsync(_folder.At("target", child.Id + ".md"), Cancel));
        Assert.False(File.Exists(_folder.At("target", "stray.md")));
    }

    [Fact]
    public void A_folder_that_cannot_be_made_is_reported()
    {
        var blocker = _folder.Write("file", "x");
        var store = NoteStore.Load(Path.Combine(blocker, "notes"));
        var failed = new List<string>();
        store.Failed += (path, _) => failed.Add(path);

        store.WriteText("a", "x");
        store.SaveIndex([], "");

        Assert.Equal(2, failed.Count);
    }

    [Fact]
    public void Compares_folders_whatever_their_ending()
    {
        Assert.True(NoteStore.SameFolder(_folder.Path, _folder.Path + Path.DirectorySeparatorChar));
        Assert.False(NoteStore.SameFolder(_folder.Path, _folder.At("other")));
    }
}

[Collection(ProcessStateCollection.Name)]
public class NoteStoreLocationTests : DataFolderTest
{
    [Fact]
    public void The_default_folder_is_in_the_data_folder()
    {
        Assert.Equal(Data.At("notes"), NoteStore.DefaultFolder);
        Assert.Equal(Data.At("notes"), NoteStore.ChosenFolder);
        Assert.Equal(Data.At("notes"), NoteStore.Load().Folder);
    }

    [Fact]
    public void Remembers_the_folder_chosen()
    {
        NoteStore.SaveChosenFolder("/some/where");

        Assert.Equal("/some/where", NoteStore.ChosenFolder);
    }

    [Fact]
    public void Choosing_the_default_forgets_the_choice()
    {
        NoteStore.SaveChosenFolder("/some/where");

        NoteStore.SaveChosenFolder(NoteStore.DefaultFolder);

        Assert.False(File.Exists(Data.At("notes-location.json")));
        Assert.Equal(NoteStore.DefaultFolder, NoteStore.ChosenFolder);
    }

    [Fact]
    public void A_choice_that_cannot_be_read_falls_back_to_the_default()
    {
        File.WriteAllText(Data.At("notes-location.json"), "not json");

        Assert.Equal(NoteStore.DefaultFolder, NoteStore.ChosenFolder);
    }
}
