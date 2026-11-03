using System;
using System.IO;
using System.Linq;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Logs.Tests;

public sealed class SourceFolderTests : IDisposable
{
    private readonly TempFolder _repo = new();
    private readonly TempFolder _outside = new();

    public SourceFolderTests()
    {
        _repo.Write("src/Joktan.Mqtt/Client.cs", string.Join('\n', Enumerable.Range(1, 30).Select(i => $"line {i}")));
        _repo.Write("src/Joktan.Mqtt/bin/Debug/Client.cs", "built copy with Connect in it");
        _repo.Write("src/Joktan.Mqtt/Page.cs", "class Page { void Connect() { } }");
        _outside.Write("secret.txt", "Connect to the secret");
    }

    public void Dispose()
    {
        _repo.Dispose();
        _outside.Dispose();
    }

    private SourceFolder Folder => new(_repo.Path);

    [Theory]
    [InlineData("/home/builder/work/Joktan/src/Joktan.Mqtt/Client.cs")]
    [InlineData("C:\\agent\\_work\\1\\s\\src\\Joktan.Mqtt\\Client.cs")]
    [InlineData("src/Joktan.Mqtt/Client.cs")]
    public void Finds_a_file_named_by_where_the_app_was_built(string path)
    {
        Assert.Equal(_repo.At("src", "Joktan.Mqtt", "Client.cs"), Folder.Find(path));
    }

    [Fact]
    public void Finds_nothing_for_a_file_that_is_not_there()
    {
        Assert.Null(Folder.Find("/build/src/Other/Missing.cs"));
    }

    [Theory]
    [InlineData("../secret.txt")]
    [InlineData("src/../../secret.txt")]
    public void Never_resolves_outside_the_folder(string path)
    {
        Assert.Null(Folder.Resolve(path));
        Assert.Null(Folder.Resolve(_outside.At("secret.txt")));
        Assert.Throws<UnauthorizedAccessException>(() => Folder.Read(path, 1, 10));
    }

    [Fact]
    public void Does_not_follow_a_link_out_of_the_folder()
    {
        File.CreateSymbolicLink(_repo.At("src", "leak.txt"), _outside.At("secret.txt"));
        Directory.CreateSymbolicLink(_repo.At("src", "elsewhere"), _outside.Path);

        Assert.False(Folder.IsInside(_repo.At("src", "leak.txt")));
        Assert.False(Folder.IsInside(_repo.At("src", "elsewhere", "secret.txt")));
        Assert.Null(Folder.Find("/build/src/leak.txt"));
        Assert.DoesNotContain(Folder.Search("secret", null, 10, TestContext.Current.CancellationToken), m => m.Text.Contains("secret"));
    }

    [Fact]
    public void Reads_the_lines_asked_for_and_says_how_many_there_are()
    {
        var (lines, total) = Folder.Read("src/Joktan.Mqtt/Client.cs", 28, 10);

        Assert.Equal(30, total);
        Assert.Equal(["line 28", "line 29", "line 30"], lines.Select(l => l.Text));
        Assert.Equal(28, lines[0].Number);
    }

    [Fact]
    public void Searches_the_code_ignoring_case_and_build_output()
    {
        var matches = Folder.Search("connect", "*.cs", 10, TestContext.Current.CancellationToken);

        var match = Assert.Single(matches);
        Assert.Equal(("src/Joktan.Mqtt/Page.cs", 1), (match.File, match.Line));
    }

    [Fact]
    public void Marks_the_line_of_a_frame_among_those_around_it()
    {
        var (lines, _) = Folder.Read("src/Joktan.Mqtt/Client.cs", 8, 3);

        Assert.Equal("   8  line 8\n→  9  line 9\n  10  line 10", SourceFolder.Around(lines, 9).ReplaceLineEndings("\n"));
    }
}
