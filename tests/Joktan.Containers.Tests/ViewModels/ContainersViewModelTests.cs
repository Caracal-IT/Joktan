using System;
using Joktan.Containers.ViewModels;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Containers.Tests.ViewModels;

public class ContainersViewModelTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void The_log_panel_starts_open()
    {
        Assert.True(new ContainersViewModel(_folder.Path).IsLogPanelOpen);
    }

    [Fact]
    public void Collapsing_the_log_panel_is_remembered()
    {
        var page = new ContainersViewModel(_folder.Path);

        page.ToggleLogPanelCommand.Execute(null);

        Assert.False(page.IsLogPanelOpen);
        Assert.True(ComposeSettings.Load(_folder.Path).LogPanelCollapsed);
        Assert.False(new ContainersViewModel(_folder.Path).IsLogPanelOpen);
    }

    [Fact]
    public void Opening_the_log_panel_again_is_remembered()
    {
        new ComposeSettings { LogPanelCollapsed = true }.Save(_folder.Path);
        var page = new ContainersViewModel(_folder.Path);

        page.ToggleLogPanelCommand.Execute(null);

        Assert.True(page.IsLogPanelOpen);
        Assert.False(ComposeSettings.Load(_folder.Path).LogPanelCollapsed);
    }
}
