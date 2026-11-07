using System.Collections.Generic;
using System.Linq;
using Avalonia.Data.Converters;
using Joktan.Workflows.Resources;

namespace Joktan.Workflows.Model;

/// <summary>The kinds of step: their outputs, names and group on the palette.</summary>
public static class StepTypes
{
    /// <summary>The palette's groups, in order.</summary>
    public enum Group
    {
        Flow,
        Data,
        Messages,
        Code,
    }

    public static readonly IReadOnlyList<StepType> All = System.Enum.GetValues<StepType>();

    /// <summary>The outputs a step has, in the order they are drawn: a switch's are its cases, then default.</summary>
    public static IReadOnlyList<string> Outputs(WorkflowStep step) => step.Type == StepType.Switch
        ? [.. step.Cases.Where(c => c.Trim().Length > 0).Distinct().Select(StepOutputs.Case), StepOutputs.Default, StepOutputs.Error]
        : Outputs(step.Type);

    /// <summary>The outputs a step of a type has, in the order they are drawn down its right side.</summary>
    public static IReadOnlyList<string> Outputs(StepType type) => type switch
    {
        StepType.End or StepType.Fail => [],
        StepType.Start or StepType.Delay => [StepOutputs.Next],
        StepType.Condition => [StepOutputs.True, StepOutputs.False, StepOutputs.Error],
        StepType.ForEach or StepType.While => [StepOutputs.Loop, StepOutputs.Done, StepOutputs.Error],
        StepType.Switch => [StepOutputs.Default, StepOutputs.Error],
        StepType.MqttWait or StepType.UserInput => [StepOutputs.Next, StepOutputs.Timeout, StepOutputs.Error],
        _ => [StepOutputs.Next, StepOutputs.Error],
    };

    /// <summary>Whether other steps can lead to a step of this type: all but the start.</summary>
    public static bool HasInput(StepType type) => type != StepType.Start;

    public static Group GroupOf(StepType type) => type switch
    {
        StepType.Start or StepType.End or StepType.Condition or StepType.Switch or StepType.ForEach or StepType.While or StepType.Delay
            or StepType.CallWorkflow or StepType.UserInput or StepType.Fail => Group.Flow,
        StepType.SetVariable or StepType.Log => Group.Data,
        StepType.Http or StepType.Grpc or StepType.MqttPublish or StepType.MqttWait => Group.Messages,
        _ => Group.Code,
    };

    /// <summary>What a step type is called on screen.</summary>
    public static string Text(this StepType type) => type switch
    {
        StepType.Start => Strings.StepType_Start,
        StepType.End => Strings.StepType_End,
        StepType.SetVariable => Strings.StepType_SetVariable,
        StepType.Condition => Strings.StepType_Condition,
        StepType.Delay => Strings.StepType_Delay,
        StepType.Http => Strings.StepType_Http,
        StepType.Grpc => Strings.StepType_Grpc,
        StepType.MqttPublish => Strings.StepType_MqttPublish,
        StepType.MqttWait => Strings.StepType_MqttWait,
        StepType.Script => Strings.StepType_Script,
        StepType.CallWorkflow => Strings.StepType_CallWorkflow,
        StepType.UserInput => Strings.StepType_UserInput,
        StepType.Log => Strings.StepType_Log,
        StepType.Fail => Strings.StepType_Fail,
        StepType.ForEach => Strings.StepType_ForEach,
        StepType.While => Strings.StepType_While,
        StepType.Switch => Strings.StepType_Switch,
        _ => type.ToString(),
    };

    /// <summary>What a step type does, for the palette's tips.</summary>
    public static string Tip(this StepType type) => type switch
    {
        StepType.Start => Strings.StepTip_Start,
        StepType.End => Strings.StepTip_End,
        StepType.SetVariable => Strings.StepTip_SetVariable,
        StepType.Condition => Strings.StepTip_Condition,
        StepType.Delay => Strings.StepTip_Delay,
        StepType.Http => Strings.StepTip_Http,
        StepType.Grpc => Strings.StepTip_Grpc,
        StepType.MqttPublish => Strings.StepTip_MqttPublish,
        StepType.MqttWait => Strings.StepTip_MqttWait,
        StepType.Script => Strings.StepTip_Script,
        StepType.CallWorkflow => Strings.StepTip_CallWorkflow,
        StepType.UserInput => Strings.StepTip_UserInput,
        StepType.Log => Strings.StepTip_Log,
        StepType.Fail => Strings.StepTip_Fail,
        StepType.ForEach => Strings.StepTip_ForEach,
        StepType.While => Strings.StepTip_While,
        StepType.Switch => Strings.StepTip_Switch,
        _ => "",
    };

