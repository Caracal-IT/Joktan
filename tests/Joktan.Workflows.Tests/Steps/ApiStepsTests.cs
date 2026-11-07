using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Joktan.Api;
using Joktan.Api.Tests;
using Joktan.Mqtt.Tests;
using Joktan.Tests.TestSupport;
using Joktan.Workflows.Engine;
using Joktan.Workflows.Model;
using Joktan.Workflows.Tests.Engine;
using Xunit;

namespace Joktan.Workflows.Tests.Steps;

[Collection(ProcessStateCollection.Name)]
public class ApiStepsTests : EngineTest
{
    [Fact]
    public async Task A_REST_request_written_on_the_step_fills_in_paths_and_gives_back_the_answer()
    {
        await using var server = await FakeServer.StartAsync();
        var step = new WorkflowStep
        {
            Type = StepType.Http, Method = "POST", Url = server.RestAddress + "/orders/{{input.id}}", Body = """{"qty": {{input.qty}}}""",
            Headers = [new StepPair { Name = "X-Order", Value = "{{input.id}}" }], ResultVariable = "reply",
        };

        var run = await RunAsync(new Flow().Then(step).Then(Flow.End("{{vars.reply.body.token}}")), """{"id": 7, "qty": 3}""");

        Assert.Equal(RunStatus.Completed, run.Status);
        Assert.Equal("\"from the server\"", JsonText.Of(run.Output));
        Assert.Equal("""{"qty": 3}""", server.LastRestBody);
        var reply = run.Data[Templates.Vars]!["reply"]!;
        Assert.Equal(200, (int?)reply["code"]);
        Assert.True((bool?)reply["ok"]);
    }

    [Fact]
    public async Task An_answer_that_is_not_a_success_goes_out_by_error_unless_told_not_to()
    {
        await using var server = await FakeServer.StartAsync();
        var failing = new WorkflowStep { Type = StepType.Http, Url = server.RestAddress + "/missing" };
        var tolerant = new WorkflowStep { Type = StepType.Http, Url = server.RestAddress + "/missing", FailOnErrorStatus = false };

        var failed = await RunAsync(new Flow().Then(failing));
        var carried = await RunAsync(new Flow().Then(tolerant).Then(Flow.End("{{last.code}}")));

        Assert.Equal(RunStatus.Failed, failed.Status);
        Assert.Contains("404", failed.Error);
        Assert.Equal("404", JsonText.Of(carried.Output));
    }

    [Fact]
    public async Task A_REST_request_that_gets_no_answer_fails_the_step()
    {
        var run = await RunAsync(new Flow().Then(new WorkflowStep { Type = StepType.Http, Url = $"http://127.0.0.1:{TestBroker.FreePort()}/" }));

        Assert.Equal(RunStatus.Failed, run.Status);
    }

    [Theory]
    [InlineData(BodyMode.Keep, """{"name":"saved","n":1}""")]
    [InlineData(BodyMode.Replace, """{"name":"Ada"}""")]
    [InlineData(BodyMode.Merge, """{"name":"Ada","n":1}""")]
    public async Task A_saved_request_is_sent_with_its_body_kept_replaced_or_merged_into(BodyMode mode, string sent)
    {
        await using var server = await FakeServer.StartAsync();
        Services.Requests["Orders/Create"] = new ApiRequest
        {
            Method = "POST", Url = "{{baseUrl}}/orders", BodyKind = ApiBodyKind.Json, Body = """{"name":"saved","n":1}""",
        };
        Services.Environment.Add(new ApiPair { Name = "baseUrl", Value = server.RestAddress });
        var step = new WorkflowStep { Type = StepType.Http, RequestPath = "Orders/Create", BodyMode = mode, Body = """{"name":"{{input.name}}"}""" };

        var run = await RunAsync(new Flow().Then(step), """{"name": "Ada"}""");

        Assert.Equal(RunStatus.Completed, run.Status);
        Assert.Equal(JsonNode.Parse(sent)!.ToJsonString(), JsonNode.Parse(server.LastRestBody)!.ToJsonString());

        // The saved request is left as it was.
        Assert.Equal("""{"name":"saved","n":1}""", Services.Requests["Orders/Create"].Body);
    }

    [Fact]
    public async Task A_request_that_is_not_saved_or_of_the_wrong_kind_fails_the_step()
    {
        Services.Requests["Call"] = new ApiRequest { Kind = ApiKind.Grpc };

        var missing = await RunAsync(new Flow().Then(new WorkflowStep { Type = StepType.Http, RequestPath = "Nowhere" }));
        var wrong = await RunAsync(new Flow().Then(new WorkflowStep { Type = StepType.Http, RequestPath = "Call" }));

        Assert.Contains("no request Nowhere", missing.Error);
        Assert.Contains("gRPC request", wrong.Error);
    }

    [Fact]
    public async Task A_gRPC_call_written_on_the_step_finds_the_method_by_reflection()
    {
        await using var server = await FakeServer.StartAsync();
        var step = new WorkflowStep
        {
            Type = StepType.Grpc, Address = server.GrpcAddress, Service = "demo.Greeter", Rpc = "One", Body = """{"name": "{{input.name}}"}""", ResultVariable = "reply",
            Headers = [new StepPair { Name = "x-custom", Value = "from a workflow" }],
        };

        var run = await RunAsync(new Flow().Then(step).Then(Flow.End("{{vars.reply.body.name}}")), """{"name": "Ada"}""");

        Assert.Equal(RunStatus.Completed, run.Status);
        Assert.Equal("\"hello Ada\"", JsonText.Of(run.Output));
        Assert.Equal("from a workflow", server.LastMetadata);
    }

    [Fact]
    public async Task A_saved_gRPC_request_can_have_its_message_merged_into()
    {
        await using var server = await FakeServer.StartAsync();
        Services.Requests["Greet"] = new ApiRequest { Kind = ApiKind.Grpc, Address = server.GrpcAddress, Service = "demo.Greeter", Rpc = "One", Message = """{"name": "saved"}""" };
        var step = new WorkflowStep { Type = StepType.Grpc, RequestPath = "Greet", BodyMode = BodyMode.Merge, Body = """{"name": "merged"}""" };

        var run = await RunAsync(new Flow().Then(step).Then(Flow.End("{{last.body.name}}")));

        Assert.Equal("\"hello merged\"", JsonText.Of(run.Output));
    }

    [Fact]
    public async Task A_gRPC_error_or_unknown_method_fails_the_step()
    {
        await using var server = await FakeServer.StartAsync();

        var failing = await RunAsync(new Flow().Then(new WorkflowStep { Type = StepType.Grpc, Address = server.GrpcAddress, Service = "demo.Greeter", Rpc = "Fails" }));
        var unknown = await RunAsync(new Flow().Then(new WorkflowStep { Type = StepType.Grpc, Address = server.GrpcAddress, Service = "demo.Greeter", Rpc = "Nope" }));

        Assert.Equal(RunStatus.Failed, failing.Status);
        Assert.Contains("there is none", failing.Error);
        Assert.Contains("no method demo.Greeter/Nope", unknown.Error);
    }
}
