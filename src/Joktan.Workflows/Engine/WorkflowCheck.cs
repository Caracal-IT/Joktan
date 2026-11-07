using System.Collections.Generic;
using System.Linq;
using Joktan.Mqtt;
using Joktan.Workflows.Model;
using Joktan.Workflows.Resources;

namespace Joktan.Workflows.Engine;

/// <summary>Something wrong with a workflow. A blocking one stops it running; the others are shown on the step.</summary>
public sealed record WorkflowProblem(string? StepId, string Text, bool IsBlocking = false);

/// <summary>Looks over a workflow for what would stop it running, or make a step fail.</summary>
public static class WorkflowCheck
{
    public static IReadOnlyList<WorkflowProblem> Problems(Workflow workflow)
    {
        var problems = new List<WorkflowProblem>();
        var starts = workflow.Steps.Count(s => s.Type == StepType.Start);
        if (starts == 0)
            problems.Add(new(null, Strings.Check_NoStart, true));
        else if (starts > 1)
            problems.Add(new(null, Strings.Check_MoreThanOneStart, true));

        var ids = workflow.Steps.Select(s => s.Id).ToHashSet();
        if (workflow.Links.Any(l => !ids.Contains(l.From) || !ids.Contains(l.To)))
            problems.Add(new(null, Strings.Check_LinkToNothing));

        // A loop whose body never comes back goes round once.
        foreach (var loop in workflow.Steps.Where(s => s.Type is StepType.ForEach or StepType.While))
        {
            if (workflow.Links.FirstOrDefault(l => l.From == loop.Id && l.Output == StepOutputs.Loop) is { } body && !Leads(workflow, body.To, loop.Id))
                problems.Add(new(loop.Id, Strings.Check_LoopDoesNotComeBack));
        }

        // Steps that no link leads to, other than the start, never run.
        var reached = Reachable(workflow);
        foreach (var step in workflow.Steps.Where(s => s.Type != StepType.Start && !reached.Contains(s.Id)))
            problems.Add(new(step.Id, Strings.Check_NeverReached));

        foreach (var step in workflow.Steps)
        {
            if (StepProblem(step) is { } problem)
                problems.Add(new(step.Id, problem));
            if (step.Retries > 0 && !Durations.TryParse(step.RetryDelay, out _) && !Templates.PathsIn(step.RetryDelay).Any())
                problems.Add(new(step.Id, Strings.Check_RetryDelayNotReadable));
        }

        var names = workflow.Globals.Select(g => g.Name.Trim()).ToList();
        if (names.Any(n => !IsName(n)))
            problems.Add(new(null, Strings.Check_GlobalName));
        if (names.Where(n => n.Length > 0).GroupBy(n => n).Any(g => g.Count() > 1))
            problems.Add(new(null, Strings.Check_GlobalTwice));

        if (workflow.Triggers.Mqtt)
        {
            if (workflow.Triggers.MqttClient.Trim().Length == 0)
                problems.Add(new(null, Strings.Check_TriggerNeedsClient));
            if (TopicRules.ValidateFilter(workflow.Triggers.MqttTopic.Trim()) is { } topicProblem)
                problems.Add(new(null, string.Format(Strings.Check_TriggerTopic, topicProblem)));
        }

        if (workflow.Triggers.Scheduled && workflow.Triggers.ScheduleInput.Trim() is { Length: > 0 } sample
            && workflow.Samples.All(s => s.Name != sample))
            problems.Add(new(null, string.Format(Strings.Check_ScheduleInput, sample)));

        return problems;
    }

