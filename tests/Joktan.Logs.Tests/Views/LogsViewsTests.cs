using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Joktan.Core;
using Joktan.Logs.ViewModels;
using Joktan.Logs.Views;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Logs.Tests.Views;

[Collection(ProcessStateCollection.Name)]
public sealed class LogsViewsTests : DataFolderTest
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 3, 12, 0, 0, TimeSpan.FromHours(2));

    private readonly FakeCredentialStore _store = new();

    [Fact]
    public Task The_errors_page_shows_a_group_its_frames_and_the_code() => Headless.Run(async () =>
    {
        using var logs = new TempFolder();
        using var code = new TempFolder();
        code.Write("src/A.cs", "one\ntwo\nthree");
        File.SetLastWriteTimeUtc(logs.Write("app.log", """
            2026-11-03T11:50:00+02:00 ERROR app=shop module=cart Cart failed
            System.InvalidOperationException: empty
               at Shop.Cart.Add() in /ci/src/A.cs:line 2
            """), Noon.UtcDateTime);
        var settings = new LogSourcesViewModel(LogSourceSettings.Load(), TelemetrySettings.Load(), _store);
        settings.AddFilesCommand.Execute(null);
        settings.Sources[^1].PathText = logs.Path;
        settings.Sources[^1].AddFolderCommand.Execute(null);
        settings.Sources[^1].Folders[0].FolderText = code.Path;
        var page = new ErrorsViewModel(settings) { Now = () => Noon };
        page.SelectedSource = settings.Sources[^1].Source;
        var window = Headless.Show(new ErrorsView { DataContext = page });

        await page.SearchCommand.ExecuteAsync(null);
        page.SelectedWindow = page.Windows[^1];
        Headless.Settle();
        window.UpdateLayout();
        window.CaptureRenderedFrame();

        var texts = Headless.All<TextBlock>(window).Select(t => t.Text).ToList();
        Assert.Contains("System.InvalidOperationException", texts);
        Assert.Contains("src/A.cs:2", texts);
        Assert.Contains(Headless.All<SelectableTextBlock>(window), t => t.Text?.Contains("→ 2  two") == true);
        Assert.NotNull(page.PickFolder);
        Assert.NotNull(page.OpenFile);
        window.Close();
    });

    [Fact]
    public Task The_log_sources_page_shows_every_kind_of_source() => Headless.Run(() =>
    {
        var page = new LogSourcesViewModel(LogSourceSettings.Load(), TelemetrySettings.Load(), _store);
        page.AddElasticCommand.Execute(null);
        page.AddFilesCommand.Execute(null);
        page.Sources[2].SelectedSignIn = page.SignIns[2];
        page.Sources[2].UrlText = "not an address";
        var window = Headless.Show(new LogSourcesView { DataContext = page });

        Headless.Settle();
        window.UpdateLayout();
        window.CaptureRenderedFrame();

        var texts = Headless.All<TextBlock>(window).Select(t => t.Text).ToList();
        Assert.Contains("Joktan (Elasticsearch)", texts);
        Assert.Contains("Joktan (log files)", texts);
        Assert.Contains("Enter a whole address, such as http://localhost:9200.", texts);
        Assert.NotNull(page.PickFolder);
        window.Close();
    });
}
