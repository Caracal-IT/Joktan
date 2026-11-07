using System.Collections.Generic;
using System.Linq;
using Joktan.Workflows.Engine;
using Joktan.Workflows.Resources;

namespace Joktan.Workflows.Model;

/// <summary>A line saying what a step does, drawn under its name on the canvas.</summary>
public static class StepSummary
{
    /// <param name="workflow">The workflow the step is in, whose triggers a start step lists.</param>
    public static string Of(WorkflowStep step, WorkflowLibrary? library = null, Workflow? workflow = null) => step.Type switch
    {
        StepType.Start => workflow is null ? Strings.Summary_Start : Triggers(workflow.Triggers),
        StepType.End => step.Value.Trim().Length == 0 ? Strings.Summary_EndWithLast : string.Format(Strings.Summary_Gives, OneLine(step.Value)),
        StepType.SetVariable => $"vars.{step.Variable} {(step.MergeValue ? "+=" : "=")} {OneLine(step.Value)}",
        StepType.Condition => step.Operator == CompareOperator.CSharp
            ? OneLine(step.Left)
            : $"{OneLine(step.Left)} {step.Operator.Text()}{(Conditions.TakesRight(step.Operator) ? " " + OneLine(step.Right) : "")}",
        StepType.Delay => string.Format(Strings.Summary_Wait, step.Duration),
        StepType.Http => step.RequestPath.Length > 0 ? step.RequestPath : $"{step.Method} {step.Url}",
        StepType.Grpc => step.RequestPath.Length > 0 ? step.RequestPath : $"{step.Service}/{step.Rpc}",
        StepType.MqttPublish => $"{step.Client} → {(step.MessageFile.Length > 0 ? step.MessageFile : step.Topic)}",
        StepType.MqttWait => $"{step.Client} ← {step.Topic}",
        StepType.Script => $"{step.Language.Text()}: {OneLine(step.Code.Split('\n').FirstOrDefault(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith("//", System.StringComparison.Ordinal)) ?? "")}",
        StepType.CallWorkflow => string.Format(step.Wait ? Strings.Summary_CallsAndWaits : Strings.Summary_Calls,
            library?.Find(step.Workflow)?.Name ?? step.Workflow),
        StepType.UserInput or StepType.Log or StepType.Fail => OneLine(step.Message),
        StepType.ForEach => string.Format(Strings.Summary_ForEach, step.ItemVariable, OneLine(step.Items)),
        StepType.While => string.Format(Strings.Summary_While, Of(new WorkflowStep { Type = StepType.Condition, Left = step.Left, Operator = step.Operator, Right = step.Right })),
        StepType.Switch => string.Format(Strings.Summary_Switch, OneLine(step.Left)),
        _ => step.Type.Text(),
    };

    /// <summary>What starts a workflow: by hand always, then the REST API and an MQTT topic when switched on.</summary>
    public static string Triggers(WorkflowTriggers triggers)
    {
        var parts = new List<string> { Strings.RunTrigger_Manual };
        if (triggers.Rest)
            parts.Add(Strings.RunTrigger_Rest);
        if (triggers.Mqtt)
            parts.Add(string.Format(Strings.Summary_MqttTrigger, triggers.MqttTopic));
        return string.Join(" · ", parts);
    }

    private static string OneLine(string text) => string.Join(" ", text.Split((char[])['\n', '\r'], System.StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()));
}
