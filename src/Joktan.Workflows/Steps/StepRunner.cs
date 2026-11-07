using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Joktan.Mqtt.Proxy;
using Joktan.Workflows.Engine;
using Joktan.Workflows.Model;

namespace Joktan.Workflows.Steps;

/// <summary>What a step gave: the output to leave by, and its result.</summary>
/// <param name="KeepsResult">Whether the result goes in <c>last</c> and the step's variable; steps that only steer, such as a condition, leave them.</param>
public sealed record StepResult(string Output, JsonNode? Value = null, bool KeepsResult = true)
{
    public static StepResult Next(JsonNode? value = null) => new(StepOutputs.Next, value);
}

/// <summary>Runs one step of a run.</summary>
public static class StepRunner
{
    public static Task<StepResult> RunAsync(WorkflowStep step, StepContext context) => step.Type switch
    {
        StepType.Start => Task.FromResult(new StepResult(StepOutputs.Next, context.Data[Templates.Input]?.DeepClone())),
        StepType.End => Task.FromResult(new StepResult(StepOutputs.Next, step.Value.Trim().Length == 0 ? context.Data[Templates.Last]?.DeepClone() : context.Value(step.Value))),
        StepType.Fail => Task.FromResult(new StepResult(StepOutputs.Next, JsonValue.Create(context.Render(step.Message)), false)),
        StepType.Log => Task.FromResult(Log(step, context)),
        StepType.SetVariable => Task.FromResult(SetVariable(step, context)),
        StepType.Condition => ConditionAsync(step, context),
        StepType.Delay => DelayAsync(step, context),
        StepType.UserInput => AskAsync(step, context),
        StepType.CallWorkflow => CallAsync(step, context),
        StepType.Http => ApiSteps.HttpAsync(step, context),
        StepType.Grpc => ApiSteps.GrpcAsync(step, context),
        StepType.MqttPublish => MqttSteps.PublishAsync(step, context),
        StepType.MqttWait => MqttSteps.WaitAsync(step, context),
        StepType.Script => context.Engine.Scripts.RunAsync(step, context),
        StepType.ForEach => Task.FromResult(ForEach(step, context)),
        StepType.While => WhileAsync(step, context),
        StepType.Switch => Task.FromResult(Switch(step, context)),
        _ => throw new StepFailure($"{step.Type} steps cannot run."),
    };

    private static StepResult Log(WorkflowStep step, StepContext context)
    {
        var text = context.Render(step.Message);
        context.Log(text);
        return new StepResult(StepOutputs.Next, JsonValue.Create(text), false);
    }

    private static StepResult SetVariable(WorkflowStep step, StepContext context)
    {
        var name = step.Variable.Trim();
        if (name.Length == 0)
            throw new StepFailure("Name the variable to set.");
        var value = context.Value(step.Value);
        if (step.MergeValue)
        {
            if (value is not JsonObject)
                throw new StepFailure("Only a JSON object can be merged into a variable.");
            value = JsonMerge.Merge(context.Vars[name]?.DeepClone(), value);
        }

        context.Vars[name] = value;
        return new StepResult(StepOutputs.Next, value?.DeepClone(), false);
    }

    private static async Task<StepResult> ConditionAsync(WorkflowStep step, StepContext context)
    {
        var outcome = await TestAsync(step, context, context.Data);
        return new StepResult(outcome ? StepOutputs.True : StepOutputs.False, JsonValue.Create(outcome), false);
    }

    /// <summary>Whether the data passes the step's comparison, or its C#.</summary>
    public static async Task<bool> TestAsync(WorkflowStep step, StepContext context, JsonObject data)
    {
        if (step.Operator == CompareOperator.CSharp)
            return Conditions.IsTrue(await context.Engine.Scripts.EvaluateAsync(step.Left, data, context));
        return Conditions.Compare(Templates.Value(step.Left, data), step.Operator, Templates.Value(step.Right, data));
    }

    // Each time it is reached, the next item, until there are none.
    private static StepResult ForEach(WorkflowStep step, StepContext context)
    {
        var loops = context.Run.Loops;
        if (!loops.TryGetValue(step.Id, out var state))
        {
            state = new LoopState { Items = Items(context.Value(step.Items), step.Items) };
            loops[step.Id] = state;
        }

        var items = state.Items ?? [];
        if (state.Index >= items.Count)
        {
            loops.Remove(step.Id);
            return new StepResult(StepOutputs.Done, JsonValue.Create(state.Index), false);
        }

        var item = items[state.Index]?.DeepClone();
        if (step.ItemVariable.Trim() is { Length: > 0 } variable)
            context.Vars[variable] = item?.DeepClone();
        if (step.IndexVariable.Trim() is { Length: > 0 } index)
            context.Vars[index] = state.Index;
        state.Index++;
        return new StepResult(StepOutputs.Loop, item, false);
    }

