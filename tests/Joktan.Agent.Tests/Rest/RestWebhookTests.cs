using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Joktan.Agent.Rest;
using Joktan.Tests.TestSupport;
using Joktan.Workflows;
using Joktan.Workflows.Engine;
using Joktan.Workflows.Tests;
using Xunit;

namespace Joktan.Agent.Tests.Rest;

[Collection(ProcessStateCollection.Name)]
public sealed class RestWebhookTests : DataFolderTest
{
    private readonly RestApiSettings _settings = new();
    private readonly string _secret;
    private readonly WorkflowEngine _engine;
    private readonly RestApi _api;

    public RestWebhookTests()
    {
        var (key, secret) = ApiKey.Create("Shop", null, DateTimeOffset.Now);
        _settings.Keys.Add(key);
        _secret = secret;
        _engine = new WorkflowEngine(new WorkflowLibrary(Data.At("definitions")), new WorkflowSettings(), new FakeWorkflowServices(), new RunStore(Data.At("runs")));
        _api = new RestApi(_settings, () => []) { Webhooks = new WebhookTriggers(_engine) };
    }

    protected override void Dispose(bool disposing)
    {
        _engine.Dispose();
        base.Dispose(disposing);
    }

    private void Hooked(bool needsKey, bool wait = true)
    {
        var flow = new Flow("Orders").Then(Flow.End("""{"got": {{input.order}}}"""));
        flow.Workflow.Triggers.Webhook = true;
        flow.Workflow.Triggers.WebhookPath = "orders/new";
        flow.Workflow.Triggers.WebhookNeedsKey = needsKey;
        flow.Workflow.Triggers.WebhookWait = wait;
        _engine.Library.Save(flow.Workflow);
    }

    private Task<RestReply> Post(string path, string body, bool withKey = true, string host = "localhost:47821")
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Host"] = host };
        if (withKey)
            headers["Authorization"] = $"Bearer {_secret}";
        return _api.AnswerAsync(new RestRequest("POST", path, "", headers, body));
    }

    [Fact]
    public async Task A_webhook_starts_its_workflow_and_answers_with_the_output()
    {
        Hooked(needsKey: true);

        var reply = await Post("/hooks/Orders/New", """{"order": 5}""");

        Assert.Equal(200, reply.Status);
        Assert.Equal(5, (int)reply.Body!["output"]!["got"]!);
    }

    [Fact]
    public async Task One_that_needs_a_key_turns_away_a_request_without_one()
    {
        Hooked(needsKey: true);

        Assert.Equal(401, (await Post("/hooks/orders/new", "{}", withKey: false)).Status);
        Assert.Empty(_engine.Runs);
    }

    [Fact]
    public async Task One_that_needs_no_key_answers_anyone_who_can_reach_the_server()
    {
        Hooked(needsKey: false, wait: false);

        var reply = await Post("/hooks/orders/new", """{"order": 1}""", withKey: false);

        Assert.Equal(202, reply.Status);
        Assert.Single(_engine.Runs);
    }

    [Fact]
    public async Task Other_computers_and_paths_no_workflow_has_are_turned_away()
    {
        Hooked(needsKey: false);

        Assert.Equal(404, (await Post("/hooks/nothing", "{}")).Status);
        Assert.Equal(403, (await Post("/hooks/orders/new", "{}", withKey: false, host: "shop.example.com")).Status);

        // Without the workflows, there are no webhooks.
        _api.Webhooks = null;
        Assert.Equal(404, (await Post("/hooks/orders/new", "{}")).Status);
        Assert.Empty(_engine.Runs);
    }
}