    /// <summary>What a step lacks to work, or null.</summary>
    public static string? StepProblem(WorkflowStep step) => step.Type switch
    {
        StepType.SetVariable when step.Variable.Trim().Length == 0 => Strings.Check_NameTheVariable,
        StepType.Condition or StepType.While or StepType.Switch when step.Left.Trim().Length == 0 => Strings.Check_ConditionNeedsLeft,
        StepType.ForEach when step.Items.Trim().Length == 0 => Strings.Check_ForEachNeedsItems,
        StepType.Switch when step.Cases.All(c => c.Trim().Length == 0) => Strings.Check_SwitchNeedsCases,
        StepType.Delay when !Durations.TryParse(step.Duration, out _) && !Templates.PathsIn(step.Duration).Any() => Strings.Check_DelayNeedsDuration,
        StepType.Http when step.RequestPath.Trim().Length == 0 && step.Url.Trim().Length == 0 => Strings.Check_HttpNeedsRequest,
        StepType.Grpc when step.RequestPath.Trim().Length == 0 && (step.Address.Trim().Length == 0 || step.Service.Trim().Length == 0 || step.Rpc.Trim().Length == 0) => Strings.Check_GrpcNeedsRequest,
        StepType.MqttPublish when step.Client.Trim().Length == 0 => Strings.Check_ChooseClient,
        StepType.MqttPublish when step.MessageFile.Trim().Length == 0 && step.Topic.Trim().Length == 0 => Strings.Check_PublishNeedsTopic,
        StepType.MqttWait when step.Client.Trim().Length == 0 => Strings.Check_ChooseClient,
        StepType.MqttWait when step.Topic.Trim().Length == 0 => Strings.Check_WaitNeedsTopic,
        StepType.Script when step.Code.Trim().Length == 0 => Strings.Check_ScriptNeedsCode,
        StepType.CallWorkflow when step.Workflow.Trim().Length == 0 => Strings.Check_ChooseWorkflow,
        StepType.UserInput when step.Fields.Count == 0 && step.Message.Trim().Length == 0 => Strings.Check_AskSomething,
        StepType.UserInput when step.Metadata.Any(m => m.Name.Trim().Length == 0 && m.Value.Trim().Length > 0) => Strings.Check_MetadataNeedsName,
        StepType.UserInput when step.Metadata.Any(m => Steps.StepRunner.RunMetadata.Contains(m.Name.Trim())) => Strings.Check_MetadataReserved,
        StepType.UserInput when step.Metadata.Select(m => m.Name.Trim()).Where(n => n.Length > 0).GroupBy(n => n).Any(g => g.Count() > 1) => Strings.Check_MetadataTwice,
        _ when StepTypes.IsDatabase(step.Type) => DatabaseProblem(step),
        _ when step.Duration.Trim().Length > 0 && step.Type is StepType.MqttWait or StepType.UserInput
                                              && !Durations.TryParse(step.Duration, out _) && !Templates.PathsIn(step.Duration).Any() => Strings.Check_DurationNotReadable,
        _ => null,
    };

    private static string? DatabaseProblem(WorkflowStep step)
    {
        var named = step.Columns.Where(c => c.Column.Trim().Length > 0).ToList();
        var columns = step.Type == StepType.DatabaseRead ? named : named.Where(c => c.SetsValue).ToList();
        var filters = step.Filters.Where(f => f.Column.Trim().Length > 0).ToList();
        if (step.Database.Trim().Length == 0)
            return Strings.Check_ChooseDatabase;
        if (step.Table.Trim().Length == 0)
            return Strings.Check_ChooseTable;
        if (filters.Any(f => f.Operator == CompareOperator.CSharp))
            return Strings.Check_FilterCannotBeCSharp;
        if (named.Any(c => c.Variable.Trim().Length > 0 && !IsName(c.Variable.Trim())))
            return Strings.Check_ColumnVariableName;
        switch (step.Type)
        {
            case StepType.DatabaseInsert or StepType.DatabaseUpsert when step.ManyRows && step.Items.Trim().Length == 0:
                return Strings.Check_InsertNeedsRows;
            case StepType.DatabaseInsert or StepType.DatabaseUpdate or StepType.DatabaseUpsert when columns.GroupBy(c => c.Column.Trim()).Any(g => g.Count() > 1):
                return Strings.Check_ColumnTwice;
            case StepType.DatabaseUpsert when !columns.Any(c => c.Match):
                return Strings.Check_UpsertNeedsKey;
            case StepType.DatabaseUpsert when columns.Any(c => c.Match && c.Value.Trim().Length == 0):
                return Strings.Check_KeyNeedsValue;
            case StepType.DatabaseUpdate when columns.Count == 0:
                return Strings.Check_UpdateNeedsColumns;
            case StepType.DatabaseUpdate or StepType.DatabaseDelete:
                return NarrowingProblem(filters);
            default:
                return null;
        }
    }

    // An update or delete must say which rows with filters whose values are all there, one of them a comparison with a value.
    private static string? NarrowingProblem(List<DatabaseFilter> filters)
    {
        if (filters.Count == 0)
            return Strings.Check_NeedsFilter;
        if (filters.Any(f => Conditions.TakesRight(f.Operator) && f.Value.Trim().Length == 0))
            return Strings.Check_FilterNeedsValue;
        return filters.Any(f => Conditions.TakesRight(f.Operator)) ? null : Strings.Check_FilterNeedsComparison;
    }

    /// <summary>Whether a name can be a variable that paths reach: letters, digits and underscores, not starting with a digit.</summary>
    public static bool IsName(string name)
        => name.Length > 0 && !char.IsDigit(name[0]) && name.All(c => char.IsLetterOrDigit(c) || c == '_');

    // Whether following links from one step reaches another.
    private static bool Leads(Workflow workflow, string from, string to)
    {
        var seen = new HashSet<string>();
        var queue = new Queue<string>([from]);
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (id == to)
                return true;
            if (!seen.Add(id))
                continue;
            foreach (var link in workflow.Links.Where(l => l.From == id))
                queue.Enqueue(link.To);
        }

        return false;
    }

    private static HashSet<string> Reachable(Workflow workflow)
    {
        var reached = new HashSet<string>();
        var queue = new Queue<string>(workflow.Steps.Where(s => s.Type == StepType.Start).Select(s => s.Id));
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (!reached.Add(id))
                continue;
            foreach (var link in workflow.Links.Where(l => l.From == id))
                queue.Enqueue(link.To);
        }

        return reached;
    }
}
