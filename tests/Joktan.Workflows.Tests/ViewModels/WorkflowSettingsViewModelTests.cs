using System.IO;
using System.Threading.Tasks;
using Joktan.Tests.TestSupport;
using Joktan.Workflows.Tests.Engine;
using Joktan.Workflows.ViewModels;
using Xunit;

namespace Joktan.Workflows.Tests.ViewModels;

[Collection(ProcessStateCollection.Name)]
public class WorkflowSettingsViewModelTests : EngineTest
{
    [Fact]
    public void Changes_are_kept_within_their_ranges_and_saved()
    {
        var vm = new WorkflowSettingsViewModel(Engine);

        vm.MaxRunning = 1000;
        vm.ShortTimeLimitSeconds = 0;
        vm.KeepRuns = 3;
        vm.MaxSteps = 50;
        vm.ResumeLongRuns = false;
        vm.GoCommand = "/opt/my go/bin/go ";

        Assert.Equal(100, Settings.MaxRunning);
        Assert.Equal(1, Settings.ShortTimeLimitSeconds);
        Assert.Equal(10, Settings.KeepRuns);
        var saved = WorkflowSettings.Load();
        Assert.Equal(50, saved.MaxSteps);
        Assert.False(saved.ResumeLongRuns);
        Assert.Equal("/opt/my go/bin/go", saved.Go);
    }

    [Fact]
    public void Another_folder_is_read_for_workflows()
    {
        var vm = new WorkflowSettingsViewModel(Engine);
        Data.Write("elsewhere/Mine.workflow.json", """{"id": "m", "name": "Mine"}""");

        vm.FolderText = Data.At("elsewhere");
        vm.ApplyFolderCommand.Execute(null);

        Assert.Equal(Path.GetFullPath(Data.At("elsewhere")), Engine.Library.Folder);
        Assert.NotNull(Engine.Library.Find("Mine"));
        Assert.Contains("Read 1 workflows", vm.FolderMessage);
        Assert.Equal(Data.At("elsewhere"), WorkflowSettings.Load().Folder);

        vm.UseDefaultFolderCommand.Execute(null);
        vm.ApplyFolderCommand.Execute(null);
        Assert.Equal(Path.GetFullPath(WorkflowSettings.DefaultFolder), Engine.Library.Folder);
    }

    [Fact]
    public async Task The_go_command_is_checked()
    {
        var vm = new WorkflowSettingsViewModel(Engine) { GoCommand = "joktan-no-such-go" };

        await vm.CheckGoCommand.ExecuteAsync(null);

        Assert.Contains("not found", vm.GoVersion);
    }

    [Fact]
    public Task Turning_off_mqtt_triggers_stops_them_listening() => Headless.Run(async () =>
    {
        var flow = new Flow("Listener").Then(Flow.End());
        flow.Workflow.Triggers = new() { Mqtt = true, MqttClient = "Local", MqttTopic = "a/#" };
        Library.Save(flow.Workflow);
        await Headless.Until(() => Services.Listening == 1);

        new WorkflowSettingsViewModel(Engine).MqttTriggers = false;

        await Headless.Until(() => Services.Listening == 0);
        Assert.False(WorkflowSettings.Load().MqttTriggers);
    });

    [Fact]
    public void Turning_off_schedules_stops_them()
    {
        var flow = new Flow("Timed").Then(Flow.End());
        flow.Workflow.Triggers = new() { Scheduled = true };
        Library.Save(flow.Workflow);
        Engine.Restore();
        Assert.True(Engine.IsScheduleActive(flow.Workflow.Id));

        new WorkflowSettingsViewModel(Engine).ScheduleTriggers = false;

        Assert.False(Engine.IsScheduleActive(flow.Workflow.Id));
        Assert.False(WorkflowSettings.Load().ScheduleTriggers);
    }

    [Fact]
    public void Global_usings_and_libraries_are_kept_one_per_line()
    {
        var vm = new WorkflowSettingsViewModel(Engine) { CSharpImports = "System.Globalization\r\nSystem.Numerics", JavaScriptLibraries = "a.js" };

        Assert.Equal(["System.Globalization", "System.Numerics"], WorkflowSettings.Load().CSharpImports);
        Assert.Equal(["a.js"], Settings.JavaScriptLibraries);
        Assert.Equal("System.Globalization\nSystem.Numerics", vm.CSharpImports);

        vm.NuGetFeeds = "  ";
        Assert.Equal([WorkflowSettings.NuGetOrg], Settings.Feeds);
        vm.NuGetFeeds = "/packages \nhttps://feed.example.com/v3/";
        Assert.Equal(["/packages", "https://feed.example.com/v3/"], WorkflowSettings.Load().Feeds);

        vm.WorkingFolder = "/work";
        Assert.Equal("/work", WorkflowSettings.Load().WorkingFolder);
    }

    [Fact]
    public void Settings_with_one_feed_get_it_as_the_first_of_their_feeds()
    {
        Directory.CreateDirectory(Joktan.Core.DataFolder.For("settings"));
        File.WriteAllText(Path.Combine(Joktan.Core.DataFolder.For("settings"), "workflows.json"), """{"NuGetFeed": "/old/packages"}""");

        var settings = WorkflowSettings.Load();

        Assert.Equal(["/old/packages", WorkflowSettings.NuGetOrg], settings.Feeds);
        Assert.Null(settings.NuGetFeed);
    }
}
