using System.IO;
using System.Linq;
using Joktan.Core;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Logs.Tests;

[Collection(ProcessStateCollection.Name)]
public class LogSourceSettingsTests : DataFolderTest
{
    [Fact]
    public void Starts_with_Joktans_own_sources_whose_code_is_for_joktan()
    {
        var settings = LogSourceSettings.Load();

        Assert.Equal([LogSourceKind.JoktanElastic, LogSourceKind.JoktanFiles], settings.Sources.Select(s => s.Kind));
        Assert.Equal(["Joktan (Elasticsearch)", "Joktan (log files)"], settings.Sources.Select(s => s.DisplayName));
        Assert.Equal("logs-joktan*", settings.Sources[0].Index);
        Assert.All(settings.Sources, s => Assert.Equal("joktan", Assert.Single(s.Folders).App));
    }

    [Fact]
    public void Keeps_other_sources_and_puts_back_Joktans_own_if_they_went()
    {
        var settings = LogSourceSettings.Load();
        settings.Sources.Clear();
        settings.Sources.Add(new LogSource { Kind = LogSourceKind.Files, Name = "Billing", Path = "~/logs/billing", Folders = [new AppFolder { Folder = "~/src/billing" }] });
        settings.Save();

        var again = LogSourceSettings.Load();

        Assert.Equal(["Joktan (Elasticsearch)", "Joktan (log files)", "Billing"], again.Sources.Select(s => s.DisplayName));
        Assert.Equal("~/logs/billing", again.Find("billing")!.Path);
        Assert.DoesNotContain("Secret", File.ReadAllText(Data.At("settings", "log-sources.json")));
    }

    [Fact]
    public void An_apps_own_folder_wins_over_the_one_for_any_app()
    {
        var source = new LogSource { Folders = [new AppFolder { Folder = "/src/any" }, new AppFolder { App = "Billing", Folder = "/src/billing" }] };

        Assert.Equal("/src/billing", source.FolderFor("billing"));
        Assert.Equal("/src/any", source.FolderFor("orders"));
        Assert.Equal("/src/any", source.FolderFor(null));
        Assert.Null(new LogSource().FolderFor("billing"));
    }

    [Fact]
    public void Keeps_keys_and_passwords_in_the_credential_store()
    {
        var store = new FakeCredentialStore();
        var settings = LogSourceSettings.Load();
        var other = new LogSource { Kind = LogSourceKind.Elastic, Authentication = ElasticAuthentication.Password, Username = "ann" };
        settings.Sources.Add(other);

        LogSourceSettings.WriteSecret(store, other, "pw");
        LogSourceSettings.WriteSecret(store, settings.Sources[0], "read-key");
        other.Secret = settings.Sources[0].Secret = "";
        settings.ReadSecrets(store);

        Assert.Equal("Basic YW5uOnB3", other.Authorization());
        Assert.Equal("read-key", settings.Sources[0].Secret);
        LogSourceSettings.WriteSecret(store, other, "");
        Assert.DoesNotContain(other.Id, store.Saved.Keys);
    }

    [Theory]
    [InlineData("logs-*", true)]
    [InlineData("logs-a-*,logs-b-*", true)]
    [InlineData("logs *", false)]
    [InlineData("logs/a", false)]
    [InlineData("", false)]
    public void Index_patterns_are_checked(string index, bool valid)
    {
        Assert.Equal(valid, LogSource.IsValidIndex(index));
    }

    [Theory]
    [InlineData("*.log", true)]
    [InlineData("app-*.txt; *.log", true)]
    [InlineData("logs/*.log", false)]
    [InlineData(" ; ", false)]
    public void Filters_are_checked(string filter, bool valid)
    {
        Assert.Equal(valid, LogSource.IsValidFilter(filter));
    }
}
