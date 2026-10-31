using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Notes.Tests;

public class NoteStoreFailureTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public void A_page_file_that_cannot_be_read_is_an_empty_page()
    {
        var store = NoteStore.Load(_folder.Path);
        Directory.CreateDirectory(store.PagePath("a"));

        Assert.Equal("", store.ReadText("a"));
    }

    [Fact]
    public async Task A_page_file_that_cannot_be_deleted_is_reported()
    {
        var store = NoteStore.Load(_folder.Path);
        var failed = new TaskCompletionSource<string>();
        store.Failed += (path, _) => failed.TrySetResult(path);
        Directory.CreateDirectory(store.PagePath("a"));
        _folder.Write(Path.GetFileName(store.PagePath("a")) + "/inside", "");

        store.DeleteText(["a"]);

        Assert.Equal(store.PagePath("a"), await failed.Task.WaitAsync(TimeSpan.FromSeconds(10), Cancel));
    }

    [Fact]
    public void An_index_that_cannot_be_read_counts_as_no_notes()
    {
        Directory.CreateDirectory(_folder.At("index.json"));

        Assert.Empty(NoteStore.Load(_folder.Path).Pages);
    }
}
