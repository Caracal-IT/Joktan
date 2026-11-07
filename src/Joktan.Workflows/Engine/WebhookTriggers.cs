using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Joktan.Workflows.Model;
using Joktan.Workflows.Resources;

namespace Joktan.Workflows.Engine;

/// <summary>A request to a webhook address, as the REST API's server read it.</summary>
/// <param name="Path">The path below <see cref="WebhookTriggers.Root"/>, such as <c>orders/new</c>.</param>
/// <param name="Query">What followed the question mark, still encoded; empty for none.</param>
public sealed record WebhookCall(string Method, string Path, string Query, string Body);

/// <summary>What a webhook answers: an HTTP status and a JSON body.</summary>
public sealed record WebhookAnswer(int Status, JsonNode Body);

/// <summary>
/// Webhook triggers: a workflow that switches its webhook on is started by a request to
/// <c>/hooks/&lt;path&gt;</c> on the REST API's server. A POST's JSON body is the run's input, a GET's query is; the
/// request is in <c>{{run.request}}</c>. It answers at once with the run, or when the run has finished, with its output.
/// </summary>
public sealed class WebhookTriggers(WorkflowEngine engine)
{
    public const string Root = "/hooks";

    /// <summary>How long a webhook that answers when the run has finished waits, before it answers with the run under way.</summary>
    public static readonly TimeSpan Wait = TimeSpan.FromSeconds(60);

    /// <summary>A path as webhooks are matched: without slashes at either end, in lower case.</summary>
    public static string Normalize(string path) => path.Trim().Trim('/').ToLowerInvariant();

    /// <summary>What is wrong with a webhook path, or null: it needs letters, digits, dashes, dots and underscores, in parts between slashes.</summary>
    public static string? PathProblem(string path)
    {
        var parts = Normalize(path).Split('/');
        if (parts.Any(p => p.Length == 0))
            return Strings.Check_WebhookNeedsPath;
        return parts.All(p => p is not ("." or "..") && p.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))
            ? null
            : Strings.Check_WebhookPath;
    }

    /// <summary>The workflow a path starts, if one has its webhook switched on there.</summary>
    public Workflow? Find(string path)
    {
        var wanted = Normalize(path);
        return engine.Library.All.FirstOrDefault(w => w.Triggers.Webhook && Normalize(w.Triggers.WebhookPath) == wanted);
    }

    /// <summary>Another workflow using the same webhook path, which would leave one of them never started; or null.</summary>
    public static Workflow? SharedWith(WorkflowLibrary library, Workflow workflow)
        => workflow.Triggers.Webhook
            ? library.All.FirstOrDefault(w => w.Id != workflow.Id && w.Triggers.Webhook && Normalize(w.Triggers.WebhookPath) == Normalize(workflow.Triggers.WebhookPath))
            : null;

    /// <summary>Starts the workflow a webhook names and answers, as the trigger asks: at once, or when the run has finished.</summary>
    public async Task<WebhookAnswer> AnswerAsync(Workflow workflow, WebhookCall call)
    {
        if (!engine.Settings.WebhookTriggers)
            return Problem(503, "Webhook triggers are turned off on Settings / Workflow Settings.");

        JsonNode? input;
        switch (call.Method)
        {
            case "POST":
                try
                {
                    input = call.Body.Trim().Length == 0 ? new JsonObject() : JsonNode.Parse(call.Body);
                }
                catch (JsonException)
                {
                    return Problem(400, "The body must be JSON, which is the run's input.");
                }

                break;
            case "GET":
                input = Query(call.Query);
                break;
            default:
                return Problem(405, "Use POST, with the run's input as JSON, or GET, with it in the query.");
        }

        var request = new JsonObject { ["method"] = call.Method, ["path"] = Normalize(call.Path), ["query"] = Query(call.Query) };
        WorkflowRun run;
        try
        {
            run = engine.Start(workflow, input, RunTrigger.Webhook, $"{Root}/{Normalize(call.Path)}", request: request);
        }
        catch (WorkflowException e)
        {
            return Problem(422, e.Message);
        }

        if (!workflow.Triggers.WebhookWait)
            return new WebhookAnswer(202, Describe(run));
        try
        {
            await engine.WhenFinished(run.Id).WaitAsync(Wait);
        }
        catch (TimeoutException)
        {
            return new WebhookAnswer(202, Describe(run));
        }

        return new WebhookAnswer(run.Status == RunStatus.Completed ? 200 : 500, Describe(run));
    }

    private static JsonObject Describe(WorkflowRun run)
    {
        var body = new JsonObject { ["runId"] = run.Id, ["serial"] = run.Serial, ["status"] = run.Status.ToString() };
        if (run.Status == RunStatus.Completed)
            body["output"] = run.Output?.DeepClone();
        if (run.Error is { } error)
            body["error"] = error;
        return body;
    }

    private static WebhookAnswer Problem(int status, string message) => new(status, new JsonObject { ["error"] = message });

    // The names and values of a query, as text.
    private static JsonObject Query(string query)
    {
        var values = new JsonObject();
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = pair.IndexOf('=');
            var name = Decode(equals < 0 ? pair : pair[..equals]);
            if (name.Length > 0)
                values[name] = equals < 0 ? "" : Decode(pair[(equals + 1)..]);
        }

        return values;
    }

    private static string Decode(string text) => Uri.UnescapeDataString(text.Replace('+', ' '));
}
