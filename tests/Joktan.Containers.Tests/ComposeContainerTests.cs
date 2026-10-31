using System.Text.Json;
using Xunit;

namespace Joktan.Containers.Tests;

public class ComposeContainerTests
{
    [Theory]
    [InlineData("")]
    [InlineData("  \n ")]
    [InlineData("[]")]
    [InlineData("warning: nothing here")]
    public void Nothing_to_read_gives_no_containers(string json) => Assert.Empty(ComposeContainer.Parse(json));

    [Fact]
    public void Reads_one_object_per_line_as_Docker_writes_it()
    {
        var containers = ComposeContainer.Parse("""
            {"Service":"broker","Name":"c-broker-1","State":"running","Status":"Up 2 minutes","Image":"mosquitto","Health":"healthy","Publishers":[{"PublishedPort":1883,"TargetPort":1883},{"PublishedPort":1883,"TargetPort":1883},{"PublishedPort":0,"TargetPort":9001}]}
            {"Service":"db","Name":"c-db-1","State":"exited","Status":"Exited (0)","Publishers":[]}
            """);

        Assert.Equal(2, containers.Count);
        Assert.Equal(new ComposeContainer("broker", "c-broker-1", "running", "Up 2 minutes", "1883→1883", "mosquitto", "healthy"), containers[0]);
        Assert.Equal(new ComposeContainer("db", "c-db-1", "exited", "Exited (0)", ""), containers[1]);
    }

    [Fact]
    public void Reads_an_array_with_the_field_names_of_podman()
    {
        var container = Assert.Single(ComposeContainer.Parse("""
            [{"Names":["c-broker-1"],"Labels":{"com.docker.compose.service":"broker"},"State":"running","Status":"Up",
              "Ports":[{"host_port":1883,"container_port":1883,"range":1},{"host_port":9000,"container_port":90,"range":3},{"host_port":0,"container_port":5}]}]
            """));

        Assert.Equal("broker", container.Service);
        Assert.Equal("c-broker-1", container.Name);
        Assert.Equal("1883→1883, 9000-9002→90-92", container.Ports);
    }

    [Fact]
    public void Falls_back_where_a_field_is_missing()
    {
        var container = Assert.Single(ComposeContainer.Parse("""[{"Names":"only-name","State":"created"}]"""));

        Assert.Equal("only-name", container.Service);
        Assert.Equal("created", container.Status);
        Assert.Equal("", container.Ports);
        Assert.Equal("", container.Image);
    }

    [Fact]
    public void Reads_ports_written_as_text()
    {
        Assert.Equal("1883/tcp", Assert.Single(ComposeContainer.Parse("""[{"Name":"a","Ports":"1883/tcp"}]""")).Ports);
    }

    [Fact]
    public void Bad_JSON_is_not_hidden()
    {
        Assert.ThrowsAny<JsonException>(() => ComposeContainer.Parse("[{"));
        Assert.ThrowsAny<JsonException>(() => ComposeContainer.Parse("{nope"));
    }

    [Theory]
    [InlineData("running", true, false)]
    [InlineData("RUNNING", true, false)]
    [InlineData("paused", false, true)]
    [InlineData("exited", false, false)]
    public void Knows_its_state(string state, bool running, bool paused)
    {
        var container = new ComposeContainer("s", "n", state, "", "");

        Assert.Equal(running, container.IsRunning);
        Assert.Equal(paused, container.IsPaused);
    }
}
