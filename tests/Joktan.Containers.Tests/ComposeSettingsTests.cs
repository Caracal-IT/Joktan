using System;
using System.IO;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Containers.Tests;

public class ComposeSettingsTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void With_no_file_nothing_is_chosen()
    {
        var settings = ComposeSettings.Load(_folder.Path);

        Assert.Null(settings.ComposeFiles);
        Assert.Equal(ContainerEngine.Auto, settings.Engine);
    }

    [Fact]
    public void What_is_saved_is_loaded_with_the_engine_as_a_word()
    {
        var target = _folder.At("deeper");

        new ComposeSettings { ComposeFiles = ["a.yml", "b.yml"], Engine = ContainerEngine.Podman }.Save(target);
        var loaded = ComposeSettings.Load(target);

        Assert.Equal(["a.yml", "b.yml"], loaded.ComposeFiles);
        Assert.Equal(ContainerEngine.Podman, loaded.Engine);
        Assert.Contains("\"Podman\"", File.ReadAllText(ComposeSettings.PathIn(target)));
    }

    [Fact]
    public void The_log_panel_stays_open_until_collapsed_and_is_remembered()
    {
        Assert.False(ComposeSettings.Load(_folder.Path).LogPanelCollapsed);

        new ComposeSettings { LogPanelCollapsed = true }.Save(_folder.Path);

        Assert.True(ComposeSettings.Load(_folder.Path).LogPanelCollapsed);
    }

    [Fact]
    public void A_file_that_cannot_be_read_counts_as_none()
    {
        _folder.Write(ComposeSettings.FileName, "not json");

        Assert.Null(ComposeSettings.Load(_folder.Path).ComposeFiles);
    }

    [Fact]
    public void The_single_file_of_earlier_versions_moves_into_the_list_and_is_not_written_again()
    {
        _folder.Write(ComposeSettings.FileName, """{"ComposeFile":"old.yml"}""");

        var settings = ComposeSettings.Load(_folder.Path);
        settings.Save(_folder.Path);

        Assert.Equal(["old.yml"], settings.ComposeFiles);
        Assert.Null(settings.ComposeFile);
        Assert.DoesNotContain("\"ComposeFile\"", File.ReadAllText(ComposeSettings.PathIn(_folder.Path)));
    }

    [Fact]
    public void The_list_wins_over_the_single_file()
    {
        _folder.Write(ComposeSettings.FileName, """{"ComposeFile":"old.yml","ComposeFiles":[]}""");

        Assert.Empty(ComposeSettings.Load(_folder.Path).ComposeFiles!);
    }

    [Fact]
    public void Finds_the_compose_file_of_the_repository_it_runs_from()
    {
        Assert.Equal(RepositoryFiles.At("containers", "docker-compose.yml"), ComposeSettings.FindRepositoryComposeFile());
    }
}
