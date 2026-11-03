using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Joktan.Core;
using Joktan.Logs.ViewModels;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Logs.Tests.ViewModels;

[Collection(ProcessStateCollection.Name)]
public sealed class ErrorsViewModelTests : DataFolderTest
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 3, 12, 0, 0, TimeSpan.FromHours(2));

    private readonly TempFolder _logs = new();
    private readonly TempFolder _code = new();
    private readonly FakeCredentialStore _store = new();

    private const string Log = """
        2026-11-03T11:50:00+02:00 ERROR app=billing module=charge Charge 17 failed
        System.InvalidOperationException: The card was refused
           at Billing.Charges.Charge(Card card) in /ci/work/billing/src/Charges.cs:line 3
        2026-11-03T11:55:00+02:00 ERROR app=billing module=charge Charge 18 failed
        System.InvalidOperationException: The card was refused
           at Billing.Charges.Charge(Card card) in /ci/work/billing/src/Charges.cs:line 3
           at Billing.Host.Run() in /ci/work/billing/src/Host.cs:line 9
        2026-11-03T11:56:00+02:00 WARN app=billing module=charge Slow
        2026-11-03T09:00:00+02:00 ERROR app=billing module=charge Too early
        """;

    public ErrorsViewModelTests()
    {
        File.SetLastWriteTimeUtc(_logs.Write("billing.log", Log), Noon.UtcDateTime);
        _code.Write("src/Charges.cs", "class Charges\n{\n    void Charge() => throw new InvalidOperationException();\n}");
    }

    protected override void Dispose(bool disposing)
    {
        _logs.Dispose();
        _code.Dispose();
        base.Dispose(disposing);
    }

    private (ErrorsViewModel Page, LogSourcesViewModel Settings, LogSource Billing) Pages(string? folder = null)
    {
        var settings = new LogSourcesViewModel(LogSourceSettings.Load(), TelemetrySettings.Load(), _store);
        settings.AddFilesCommand.Execute(null);
        var source = settings.Sources[^1];
        source.NameText = "Billing";
        source.PathText = _logs.Path;
        if (folder is not null)
        {
            source.AddFolderCommand.Execute(null);
            source.Folders[0].App = "billing";
            source.Folders[0].FolderText = folder;
        }

        var page = new ErrorsViewModel(settings) { Now = () => Noon };
        page.SelectedSource = page.Sources.Single(s => s.DisplayName == "Billing");
        return (page, settings, source.Source);
    }

    [Fact]
    public async Task Finds_the_errors_in_the_window_and_groups_them()
    {
        var (page, _, _) = Pages();

        await page.SearchCommand.ExecuteAsync(null);

        var group = Assert.Single(page.Groups);
        Assert.Same(group, page.SelectedGroup);
        Assert.Equal("System.InvalidOperationException", group.Title);
        Assert.Equal("Charge 18 failed", group.Message);
        Assert.Equal("2", group.CountText);
        Assert.Equal("billing", group.AppText);
        Assert.Equal("2 logs in 1 group.", page.Summary);
        Assert.Null(page.Problem);
    }

    [Fact]
    public async Task Warnings_are_found_when_asked_for()
    {
        var (page, _, _) = Pages();
        page.ShowWarnings = true;
        page.ShowErrors = false;

        await page.SearchCommand.ExecuteAsync(null);

        Assert.Equal("Slow", Assert.Single(page.Groups).Title);
        Assert.True(page.Groups[0].IsWarning);
    }

    [Fact]
    public async Task A_custom_window_is_checked_before_searching()
    {
        var (page, _, _) = Pages();
        page.SelectedWindow = page.Windows.Single(w => w.Window == TimeWindow.Custom);

        page.FromText = "yesterday";
        Assert.Equal("Enter times as yyyy-MM-dd HH:mm, such as 2026-11-03 14:30.", page.WindowError);
        page.FromText = "2026-11-03 12:00";
        page.ToText = "2026-11-03 08:00";
        Assert.Equal("From must be before To.", page.WindowError);

        page.FromText = "2026-11-03 08:00 +02:00";
        page.ToText = "2026-11-03 10:00 +02:00";
        await page.SearchCommand.ExecuteAsync(null);

        Assert.Null(page.WindowError);
        Assert.Equal("Too early", Assert.Single(page.Groups).Title);
    }

    [Fact]
    public async Task Asks_for_at_least_one_level()
    {
        var (page, _, _) = Pages();
        page.ShowErrors = page.ShowCritical = false;

        await page.SearchCommand.ExecuteAsync(null);

        Assert.Equal("Choose at least one level.", page.Problem);
    }

    [Fact]
    public async Task Says_why_a_source_could_not_be_read()
    {
        var (page, _, _) = Pages();
        _logs.Dispose();

        await page.SearchCommand.ExecuteAsync(null);

        Assert.Empty(page.Groups);
        Assert.EndsWith("is not there.", page.Problem);
    }

    [Fact]
    public async Task Follows_the_stack_trace_into_the_apps_code()
    {
        var (page, _, _) = Pages(_code.Path);

        await page.SearchCommand.ExecuteAsync(null);

        var group = page.Groups[0];
        Assert.True(group.HasFolder);
        Assert.Equal(["src/Charges.cs:3", "/ci/work/billing/src/Host.cs:9"], group.Frames.Select(f => f.Location));
        Assert.Equal([true, false], group.Frames.Select(f => f.IsFound));
        Assert.Same(group.Frames[0], group.SelectedFrame);
        Assert.Contains("→ 3      void Charge() => throw new InvalidOperationException();", group.Snippet);
        group.SelectedFrame = group.Frames[1];
        Assert.Equal("This file was not found in the source folder.", group.Snippet);
    }

    [Fact]
    public async Task Choosing_a_folder_on_the_page_keeps_it_on_Settings_Log_Sources()
    {
        var (page, settings, billing) = Pages();
        await page.SearchCommand.ExecuteAsync(null);
        var group = page.Groups[0];
        Assert.False(group.HasFolder);
        Assert.Contains("No source folder is set for billing on Billing.", group.FolderText);
        page.PickFolder = () => Task.FromResult<string?>(_code.Path);

        await page.ChooseFolderCommand.ExecuteAsync(group);

        Assert.True(group.HasFolder);
        Assert.True(group.Frames[0].IsFound);
        Assert.Equal(("billing", _code.Path), (billing.Folders[0].App, billing.Folders[0].Folder));
        Assert.Equal(_code.Path, settings.Sources[^1].Folders[0].FolderText);
        Assert.Equal(_code.Path, LogSourceSettings.Load().Find("Billing")!.FolderFor("billing"));
    }

    [Fact]
    public async Task Asking_the_agent_hands_it_the_error_and_how_to_read_the_code()
    {
        var (page, _, _) = Pages(_code.Path);
        string? asked = null;
        page.AskAgent = text => asked = text;
        await page.SearchCommand.ExecuteAsync(null);

        page.AskCommand.Execute(null);

        Assert.NotNull(asked);
        Assert.StartsWith("Is this error a bug? It is in the logs of Billing, from billing: 2 times between 2026-11-03 11:00 and 2026-11-03 12:00", asked);
        Assert.Contains("Type: System.InvalidOperationException", asked);
        Assert.Contains("Message: Charge 18 failed", asked);
        Assert.Contains("Charges.cs:line 3", asked);
        Assert.Contains("giving the source \"Billing\" and the app \"billing\"", asked);
    }

    [Fact]
    public async Task Without_code_the_agent_is_asked_to_judge_from_the_stack_trace()
    {
        var (page, _, _) = Pages();
        string? asked = null;
        page.AskAgent = text => asked = text;
        await page.SearchCommand.ExecuteAsync(null);

        page.AskCommand.Execute(page.Groups[0]);

        Assert.EndsWith("judge from the stack trace alone: say whether it looks like a bug, the likely cause, and where to look.", asked);
    }

    [Fact]
    public void A_source_removed_on_Settings_goes_from_the_page()
    {
        var (page, settings, _) = Pages();

        settings.Sources[^1].RemoveCommand.Execute(null);

        Assert.DoesNotContain(page.Sources, s => s.DisplayName == "Billing");
        Assert.Equal(LogSourceKind.JoktanElastic, page.SelectedSource!.Kind);
    }

    [Fact]
    public async Task Reads_Joktans_Elasticsearch_with_the_read_key_when_there_is_one()
    {
        var settings = new LogSourcesViewModel(LogSourceSettings.Load(), TelemetrySettings.Load(), _store);
        settings.Sources[0].SecretText = "reader";
        var answer = new ElasticLogReaderTests.Answer(HttpStatusCode.OK, ElasticLogReaderTests.TwoErrors);
        var page = new ErrorsViewModel(settings) { Now = () => Noon, Handler = answer };

        await page.SearchCommand.ExecuteAsync(null);

        Assert.Equal("ApiKey reader", answer.LastAuthorization);
        Assert.StartsWith("http://localhost:9200/logs-joktan%2A/_search", answer.LastUrl!.AbsoluteUri);
        Assert.Equal(2, page.Groups.Count);
    }
}
