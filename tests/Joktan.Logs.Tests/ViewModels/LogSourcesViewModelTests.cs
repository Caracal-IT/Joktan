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
public class LogSourcesViewModelTests : DataFolderTest
{
    private readonly FakeCredentialStore _store = new();

    private LogSourcesViewModel Page() => new(LogSourceSettings.Load(), TelemetrySettings.Load(), _store);

    [Fact]
    public void Joktans_own_sources_cannot_be_renamed_or_removed()
    {
        var page = Page();
        var joktan = page.Sources[0];

        joktan.NameText = "Mine";
        joktan.RemoveCommand.Execute(null);

        Assert.Equal(2, page.Sources.Count);
        Assert.False(joktan.CanRename);
        Assert.Equal("Joktan (Elasticsearch)", LogSourceSettings.Load().Sources[0].DisplayName);
        Assert.Contains("http://localhost:9200", joktan.JoktanText);
        Assert.Contains(Path.Combine(Data.Path, "logs"), page.Sources[1].JoktanText);
    }

    [Fact]
    public void Added_sources_get_names_of_their_own_that_must_stay_different()
    {
        var page = Page();
        page.AddElasticCommand.Execute(null);
        page.AddElasticCommand.Execute(null);
        var second = page.Sources[^1];

        Assert.Equal("Elasticsearch 2", second.NameText);
        second.NameText = "elasticsearch";
        Assert.Equal("Another source has this name.", second.NameError);
        second.NameText = " ";
        Assert.Equal("Enter a name.", second.NameError);
        second.NameText = "Billing";
        Assert.Null(second.NameError);
        Assert.Equal(["Elasticsearch", "Billing"], LogSourceSettings.Load().Sources.Skip(2).Select(s => s.Name));
    }

    [Fact]
    public void Each_field_is_saved_once_it_is_valid()
    {
        var page = Page();
        page.AddFilesCommand.Execute(null);
        var files = page.Sources[^1];

        files.PathText = "logs";
        files.FilterText = "a/*.log";
        Assert.NotNull(files.PathError);
        Assert.NotNull(files.FilterError);
        files.PathText = "~/logs";
        files.FilterText = "*.txt";
        files.Subfolders = true;

        var saved = LogSourceSettings.Load().Sources[^1];
        Assert.Equal(("~/logs", "*.txt", true), (saved.Path, saved.Filter, saved.Subfolders));
    }

    [Fact]
    public void Folders_are_added_changed_and_removed()
    {
        var page = Page();
        var joktan = page.Sources[0];
        var changes = 0;
        page.Changed += () => changes++;

        joktan.Folders[0].FolderText = "relative";
        Assert.NotNull(joktan.Folders[0].FolderError);
        joktan.Folders[0].FolderText = "~/src/Joktan";
        joktan.AddFolderCommand.Execute(null);
        joktan.Folders[1].App = " billing ";
        joktan.Folders[1].RemoveCommand.Execute(null);

        var saved = LogSourceSettings.Load().Sources[0];
        Assert.Equal("~/src/Joktan", Assert.Single(saved.Folders).Folder);
        Assert.True(changes >= 4);
    }

    [Fact]
    public void An_other_Elasticsearchs_password_goes_to_the_credential_store()
    {
        var page = Page();
        page.AddElasticCommand.Execute(null);
        var elastic = page.Sources[^1];

        elastic.SelectedSignIn = elastic.SignIns.Single(s => s.Authentication == ElasticAuthentication.Password);
        elastic.Username = "ann";
        elastic.SecretText = "pw";
        elastic.SelectedSignIn = null;

        Assert.True(elastic.UsesPassword);
        Assert.True(elastic.HasSavedSecret);
        Assert.Equal("pw", _store.Saved[elastic.Source.Id]);
        Assert.DoesNotContain("pw", File.ReadAllText(Data.At("settings", "log-sources.json")));

        elastic.RemoveCommand.Execute(null);
        Assert.Empty(_store.Saved);
    }

    [Fact]
    public void A_read_key_that_cannot_be_kept_says_so_and_can_be_forgotten()
    {
        _store.FailWrites = true;
        var joktan = Page().Sources[0];

        joktan.SecretText = "reader";
        Assert.Equal("It could not be kept in the credential store, so it is used until Joktan closes.", joktan.SecretWarning);
        joktan.ForgetSecretCommand.Execute(null);

        Assert.False(joktan.HasSavedSecret);
        Assert.Null(joktan.SecretWarning);
    }

    [Fact]
    public async Task Testing_says_whether_the_source_can_be_read()
    {
        var page = new LogSourcesViewModel(LogSourceSettings.Load(), TelemetrySettings.Load(), _store)
        {
            TestHandler = new ElasticLogReaderTests.Answer(HttpStatusCode.Unauthorized, "{}"),
        };
        page.AddFilesCommand.Execute(null);
        using var folder = new TempFolder();
        page.Sources[^1].PathText = folder.Path;

        await page.Sources[0].TestCommand.ExecuteAsync(null);
        await page.Sources[^1].TestCommand.ExecuteAsync(null);

        Assert.Equal("It could not be read: Elasticsearch refused the login. Check the API key, or the username and password.", page.Sources[0].TestResult);
        Assert.Equal("It could be read, and has nothing from the last day.", page.Sources[^1].TestResult);
    }
}
