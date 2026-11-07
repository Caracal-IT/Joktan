using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Joktan.Api;
using Joktan.Core;
using Joktan.Mqtt.Proxy;
using Joktan.Workflows.Engine;
using Joktan.Workflows.Model;

namespace Joktan.Workflows.Steps;

/// <summary>REST and gRPC steps: a request saved in the API Client, with its body kept, replaced or merged into, or one written on the step.</summary>
public static class ApiSteps
{
    // Services already found, by where they came from, as the API Client keeps them.
    private static readonly ConcurrentDictionary<string, GrpcSchema> Schemas = new();

    public static async Task<StepResult> HttpAsync(WorkflowStep step, StepContext context)
    {
        var request = Request(step, context, ApiKind.Rest);
        if (step.RequestPath.Trim().Length == 0)
        {
            request.Method = step.Method.Trim().Length > 0 ? step.Method.Trim().ToUpperInvariant() : "GET";
            request.Url = context.Render(step.Url);
            request.Headers = step.Headers.Where(h => h.Name.Trim().Length > 0)
                .Select(h => new ApiPair { Name = context.Render(h.Name), Value = context.Render(h.Value) }).ToList();
            var body = context.Render(step.Body);
            request.Body = body;
            request.BodyKind = body.Trim().Length == 0 ? ApiBodyKind.None : RestSender.TryIndentJson(body) is null ? ApiBodyKind.Text : ApiBodyKind.Json;
        }
        else
        {
            request.Url = context.Render(request.Url);
            request.Body = Body(step, context, request.Body);
            if (step.BodyMode != BodyMode.Keep && request.BodyKind == ApiBodyKind.None)
                request.BodyKind = RestSender.TryIndentJson(request.Body) is null ? ApiBodyKind.Text : ApiBodyKind.Json;
        }

        context.Log($"{request.Method} {request.Url}");
        var response = await RestSender.SendAsync(request, context.Services.Variables(), context.Cancel);
        context.Cancel.ThrowIfCancellationRequested();
        var result = Result(response);
        if (response.Status.Length > 0 && !char.IsDigit(response.Status[0]))
            throw new StepFailure(response.Status);
        if (!response.IsSuccess && step.FailOnErrorStatus)
            throw new StepFailure($"The answer was {response.Status}: {Short(response.Body)}");
        return StepResult.Next(result);
    }

    public static async Task<StepResult> GrpcAsync(WorkflowStep step, StepContext context)
    {
        var request = Request(step, context, ApiKind.Grpc);
        if (step.RequestPath.Trim().Length == 0)
        {
            request.Address = context.Render(step.Address);
            request.Service = context.Render(step.Service).Trim();
            request.Rpc = context.Render(step.Rpc).Trim();
            request.Schema = GrpcSchemaSource.Reflection;
            request.Message = step.Body.Trim().Length == 0 ? "{}" : context.Render(step.Body);
            request.Metadata = step.Headers.Where(h => h.Name.Trim().Length > 0)
                .Select(h => new ApiPair { Name = context.Render(h.Name), Value = context.Render(h.Value) }).ToList();
        }
        else
        {
            request.Address = context.Render(request.Address);
            request.Message = Body(step, context, request.Message);
        }

        var variables = context.Services.Variables();
        GrpcSchema schema;
        try
        {
            schema = await SchemaAsync(request, variables, context.Cancel);
        }
        catch (Exception e) when (e is GrpcSchemaException or InvalidOperationException or System.IO.IOException or RpcException or System.Net.Http.HttpRequestException
                                      || (e is OperationCanceledException && !context.Cancel.IsCancellationRequested))
        {
            throw new StepFailure($"The services of {request.Address} could not be found: {(e is OperationCanceledException ? "the server did not answer" : RestSender.Message(e))}");
        }

        var method = schema.FindMethod(request.Service, request.Rpc)
                     ?? throw new StepFailure($"{request.Address} has no method {request.Service}/{request.Rpc}.");
        context.Log($"gRPC {request.Service}/{request.Rpc} at {request.Address}");
        var response = await GrpcCaller.CallAsync(request, method, variables, context.Cancel, schema.Types);
        context.Cancel.ThrowIfCancellationRequested();
        return response.IsSuccess
            ? StepResult.Next(Result(response))
            : throw new StepFailure($"The call failed: {response.Status} {Short(response.Body)}".Trim());
    }

