using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Containers.Tests;

public class ComposeToolOrderTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    private List<string> Order(string yaml, params string[] services)
        => ComposeTool.InFileOrder(_folder.Write("docker-compose.yml", yaml), services);

    [Fact]
    public void Follows_the_order_of_the_file()
    {
        var order = Order("""
            # A file.
            name: x
            services:
              zebra:
                image: a
                ports:
                  - "1:1"

              # Comment.
              "quoted":
                image: b
              apple: # Trailing.
                image: c
            volumes:
              notaservice:
            """, "apple", "notaservice", "quoted", "zebra");

        Assert.Equal(["zebra", "quoted", "apple", "notaservice"], order);
    }

    [Fact]
    public void Services_not_in_the_file_come_last_in_alphabetical_order()
    {
        var order = Order("services:\n  b:\n    image: x\n", "z", "a", "b");

        Assert.Equal(["b", "a", "z"], order);
    }

    [Fact]
    public void Without_a_services_section_the_order_is_alphabetical()
    {
        Assert.Equal(["a", "b"], Order("name: x\n", "b", "a"));
    }

    [Fact]
    public void An_unreadable_file_leaves_the_order_alphabetical()
    {
        Assert.Equal(["a", "b"], ComposeTool.InFileOrder(_folder.At("missing.yml"), ["b", "a"]));
    }
}

// Runs a small script standing in for docker, found first on PATH, so no real engine is touched.
[Collection(ProcessStateCollection.Name)]
public class ComposeToolRunTests : IDisposable
{
    private const string FakeDocker = """
        #!/bin/sh
        # Lets each test choose what the command does through its arguments.
        case "$*" in
          *say-hello*) printf 'hello\n'; printf '\033[31mred\033[0m\n'; printf 'to errors\n' >&2 ;;
          *show-args*) for a in "$@"; do echo "[$a]"; done ;;
          *show-dir*) pwd ;;
          *show-env*) echo "$COMPOSE_ANSI $NO_COLOR" ;;
          *fail-quietly*) exit 3 ;;
          *fail-loudly*) echo out; echo 'first' >&2; echo 'last reason' >&2; echo '' >&2; exit 4 ;;
          *hang*) exec sleep 30 ;;
        esac
        """;

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private readonly TempFolder _folder = new();
    private readonly string? _path = Environment.GetEnvironmentVariable("PATH");
    private readonly string _composeFile;

    [UnsupportedOSPlatform("windows")]
    public ComposeToolRunTests()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The stand-in for docker is a shell script.");

        var docker = _folder.Write("bin/docker", FakeDocker.ReplaceLineEndings("\n"));
        File.SetUnixFileMode(docker, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        Environment.SetEnvironmentVariable("PATH", _folder.At("bin") + Path.PathSeparator + _path);
        _composeFile = _folder.Write("project dir/docker-compose.yml", "services: {}\n");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("PATH", _path);
        _folder.Dispose();
    }

    private Task<ComposeTool> Tool() => ComposeTool.ResolveAsync(ContainerEngine.Docker, _composeFile, Cancel);

    [Fact]
    public void Finds_a_command_on_PATH_first()
    {
        Assert.Equal(_folder.At("bin", "docker"), ComposeTool.Find(ContainerEngine.Docker));
    }

    [Fact]
    public async Task Resolves_the_engine_asked_for()
    {
        var tool = await Tool();

        Assert.Equal(ContainerEngine.Docker, tool.Engine);
        Assert.Equal("docker", tool.EngineName);
        Assert.Equal(_folder.At("bin", "docker"), tool.Executable);
        Assert.Equal(_composeFile, tool.ComposeFile);
    }

    [Fact]
    public async Task Moves_to_another_compose_file_with_the_same_engine()
    {
        var other = (await Tool()).For("/other.yml");

        Assert.Equal("/other.yml", other.ComposeFile);
        Assert.Equal(ContainerEngine.Docker, other.Engine);
    }

    [Fact]
    public async Task Shows_command_lines_quoting_what_has_spaces()
    {
        var tool = await Tool();

        Assert.Equal("docker compose up \"a b\"", tool.CommandLine(["up", "a b"]));
        Assert.Equal("docker ps \"a b\"", tool.EngineCommandLine(["ps", "a b"]));
    }

    [Fact]
    public async Task Passes_on_every_line_without_colour_codes_and_returns_the_exit_code()
    {
        var lines = new List<string>();

        var exitCode = await (await Tool()).RunAsync(["say-hello"], line =>
        {
            lock (lines)
                lines.Add(line);
        }, Cancel);

        Assert.Equal(0, exitCode);
        Assert.Equal(["hello", "red", "to errors"], lines.Order());
    }

    [Fact]
    public async Task Runs_compose_with_the_file_from_its_folder()
    {
        var tool = await Tool();

        var arguments = await tool.ReadAsync(["show-args", "a b"], Cancel);
        var folder = await tool.ReadAsync(["show-dir"], Cancel);

        Assert.Equal(string.Join("\n", "[compose]", "[-f]", $"[{_composeFile}]", "[show-args]", "[a b]"), arguments);
        Assert.Equal(Path.GetFullPath(Path.GetDirectoryName(_composeFile)!), Path.GetFullPath(folder.Replace("/private/var", "/var", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Asks_for_output_without_colour()
    {
        Assert.Equal("never 1", await (await Tool()).ReadAsync(["show-env"], Cancel));
    }

    [Fact]
    public async Task Runs_an_engine_command_without_compose()
    {
        var lines = new List<string>();

        await (await Tool()).RunEngineAsync(["show-args"], lines.Add, Cancel);

        Assert.Equal(["[show-args]"], lines);
    }

    [Fact]
    public async Task Reading_returns_the_exit_code_as_an_error_with_the_reason()
    {
        var tool = await Tool();

        var loud = await Assert.ThrowsAsync<InvalidOperationException>(() => tool.ReadAsync(["fail-loudly"], Cancel));
        var quiet = await Assert.ThrowsAsync<InvalidOperationException>(() => tool.ReadAsync(["fail-quietly"], Cancel));

        Assert.Equal("last reason", loud.Message);
        Assert.Equal("docker compose fail-quietly failed with exit code 3.", quiet.Message);
    }

    [Fact]
    public async Task Running_returns_a_failing_exit_code()
    {
        Assert.Equal(3, await (await Tool()).RunAsync(["fail-quietly"], _ => { }, Cancel));
    }

    [Fact]
    public async Task Cancelling_stops_the_command()
    {
        var tool = await Tool();
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tool.RunAsync(["hang"], _ => { }, cancel.Token));
    }
}
