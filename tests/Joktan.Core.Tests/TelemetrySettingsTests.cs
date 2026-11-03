using System.IO;
using Joktan.Tests.TestSupport;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Joktan.Core.Tests;

[Collection(ProcessStateCollection.Name)]
public class TelemetrySettingsTests : DataFolderTest
{
    [Fact]
    public void Only_the_collector_starts_on_and_settings_come_back_without_the_secret()
    {
        var fresh = TelemetrySettings.Load();
        Assert.False(fresh.LogsToFile || fresh.LogsToElastic || fresh.MetricsEndpointEnabled);
        Assert.True(fresh.LogsToCollector);
        Assert.Equal("http://localhost:4317", fresh.CollectorEndpoint);

        new TelemetrySettings { LogsToFile = true, MinimumLevel = LogLevel.Warning, ElasticAuthentication = ElasticAuthentication.ApiKey, Secret = "very-secret" }.Save();

        var loaded = TelemetrySettings.Load();
        Assert.True(loaded.LogsToFile);
        Assert.Equal(LogLevel.Warning, loaded.MinimumLevel);
        Assert.Equal(ElasticAuthentication.ApiKey, loaded.ElasticAuthentication);
        var text = File.ReadAllText(Data.At("settings", "telemetry.json"));
        Assert.DoesNotContain("very-secret", text);
        Assert.Contains("\"ApiKey\"", text);
    }

    [Fact]
    public void A_file_that_cannot_be_read_leaves_everything_off()
    {
        Data.Write("settings/telemetry.json", "not json");

        Assert.False(TelemetrySettings.Load().LogsToElastic);
    }

    [Theory]
    [InlineData("http://localhost:9200", true)]
    [InlineData("https://x.es.cloud.example:9243", true)]
    [InlineData("localhost:9200", false)]
    [InlineData("ftp://x", false)]
    [InlineData("", false)]
    public void Only_a_whole_web_address_is_valid(string text, bool valid) => Assert.Equal(valid, TelemetrySettings.IsValidUrl(text));

    [Theory]
    [InlineData("logs-joktan-default", true)]
    [InlineData("Logs", false)]
    [InlineData("has space", false)]
    [InlineData("-dash", false)]
    [InlineData("", false)]
    public void A_data_stream_follows_the_rules(string text, bool valid) => Assert.Equal(valid, TelemetrySettings.IsValidDataStream(text));

    [Fact]
    public void The_login_is_sent_as_the_chosen_kind()
    {
        Assert.Null(new TelemetrySettings().Authorization());
        Assert.Equal("ApiKey abc", new TelemetrySettings { ElasticAuthentication = ElasticAuthentication.ApiKey, Secret = "abc" }.Authorization());
        Assert.Equal("Basic dTpw", new TelemetrySettings { ElasticAuthentication = ElasticAuthentication.Password, ElasticUsername = "u", Secret = "p" }.Authorization());
    }

    [Fact]
    public void The_secret_goes_to_the_credential_store()
    {
        var store = new FakeCredentialStore();
        var settings = new TelemetrySettings { ElasticAuthentication = ElasticAuthentication.ApiKey };

        Assert.True(settings.WriteSecret(store, "abc"));
        Assert.Contains("abc", store.Saved.Values);

        var again = new TelemetrySettings { ElasticAuthentication = ElasticAuthentication.ApiKey };
        again.ReadSecret(store);
        Assert.Equal("abc", again.Secret);

        again.WriteSecret(store, "");
        Assert.Empty(store.Saved);
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("~/logs", true)]
    [InlineData("/var/log/joktan", true)]
    [InlineData("relative/logs", false)]
    public void A_log_folder_is_whole_or_empty(string text, bool valid) => Assert.Equal(valid, TelemetrySettings.IsValidFolder(text));

    [Fact]
    public void Log_files_go_to_the_data_folder_unless_another_is_chosen()
    {
        Assert.Equal(Data.At("logs"), new TelemetrySettings().EffectiveLogFolder);
        Assert.Equal("/somewhere", new TelemetrySettings { LogFolder = "/somewhere" }.EffectiveLogFolder);
    }
}