    // The saved request, a copy so the file is never changed, or an empty one for a request written on the step.
    private static ApiRequest Request(WorkflowStep step, StepContext context, ApiKind kind)
    {
        if (step.RequestPath.Trim().Length == 0)
            return new ApiRequest { Kind = kind, TimeoutSeconds = context.Run.Kind == WorkflowKind.Short ? context.Engine.Settings.ShortTimeLimitSeconds : 100 };
        var saved = context.Services.ReadRequest(step.RequestPath.Trim())
                    ?? throw new StepFailure($"The API Client has no request {step.RequestPath}.");
        if (saved.Kind != kind)
            throw new StepFailure($"{step.RequestPath} is a {(saved.Kind == ApiKind.Rest ? "REST" : "gRPC")} request.");
        return JsonSerializer.Deserialize<ApiRequest>(JsonSerializer.Serialize(saved))!;
    }

    /// <summary>The body to send: the saved one, the step's, or the step's merged into the saved one.</summary>
    public static string Body(WorkflowStep step, StepContext context, string saved)
    {
        saved = context.Render(saved);
        switch (step.BodyMode)
        {
            case BodyMode.Replace:
                return context.Render(step.Body);
            case BodyMode.Merge:
                var overlay = context.Value(step.Body);
                if (overlay is not JsonObject)
                    throw new StepFailure("To merge into the saved body, the step's body must be a JSON object.");
                JsonNode? target;
                try
                {
                    target = saved.Trim().Length == 0 ? new JsonObject() : JsonNode.Parse(saved);
                }
                catch (JsonException)
                {
                    throw new StepFailure("The saved body is not JSON, so nothing can be merged into it.");
                }

                return JsonMerge.Merge(target, overlay)?.ToJsonString(JsonFormats.Compact) ?? "";
            default:
                return saved;
        }
    }

    private static JsonObject Result(ApiResponse response)
    {
        var headers = new JsonObject();
        foreach (var header in response.Headers.Concat(response.Trailers))
            headers[header.Name.ToLowerInvariant()] = header.Value;
        var code = int.TryParse(response.Status.Split(' ')[0], out var number) ? number : (int?)null;
        return new JsonObject
        {
            ["status"] = response.Status,
            ["code"] = code,
            ["ok"] = response.IsSuccess,
            ["body"] = Templates.Parse(response.Body),
            ["headers"] = headers,
            ["elapsedMs"] = (long)response.Elapsed.TotalMilliseconds,
        };
    }

    private static string Short(string text) => text.Length > 300 ? text[..300] + "…" : text;

    private static async Task<GrpcSchema> SchemaAsync(ApiRequest request, ApiVariables variables, CancellationToken cancel)
    {
        var key = request.Schema == GrpcSchemaSource.Reflection
            ? "reflection " + variables.Apply(request.Address)
            : "protos " + string.Join("|", request.ProtoFiles) + "|" + string.Join("|", request.ImportPaths);
        if (Schemas.TryGetValue(key, out var known))
            return known;

        GrpcSchema schema;
        if (request.Schema == GrpcSchemaSource.Reflection)
        {
            using var channel = GrpcCaller.Channel(request, variables);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, request.TimeoutSeconds)));
            schema = await GrpcSchema.FromServerAsync(channel, GrpcCaller.Headers(request, variables), timeout.Token);
        }
        else
        {
            schema = await Task.Run(() => GrpcSchema.FromProtoFiles(request.ProtoFiles, request.ImportPaths), cancel);
        }

        Schemas[key] = schema;
        return schema;
    }
}
