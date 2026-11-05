using System;
using System.IO;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Mqtt.Tests;

public class FolderMessageFileTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void Writes_the_body_indented_in_the_folder_for_its_topic()
    {
        var path = FolderMessageFile.Create(_folder.Path, " /plant/line1/status/ ", "status.json", """{"mode":"run"}""");

        Assert.Equal(_folder.At("plant", "line1", "status", "status.json"), path);
        Assert.Equal("{\n  \"mode\": \"run\"\n}\n", File.ReadAllText(path).ReplaceLineEndings("\n"));
        var message = Assert.Single(FolderScanner.Scan(_folder.Path));
        Assert.Equal("plant/line1/status", message.Topic);
        Assert.True(message.IsValid);
    }

    [Theory]
    [InlineData("", "m", "{}")]
    [InlineData("a/+/b", "m", "{}")]
    [InlineData("a/#", "m", "{}")]
    [InlineData("a//b", "m", "{}")]
    [InlineData("a/../b", "m", "{}")]
    [InlineData("a", "", "{}")]
    [InlineData("a", "x/y", "{}")]
    [InlineData("a", ".hidden", "{}")]
    [InlineData("a", "m.send", "{}")]
    [InlineData("a", "m", "{ not json")]
    public void What_cannot_be_a_folder_message_is_refused(string topic, string name, string body)
    {
        Assert.Throws<FormatException>(() => FolderMessageFile.Create(_folder.Path, topic, name, body));
        Assert.Empty(Directory.GetFileSystemEntries(_folder.Path));
    }

    [Fact]
    public void Needs_a_folder_and_leaves_a_file_already_there()
    {
        Assert.Throws<FormatException>(() => FolderMessageFile.Create(null, "a", "m", "{}"));
        FolderMessageFile.Create(_folder.Path, "a", "m", """{"first":true}""");

        var problem = Assert.Throws<FormatException>(() => FolderMessageFile.Create(_folder.Path, "a", "m", "{}"));

        Assert.Contains("already there", problem.Message);
        Assert.Contains("first", File.ReadAllText(_folder.At("a", "m.json")));
    }

    [Fact]
    public void A_folder_that_cannot_be_made_is_reported()
    {
        File.WriteAllText(_folder.At("a"), "");

        Assert.Throws<FormatException>(() => FolderMessageFile.Create(_folder.Path, "a/b", "m", "{}"));
    }
}
