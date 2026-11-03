using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Joktan.Agent.Mcp;
using Joktan.Agent.Tools;
using Joktan.Core;
using Joktan.Logs;
using Joktan.Logs.ViewModels;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Agent.Tests.Tools;

[Collection(ProcessStateCollection.Name)]
public sealed class LogToolsTests : DataFolderTest
{
    private readonly TempFolder _logs = new();
    private readonly TempFolder _code = new();
    private readonly ErrorsViewModel _page;

    public LogToolsTests()
    {
        var now = DateTimeOffset.Now;
        _logs.Write("shop.log", $"""
            {now.AddMinutes(-5):yyyy-MM-ddTHH:mm:sszzz} ERROR app=shop module=cart Cart failed
            System.InvalidOperationException: empty
               at Shop.Cart.Add() in /ci/src/Cart.cs:line 2
            {now.AddMinutes(-4):yyyy-MM-ddTHH:mm:sszzz} WARN app=shop module=cart Slow
            """);
        _code.Write("src/Cart.cs", "class Cart\n{\n    void Add() { }\n}");
        var settings = new LogSourcesViewModel(LogSourceSettings.Load(), TelemetrySettings.Load(), new FakeCredentialStore());
        settings.AddFilesCommand.Execute(null);
        var shop = settings.Sources[^1];
        shop.NameText = "Shop";
        shop.PathText = _logs.Path;
        shop.AddFolderCommand.Execute(null);
        shop.Folders[0].App = "shop";
        shop.Folders[0].FolderText = _code.Path;
        _page = new ErrorsViewModel(settings);
    }

    protected override void Dispose(bool disposing)
    {
        _logs.Dispose();
        _code.Dispose();
        base.Dispose(disposing);
    }

    private async Task<string> Call(string name, string arguments = "{}")
    {
        var tool = LogTools.Create(_page).Single(t => t.Name == name);
        Assert.Equal(ToolRisk.Read, tool.Risk);
        using var json = JsonDocument.Parse(arguments);
        return JsonSerializer.Serialize(await tool.RunAsync(new ToolArgs(json.RootElement.Clone())));
    }

    [Fact]
    public async Task Searches_the_named_source_as_the_page_does()
    {
        var found = await Call("logs_errors", """{"source":"shop","window":"24h","levels":"warning, error"}""");

        Assert.Equal("Shop", _page.SelectedSource!.DisplayName);
        Assert.Equal(TimeWindow.Last24Hours, _page.SelectedWindow.Window);
        Assert.Equal(2, _page.Groups.Count);
        Assert.Contains("\"Type\":\"System.InvalidOperationException\"", found);
        Assert.Contains("\"Location\":\"src/Cart.cs:2\",\"InCode\":true", found);
    }

    [Fact]
    public async Task Refuses_what_it_does_not_know()
    {
        await Assert.ThrowsAsync<ToolFailure>(() => Call("logs_errors", """{"source":"nowhere"}"""));
        await Assert.ThrowsAsync<ToolFailure>(() => Call("logs_errors", """{"source":"Shop","window":"2y"}"""));
        await Assert.ThrowsAsync<ToolFailure>(() => Call("logs_errors", """{"source":"Shop","levels":"debug"}"""));
        await Assert.ThrowsAsync<ToolFailure>(() => Call("logs_errors", """{"source":"Shop","from":"soon"}"""));
    }

    [Fact]
    public async Task Reads_and_searches_the_apps_code_but_nothing_outside_it()
    {
        var read = await Call("source_read", """{"source":"Shop","app":"shop","path":"/ci/src/Cart.cs","from":2,"count":2}""");
        var search = await Call("source_search", """{"source":"Shop","text":"add"}""");

        Assert.Contains("\"File\":\"src/Cart.cs\",\"Lines\":4", read);
        Assert.Contains("2: {", read);
        Assert.Contains("src/Cart.cs:3: void Add() { }", search);
        await Assert.ThrowsAsync<ToolFailure>(() => Call("source_read", """{"source":"Shop","path":"../../etc/passwd"}"""));
        await Assert.ThrowsAsync<ToolFailure>(() => Call("source_read", """{"source":"Joktan (log files)","path":"a.cs"}"""));
    }

    [Fact]
    public async Task Lists_the_sources_and_their_folders()
    {
        var sources = await Call("logs_sources");

        Assert.Contains("\"Name\":\"Joktan (Elasticsearch)\"", sources);
        Assert.Contains($"\"App\":\"shop\",\"Folder\":{JsonSerializer.Serialize(_code.Path)},\"Exists\":true", sources);
    }
}