    // A list as it is, an object as its entries, a number as that many counts from 0.
    private static JsonArray Items(JsonNode? value, string written) => value switch
    {
        null => [],
        JsonArray list => (JsonArray)list.DeepClone(),
        JsonObject entries => new JsonArray(entries.Select(e => (JsonNode?)new JsonObject { ["key"] = e.Key, ["value"] = e.Value?.DeepClone() }).ToArray()),
        JsonValue number when double.TryParse(number.ToJsonString().Trim('"'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var count)
                              && count >= 0 && count == Math.Floor(count)
            => new JsonArray(Enumerable.Range(0, (int)Math.Min(count, int.MaxValue)).Select(i => (JsonNode?)JsonValue.Create(i)).ToArray()),
        _ => throw new StepFailure($"{written} is not a list, an object or a whole number, so it cannot be gone through."),
    };

    // Round again while the test holds, counting the times.
    private static async Task<StepResult> WhileAsync(WorkflowStep step, StepContext context)
    {
        var loops = context.Run.Loops;
        if (!loops.TryGetValue(step.Id, out var state))
            loops[step.Id] = state = new LoopState();

        if (!await TestAsync(step, context, context.Data))
        {
            loops.Remove(step.Id);
            return new StepResult(StepOutputs.Done, JsonValue.Create(state.Index), false);
        }

        if (step.IndexVariable.Trim() is { Length: > 0 } index)
            context.Vars[index] = state.Index;
        state.Index++;
        return new StepResult(StepOutputs.Loop, JsonValue.Create(state.Index - 1), false);
    }

    // The first case equal to the value, as a condition's equals compares.
    private static StepResult Switch(WorkflowStep step, StepContext context)
    {
        var value = context.Value(step.Left);
        foreach (var @case in step.Cases.Where(c => c.Trim().Length > 0).Distinct())
        {
            if (Conditions.Compare(value, CompareOperator.Equals, context.Value(@case)))
                return new StepResult(StepOutputs.Case(@case), value?.DeepClone(), false);
        }

        return new StepResult(StepOutputs.Default, value?.DeepClone(), false);
    }

    private static async Task<StepResult> DelayAsync(WorkflowStep step, StepContext context)
    {
        var text = context.Render(step.Duration);
        if (!Durations.TryParse(text, out var duration))
            throw new StepFailure($"\"{text}\" is not a length of time, such as 30s, 5m or 2h.");

        // A delay carried over a restart ends when it would have.
        var until = context.Run.WaitUntil ?? context.Engine.Now + duration;
        await context.DelayUntilAsync(until);
        return new StepResult(StepOutputs.Next, null, false);
    }

    private static async Task<StepResult> AskAsync(WorkflowStep step, StepContext context)
    {
        DateTime? until = null;
        if (step.Duration.Trim().Length > 0)
        {
            var text = context.Render(step.Duration);
            if (!Durations.TryParse(text, out var limit))
                throw new StepFailure($"\"{text}\" is not a length of time, such as 30s, 5m or 2h.");
            until = context.DeadlineAfter(limit);
        }

        var question = new PendingInput
        {
            StepId = step.Id,
            Message = context.Render(step.Message),
            Fields = step.Fields.Where(f => f.Name.Trim().Length > 0).Select(f => new InputField
            {
                Name = f.Name.Trim(),
                Label = f.Label.Trim().Length > 0 ? f.Label.Trim() : f.Name.Trim(),
                Default = context.Render(f.Default),
            }).ToList(),
            Until = until,
        };
        var answer = await context.AskAsync(question);
        if (answer is null)
            return new StepResult(StepOutputs.Timeout, null, false);
        context.Log($"Answered: {JsonText.Of(answer)}");
        return StepResult.Next(answer);
    }

    private static async Task<StepResult> CallAsync(WorkflowStep step, StepContext context)
    {
        var engine = context.Engine;
        var workflow = engine.Library.Find(step.Workflow)
                       ?? throw new StepFailure($"There is no workflow {step.Workflow}; it may have been deleted.");
        if (workflow.Id == context.Run.WorkflowId && step.Wait)
            throw new StepFailure("A workflow cannot wait on itself.");

        // After a restart, the run already started is waited on rather than another one started.
        var child = context.Run.WaitingOn is { } waitingOn && engine.Find(waitingOn) is { Interrupted: false } started ? started : null;
        if (child is null)
        {
            if (Depth(context) >= WorkflowSettings.MaxCallDepth)
                throw new StepFailure($"Workflows are calling workflows more than {WorkflowSettings.MaxCallDepth} deep, so they may be calling each other for ever.");
            var input = step.Input.Trim().Length == 0 ? new JsonObject() : context.Value(step.Input);
            try
            {
                child = engine.Start(workflow, input, RunTrigger.Workflow, context.Run.Id, context.Run.Id);
            }
            catch (WorkflowException e)
            {
                throw new StepFailure(e.Message);
            }

            context.Log($"Started {workflow.Name}, run {child.Id}{(step.Wait ? ", and waiting for it" : "")}.");
            if (!step.Wait)
                return StepResult.Next(new JsonObject { ["runId"] = child.Id, ["workflow"] = workflow.Name });
            context.Run.WaitingOn = child.Id;
            if (context.Run.Kind == WorkflowKind.Long)
                engine.Store.Save(context.Run);
        }
        else
        {
            context.Log($"Waiting again for {workflow.Name}, run {child.Id}.");
        }

        WorkflowRun finished;
        try
        {
            finished = await context.WaitingAsync(() => engine.WhenFinished(child.Id).WaitAsync(context.Cancel));
        }
        catch (OperationCanceledException) when (!context.IsShuttingDown)
        {
            // What this run waited on goes with it, unless Joktan is only closing.
            engine.Kill(child.Id);
            throw;
        }

        return finished.Status == RunStatus.Completed
            ? StepResult.Next(finished.Output?.DeepClone())
            : throw new StepFailure($"{workflow.Name} (run {finished.Id}) {(finished.Status == RunStatus.Killed ? "was killed" : $"failed: {finished.Error}")}");
    }

    // How many runs started this one, one from another.
    private static int Depth(StepContext context)
    {
        var depth = 0;
        for (var parent = context.Run.ParentRunId; parent is not null && depth <= WorkflowSettings.MaxCallDepth; depth++)
            parent = context.Engine.Find(parent)?.ParentRunId;
        return depth;
    }
}
