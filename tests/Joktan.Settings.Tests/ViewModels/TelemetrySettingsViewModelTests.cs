using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Joktan.Core;
using Joktan.Settings.ViewModels;
using Joktan.Settings.Views;
using Joktan.Tests.TestSupport;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Joktan.Settings.Tests.ViewModels;

[Collection(ProcessStateCollection.Name)]
public class TelemetrySettingsViewModelTests : DataFolderTest
{
    private readonly FakeCredentialStore _store = new();

    private TelemetrySettingsViewModel Page() => new(TelemetrySettings.Load(), _store);

    [Fact]
    public void Starts_with_logs_going_to_the_local_collector_at_information()
    {
        var page = Page();

        Assert.False(page.LogsToFile || page.LogsToElastic || page.MetricsEnabled);
        Assert.True(page.LogsToCollector);
        Assert.Equal("http://localhost:4317", page.CollectorEndpointText);
        Assert.Equal(LogLevel.Information, page.SelectedLevel.Level);
        Assert.Equal("http://localhost:9200", page.ElasticUrlText);
        Assert.Equal("logs-joktan-default", page.DataStreamText);
        Assert.Equal("9464", page.MetricsPortText);
        Assert.Contains(Data.At("logs"), page.LogFolderInUse);
    }

    [Fact]
    public void Each_destination_is_saved_and_applies_at_once()
    {
        var page = Page();

        page.LogsToFile = true;
        page.LogsToElastic = true;
        page.LogsToCollector = true;

        var saved = TelemetrySettings.Load();
        Assert.True(saved.LogsToFile && saved.LogsToElastic && saved.LogsToCollector);
        Assert.NotNull(AppLog.Target());

        page.LogsToElastic = false;
        Assert.Null(AppLog.Target());
        AppLog.Configure(new TelemetrySettings());
    }

    [Fact]
    public void What_is_not_valid_is_not_saved()
    {
        var page = Page();

        page.ElasticUrlText = "not an address";
        page.DataStreamText = "Not Valid";
        page.MetricsPortText = "99999";
        page.LogFolderText = "relative";
        page.LogFileDaysText = "0";
        page.CollectorEndpointText = "collector:4317";

        Assert.NotNull(page.ElasticUrlError);
        Assert.NotNull(page.DataStreamError);
        Assert.NotNull(page.MetricsPortError);
        Assert.NotNull(page.LogFolderError);
        Assert.NotNull(page.LogFileDaysError);
        Assert.NotNull(page.CollectorEndpointError);
        var saved = TelemetrySettings.Load();
        Assert.Equal("http://localhost:9200", saved.ElasticUrl);
        Assert.Equal(9464, saved.MetricsPort);
        Assert.Equal("", saved.LogFolder);
    }

    [Fact]
    public void An_api_key_goes_to_the_credential_store_and_is_never_shown_or_written()
    {
        var page = Page();
        page.ElasticUrlText = "https://x.es.cloud.example:9243";
        page.SelectedAuthentication = page.Authentications.Single(a => a.Authentication == ElasticAuthentication.ApiKey);
        Assert.True(page.UsesApiKey);
        Assert.False(page.HasSavedSecret);

        page.SecretText = "the-key";

        Assert.True(page.HasSavedSecret);
        Assert.Contains("the-key", _store.Saved.Values);
        Assert.DoesNotContain("the-key", File.ReadAllText(Data.At("settings", "telemetry.json")));

        var again = Page();
        Assert.True(again.HasSavedSecret);
        Assert.Equal("", again.SecretText);
        again.LogsToElastic = true;
        Assert.Equal("ApiKey the-key", AppLog.Target()!.Authorization);
        AppLog.Configure(new TelemetrySettings());
    }

    [Fact]
    public void Says_when_a_secret_cannot_be_kept()
    {
        _store.FailWrites = true;
        var page = Page();
        page.SelectedAuthentication = page.Authentications.Single(a => a.Authentication == ElasticAuthentication.Password);

        page.SecretText = "pw";

        Assert.NotNull(page.SecretWarning);
    }

    [Fact]
    public void The_address_says_who_can_scrape()
    {
        var page = Page();
        page.MetricsPortText = "9500";
        Assert.Contains("http://localhost:9500/metrics", page.MetricsAddress);

        page.MetricsFromOtherComputers = true;
        Assert.Contains("no login", page.MetricsAddress);
    }

    [Fact]
    public Task The_page_shows_every_destination() => Headless.Run(() =>
    {
        var window = Headless.Show(new TelemetrySettingsView { DataContext = Page() });
        Headless.Settle();

        Assert.Equal(5, Headless.All<ToggleSwitch>(window).Count());
        var text = Headless.All<TextBlock>(window).Select(t => t.Text).ToList();
        Assert.Contains("Write logs to a file", text);
        Assert.Contains("Send logs to Elasticsearch", text);
        Assert.Contains("Send logs to a collector", text);
        Assert.Contains("Metrics endpoint", text);
    });
}