    public static string Text(this Group group) => group switch
    {
        Group.Flow => Strings.StepGroup_Flow,
        Group.Data => Strings.StepGroup_Data,
        Group.Messages => Strings.StepGroup_Messages,
        _ => Strings.StepGroup_Code,
    };

    /// <summary>What an output is called on the canvas.</summary>
    public static string OutputText(string output) => output switch
    {
        StepOutputs.Next => Strings.Output_Next,
        StepOutputs.True => Strings.Output_True,
        StepOutputs.False => Strings.Output_False,
        StepOutputs.Error => Strings.Output_Error,
        StepOutputs.Timeout => Strings.Output_Timeout,
        StepOutputs.Loop => Strings.Output_Loop,
        StepOutputs.Done => Strings.Output_Done,
        StepOutputs.Default => Strings.Output_Default,
        _ when output.StartsWith(StepOutputs.CasePrefix, System.StringComparison.Ordinal) => string.Format(Strings.Output_Case, output[StepOutputs.CasePrefix.Length..]),
        _ => output,
    };

    public static string Text(this CompareOperator op) => op switch
    {
        CompareOperator.Equals => Strings.Operator_Equals,
        CompareOperator.NotEquals => Strings.Operator_NotEquals,
        CompareOperator.GreaterThan => Strings.Operator_GreaterThan,
        CompareOperator.GreaterOrEqual => Strings.Operator_GreaterOrEqual,
        CompareOperator.LessThan => Strings.Operator_LessThan,
        CompareOperator.LessOrEqual => Strings.Operator_LessOrEqual,
        CompareOperator.Contains => Strings.Operator_Contains,
        CompareOperator.StartsWith => Strings.Operator_StartsWith,
        CompareOperator.IsEmpty => Strings.Operator_IsEmpty,
        CompareOperator.IsNotEmpty => Strings.Operator_IsNotEmpty,
        _ => Strings.Operator_CSharp,
    };

    public static string Text(this BodyMode mode) => mode switch
    {
        BodyMode.Keep => Strings.BodyMode_Keep,
        BodyMode.Replace => Strings.BodyMode_Replace,
        _ => Strings.BodyMode_Merge,
    };

    public static string Text(this ScriptLanguage language) => language switch
    {
        ScriptLanguage.Go => Strings.Language_Go,
        ScriptLanguage.JavaScript => Strings.Language_JavaScript,
        _ => Strings.Language_CSharp,
    };

    public static string Text(this WorkflowKind kind) => kind == WorkflowKind.Long ? Strings.Kind_Long : Strings.Kind_Short;

    public static string Text(this RunStatus status) => status switch
    {
        RunStatus.Queued => Strings.RunStatus_Queued,
        RunStatus.Running => Strings.RunStatus_Running,
        RunStatus.Waiting => Strings.RunStatus_Waiting,
        RunStatus.WaitingForInput => Strings.RunStatus_WaitingForInput,
        RunStatus.Paused => Strings.RunStatus_Paused,
        RunStatus.Completed => Strings.RunStatus_Completed,
        RunStatus.Failed => Strings.RunStatus_Failed,
        _ => Strings.RunStatus_Killed,
    };

    public static string Text(this RunTrigger trigger) => trigger switch
    {
        RunTrigger.Manual => Strings.RunTrigger_Manual,
        RunTrigger.Rest => Strings.RunTrigger_Rest,
        RunTrigger.Mqtt => Strings.RunTrigger_Mqtt,
        _ => Strings.RunTrigger_Workflow,
    };

    public static readonly FuncValueConverter<CompareOperator, string> OperatorConverter = new(op => op.Text());
    public static readonly FuncValueConverter<BodyMode, string> BodyModeConverter = new(mode => mode.Text());
    public static readonly FuncValueConverter<ScriptLanguage, string> LanguageConverter = new(language => language.Text());
    public static readonly FuncValueConverter<WorkflowKind, string> KindConverter = new(kind => kind.Text());
    public static readonly FuncValueConverter<RunStatus, string> StatusConverter = new(status => status.Text());

    /// <summary>The types on the palette, in groups.</summary>
    public static IEnumerable<IGrouping<Group, StepType>> Palette => PaletteOrder.GroupBy(GroupOf);

    // Related steps side by side: the branches, then the loops, then the rest.
    private static readonly StepType[] PaletteOrder =
    [
        StepType.End, StepType.Condition, StepType.Switch, StepType.ForEach, StepType.While, StepType.Delay, StepType.CallWorkflow,
        StepType.UserInput, StepType.Fail, StepType.SetVariable, StepType.Log, StepType.Http, StepType.Grpc, StepType.MqttPublish,
        StepType.MqttWait, StepType.Script,
    ];
}
