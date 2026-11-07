using System;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Joktan.Mqtt;
using Joktan.Workflows.Engine;
using Joktan.Workflows.Model;

namespace Joktan.Workflows.Steps;

/// <summary>MQTT steps: publishing a client's folder message, or one written on the step, and waiting for a message.</summary>
public static class MqttSteps
{
    public static async Task<StepResult> PublishAsync(WorkflowStep step, StepContext context)
    {
        var client = context.Render(step.Client).Trim();
        string topic, body;
        int qos;
        bool retain, gzip = false;
        if (step.MessageFile.Trim().Length > 0)
        {
            var saved = context.Services.ReadFolderMessage(client, step.MessageFile.Trim())
                        ?? throw new StepFailure($"{client} has no folder message {step.MessageFile}.");
            topic = step.Topic.Trim().Length > 0 ? context.Render(step.Topic).Trim() : saved.Topic;
            body = ApiSteps.Body(step, context, saved.Body);
            (qos, retain, gzip) = (saved.Qos, saved.Retain, saved.Gzip);
        }
        else
        {
            topic = context.Render(step.Topic).Trim();
            body = context.Render(step.Body);
            (qos, retain) = (Math.Clamp(step.Qos, 0, 2), step.Retain);
        }

        if (TopicRules.Validate(topic) is { } problem)
            throw new StepFailure($"The topic \"{topic}\" cannot be used: {problem}");
        var bytes = Encoding.UTF8.GetBytes(body);
        var error = await context.Services.PublishAsync(client, topic, gzip ? Gzip.Compress(bytes) : bytes, qos, retain);
        if (error is not null)
            throw new StepFailure($"{client} could not publish to {topic}: {error}");
        context.Log($"Published {bytes.Length} bytes to {topic} from {client}.");
        return StepResult.Next(new JsonObject { ["topic"] = topic, ["payload"] = Templates.Parse(body) });
    }

    public static async Task<StepResult> WaitAsync(WorkflowStep step, StepContext context)
    {
        var client = context.Render(step.Client).Trim();
        var filter = context.Render(step.Topic).Trim();
        if (TopicRules.ValidateFilter(filter) is { } problem)
            throw new StepFailure($"The topic \"{filter}\" cannot be waited on: {problem}");

        DateTime? until = null;
        if (step.Duration.Trim().Length > 0)
        {
            var text = context.Render(step.Duration);
            if (!Durations.TryParse(text, out var duration))
                throw new StepFailure($"\"{text}\" is not a length of time, such as 30s, 5m or 2h.");
            until = context.DeadlineAfter(duration);
        }

        var arrived = new TaskCompletionSource<MqttArrival>(TaskCreationOptions.RunContinuationsAsynchronously);
        var (listener, error) = await context.Services.ListenAsync(client, filter, message => context.Engine.Post(() => _ = OfferAsync(message)));
        if (listener is null)
            throw new StepFailure($"{client} cannot listen on {filter}: {error}");

        context.Log($"Waiting for a message on {filter} at {client}{(until is { } end ? $", until {end:HH:mm:ss}" : "")}.");
        try
        {
            var left = until - context.Engine.Now;
            if (left is { } gone && gone <= TimeSpan.Zero)
                return new StepResult(StepOutputs.Timeout, null, false);
            var message = await context.WaitingAsync(() => left is { } wait
                ? arrived.Task.WaitAsync(wait, context.Cancel)
                : arrived.Task.WaitAsync(context.Cancel));
            return StepResult.Next(message.ToJson());
        }
        catch (TimeoutException)
        {
            return new StepResult(StepOutputs.Timeout, null, false);
        }
        finally
        {
            listener.Dispose();
        }

        // Only a message that passes the step's test, when it has one, ends the wait.
        async Task OfferAsync(MqttArrival message)
        {
            if (arrived.Task.IsCompleted)
                return;
            if (step.Left.Trim().Length > 0)
            {
                var data = (JsonObject)context.Data.DeepClone();
                data[Templates.Message] = message.ToJson();
                try
                {
                    if (!await StepRunner.TestAsync(step, context, data))
                        return;
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    arrived.TrySetException(new StepFailure($"The test of a message failed: {e.Message}"));
                    return;
                }
            }

            arrived.TrySetResult(message);
        }
    }
}
