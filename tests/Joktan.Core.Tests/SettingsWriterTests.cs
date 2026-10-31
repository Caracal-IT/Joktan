using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Core.Tests;

public class SettingsWriterTests : IDisposable
{
    private readonly TempFolder _folder = new();
    private readonly List<(string Path, Exception Error)> _failures = [];
    private readonly SettingsWriter _writer;

    public SettingsWriterTests()
    {
        _writer = new SettingsWriter((path, e) =>
        {
            lock (_failures)
                _failures.Add((path, e));
        });
    }

    public void Dispose() => _folder.Dispose();

    [Fact]
    public async Task Writes_the_content_to_the_file()
    {
        var path = _folder.At("a.json");

        _writer.Write(path, "content");
        await _writer.FlushAsync();

        Assert.Equal("content", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        Assert.Empty(_failures);
    }

    [Fact]
    public async Task The_last_write_of_a_file_wins()
    {
        var path = _folder.At("a.json");

        for (var i = 0; i <= 200; i++)
            _writer.Write(path, i.ToString(CultureInfo.InvariantCulture));
        await WaitUntilIdleAsync();

        Assert.Equal("200", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Writes_several_files()
    {
        var paths = Enumerable.Range(0, 20).Select(i => _folder.At($"{i}.json")).ToList();

        foreach (var path in paths)
            _writer.Write(path, Path.GetFileName(path));
        await WaitUntilIdleAsync();

        foreach (var path in paths)
            Assert.Equal(Path.GetFileName(path), await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Reports_a_file_that_cannot_be_written_and_carries_on()
    {
        var bad = _folder.At("missing", "a.json");
        var good = _folder.At("b.json");

        _writer.Write(bad, "x");
        _writer.Write(good, "y");
        await WaitUntilIdleAsync();

        var failure = Assert.Single(_failures);
        Assert.Equal(Path.GetFullPath(bad), failure.Path);
        Assert.IsAssignableFrom<IOException>(failure.Error);
        Assert.Equal("y", await File.ReadAllTextAsync(good, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Knows_what_it_wrote_recently()
    {
        var path = _folder.At("a.json");
        Assert.False(_writer.WroteRecently(path));

        _writer.Write(path, "x");
        await _writer.FlushAsync();

        Assert.True(_writer.WroteRecently(path));
        Assert.False(_writer.WroteRecently(_folder.At("other.json")));
    }

    [Fact]
    public async Task Is_not_busy_once_everything_is_written()
    {
        Assert.False(_writer.IsBusy);

        _writer.Write(_folder.At("a.json"), "x");
        await WaitUntilIdleAsync();

        Assert.False(_writer.IsBusy);
    }

    [Fact]
    public async Task Flushing_with_nothing_queued_completes()
    {
        await _writer.FlushAsync();
    }

    // FlushAsync covers what was queued when it was called; a worker may take later writes too.
    private async Task WaitUntilIdleAsync()
    {
        do
            await _writer.FlushAsync();
        while (_writer.IsBusy);
    }
}
