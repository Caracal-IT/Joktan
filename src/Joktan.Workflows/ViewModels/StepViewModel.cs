using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Core.ViewModels;
using Joktan.Data.Postgres;
using Joktan.Workflows.Engine;
using Joktan.Workflows.Model;
using Joktan.Workflows.Resources;
using Joktan.Workflows.Steps;

namespace Joktan.Workflows.ViewModels;

/// <summary>The properties panel of the step selected on the canvas. Every change is written to the step at once.</summary>
public sealed partial class StepViewModel : ViewModelBase
{
    private readonly Action _changed;
    private readonly Workflow? _workflow;

    /// <param name="choices">The clients, folder messages, saved requests and workflows the step's lists offer.</param>
    /// <param name="changed">Called after every change, to save and redraw.</param>
    /// <param name="workflow">The workflow the step is in, whose links follow a switch's cases as they are renamed or removed.</param>
    public StepViewModel(WorkflowStep step, StepChoices choices, Action changed, Workflow? workflow = null)
    {
        Step = step;
        Choices = choices;
        _changed = changed;
        _workflow = workflow;
        foreach (var @case in step.Cases)
            Cases.Add(NewCaseViewModel(@case));
        foreach (var header in step.Headers)
            Headers.Add(new PairViewModel(header, Remove, Changed));
        foreach (var field in step.Fields)
            Fields.Add(new FieldViewModel(field, Remove, Changed));
        foreach (var pair in step.Metadata)
            Metadata.Add(new PairViewModel(pair, RemoveMetadata, Changed));
        foreach (var library in step.Libraries)
            Libraries.Add(new LibraryViewModel(library, RemoveLibrary));
    }

    public WorkflowStep Step { get; }

    public StepChoices Choices { get; }

    public string TypeText => Step.Type.Text();

    public string TypeTip => Step.Type.Tip();

    public string? Problem => WorkflowCheck.StepProblem(Step);

    public bool CanDelete => Step.Type != StepType.Start;

    public bool IsStart => Step.Type == StepType.Start;
    public bool IsEnd => Step.Type == StepType.End;
    public bool IsSetVariable => Step.Type == StepType.SetVariable;
    public bool IsCondition => Step.Type == StepType.Condition;
    public bool IsDelay => Step.Type == StepType.Delay;
    public bool IsHttp => Step.Type == StepType.Http;
    public bool IsGrpc => Step.Type == StepType.Grpc;
    public bool IsApi => IsHttp || IsGrpc;
    public bool IsMqttPublish => Step.Type == StepType.MqttPublish;
    public bool IsMqttWait => Step.Type == StepType.MqttWait;
    public bool IsMqtt => IsMqttPublish || IsMqttWait;
    public bool IsScript => Step.Type == StepType.Script;
    public bool IsCallWorkflow => Step.Type == StepType.CallWorkflow;
    public bool IsUserInput => Step.Type == StepType.UserInput;
    public bool IsLog => Step.Type == StepType.Log;
    public bool IsFail => Step.Type == StepType.Fail;

    public bool IsForEach => Step.Type == StepType.ForEach;
    public bool IsWhile => Step.Type == StepType.While;
    public bool IsSwitch => Step.Type == StepType.Switch;

    /// <summary>Whether the step has the list of cases: a switch's cases, or a parallel step's branches.</summary>
    public bool HasCases => StepTypes.HasCases(Step.Type);

    public bool IsParallel => Step.Type == StepType.Parallel;

    public string CasesTitle => IsParallel ? Strings.Step_Branches : Strings.Step_Cases;

    public string CaseExample => IsParallel ? Strings.Step_BranchExample : Strings.Step_CaseExample;

    public string CasesTip => IsParallel ? Strings.Step_ParallelTip : Strings.Step_SwitchTip;
    public bool IsDatabase => StepTypes.IsDatabase(Step.Type);

    /// <summary>Whether a loop's count is kept in a variable: for each and while.</summary>
    public bool IsLoop => IsForEach || IsWhile;

    /// <summary>Whether a comparison is shown: on a condition and a while, and as the test of an MQTT wait.</summary>
    public bool HasComparison => IsCondition || IsMqttWait || IsWhile;

    /// <summary>Whether the step gives a result worth keeping in a variable.</summary>
    public bool HasResult => Step.Type is StepType.Http or StepType.Grpc or StepType.MqttPublish or StepType.MqttWait or StepType.Script
        or StepType.CallWorkflow or StepType.UserInput || IsDatabase;

    public bool HasMessage => IsUserInput || IsLog || IsFail;

    public bool HasTimeLimit => IsMqttWait || IsUserInput;

    public string MessageLabel => IsUserInput ? Strings.Step_Question : IsFail ? Strings.Step_Reason : Strings.Step_LogLine;

    public string Name
    {
        get => Step.Name;
        set => Set(value, v => Step.Name = v);
    }

    public string Description
    {
        get => Step.Description;
        set => Set(value, v => Step.Description = v);
    }

    public string? ResultVariable
    {
        get => Step.ResultVariable;
        set => Set(value?.Trim() ?? "", v => Step.ResultVariable = v);
    }

    /// <summary>
    /// The variables a result can be kept in, to choose rather than type: the workflow's global variables, then
    /// those other steps keep their results and columns in. Any other name can be typed.
    /// </summary>
    public IReadOnlyList<string> ResultVariableChoices
    {
        get
        {
            var others = _workflow?.Steps.Where(s => s != Step)
                .SelectMany(s => s.Columns.Select(c => c.Variable).Append(s.ResultVariable).Append(s.Type == StepType.SetVariable ? s.Variable : ""))
                .Select(v => v.Trim())
                .Where(WorkflowCheck.IsName) ?? [];
            return VariableChoices.Concat(others.Order(StringComparer.Ordinal)).Distinct().ToList();
        }
    }

    // Retries

    /// <summary>Whether the step can fail and so be tried again: those with an error output.</summary>
    public bool CanRetry => Step.Type is not (StepType.ForEach or StepType.While or StepType.Switch or StepType.Parallel) && StepTypes.Outputs(Step.Type).Contains(StepOutputs.Error);

    public decimal Retries
    {
        get => Step.Retries;
        set => Set((int)Math.Clamp(value, 0, 100), v => Step.Retries = v, [nameof(HasRetries)]);
    }

    public bool HasRetries => Step.Retries > 0;

    public string RetryDelay
    {
        get => Step.RetryDelay;
        set => Set(value, v => Step.RetryDelay = v);
    }

    // Set variable and end

    public string Variable
    {
        get => Step.Variable;
        set => Set(value.Trim(), v => Step.Variable = v);
    }

    public string Value
    {
        get => Step.Value;
        set => Set(value, v => Step.Value = v);
    }

    public bool MergeValue
    {
        get => Step.MergeValue;
        set => Set(value, v => Step.MergeValue = v);
    }

    // Comparison

    public IReadOnlyList<CompareOperator> Operators { get; } = Enum.GetValues<CompareOperator>();

    public string Left
    {
        get => Step.Left;
        set => Set(value, v => Step.Left = v);
    }

    public CompareOperator Operator
    {
        get => Step.Operator;
        set => Set(value, v => Step.Operator = v, [nameof(TakesRight), nameof(LeftLabel)]);
    }

    public string Right
    {
        get => Step.Right;
        set => Set(value, v => Step.Right = v);
    }

    public bool TakesRight => Conditions.TakesRight(Step.Operator);

    public string LeftLabel => Step.Operator == CompareOperator.CSharp ? Strings.Step_CSharpCondition
        : IsMqttWait ? Strings.Step_OnlyAMessageWhere
        : IsWhile ? Strings.Step_LoopWhile
        : Strings.Step_Compare;

    // For each, while and switch

    public string Items
    {
        get => Step.Items;
        set => Set(value, v => Step.Items = v);
    }

    public string ItemVariable
    {
        get => Step.ItemVariable;
        set => Set(value.Trim(), v => Step.ItemVariable = v);
    }

    public string IndexVariable
    {
        get => Step.IndexVariable;
        set => Set(value.Trim(), v => Step.IndexVariable = v);
    }

    /// <summary>A switch's cases, each an output of its own.</summary>
    public ObservableCollection<CaseViewModel> Cases { get; } = [];

    [ObservableProperty]
    public partial string NewCase { get; set; } = "";

    [RelayCommand]
    private void AddCase()
    {
        var value = NewCase.Trim();
        if (value.Length == 0 || Step.Cases.Contains(value))
            return;
        Step.Cases.Add(value);
        Cases.Add(NewCaseViewModel(value));
        NewCase = "";
        Changed();
    }

    // A renamed case keeps its link, which follows it to its new output.
    private bool RenameCase(CaseViewModel @case, string value)
    {
        value = value.Trim();
        var index = Step.Cases.IndexOf(@case.Value);
        if (index < 0 || value.Length == 0 || Step.Cases.Contains(value))
            return false;
        Step.Cases[index] = value;
        if (Step.Labels.Remove(StepOutputs.Case(@case.Value), out var label))
            Step.Labels[StepOutputs.Case(value)] = label;
        foreach (var link in _workflow?.Links.Where(l => l.From == Step.Id && l.Output == StepOutputs.Case(@case.Value)) ?? [])
            link.Output = StepOutputs.Case(value);
        Changed();
        return true;
    }

    private void RemoveCase(CaseViewModel @case)
    {
        Step.Cases.Remove(@case.Value);
        Step.Labels.Remove(StepOutputs.Case(@case.Value));
        Cases.Remove(@case);
        _workflow?.Links.RemoveAll(l => l.From == Step.Id && l.Output == StepOutputs.Case(@case.Value));
        Changed();
    }

    private CaseViewModel NewCaseViewModel(string value)
        => new(value, RenameCase, RemoveCase, StepTypes.Label(Step, StepOutputs.Case(value)), (c, label) => SetLabel(StepOutputs.Case(c.Value), label));

    // Labels written on the links leaving a condition or a switch

    public string TrueLabel
    {
        get => StepTypes.Label(Step, StepOutputs.True);
        set => SetLabel(StepOutputs.True, value);
    }

    public string FalseLabel
    {
        get => StepTypes.Label(Step, StepOutputs.False);
        set => SetLabel(StepOutputs.False, value);
    }

    public string DefaultLabel
    {
        get => StepTypes.Label(Step, StepOutputs.Default);
        set => SetLabel(StepOutputs.Default, value);
    }

    private void SetLabel(string output, string label)
    {
        StepTypes.SetLabel(Step, output, label);
        Changed();
    }

    // Delay and time limits

    public string Duration
    {
        get => Step.Duration;
        set => Set(value, v => Step.Duration = v);
    }

    // REST and gRPC

    public IReadOnlyList<string> Methods { get; } = Api.RestSender.Methods;

    public IReadOnlyList<BodyMode> BodyModes { get; } = Enum.GetValues<BodyMode>();

    /// <summary>The saved requests offered, after "write it here".</summary>
    public IReadOnlyList<string> RequestChoices => [Strings.Step_WriteItHere, .. Choices.Requests()];

    public string RequestChoice
    {
        get => Step.RequestPath.Length == 0 ? Strings.Step_WriteItHere : Step.RequestPath;
        set => Set(value == Strings.Step_WriteItHere || value is null ? "" : value, v => Step.RequestPath = v, [nameof(UsesSavedRequest), nameof(WritesRequest), nameof(ShowsBody)]);
    }

    public bool UsesSavedRequest => Step.RequestPath.Length > 0;

    public bool WritesRequest => !UsesSavedRequest;

    public string Method
    {
        get => Step.Method;
        set => Set(value, v => Step.Method = v);
    }

    public string Url
    {
        get => Step.Url;
        set => Set(value, v => Step.Url = v);
    }

    public string Address
    {
        get => Step.Address;
        set => Set(value, v => Step.Address = v);
    }

    public string Service
    {
        get => Step.Service;
        set => Set(value, v => Step.Service = v);
    }

    public string Rpc
    {
        get => Step.Rpc;
        set => Set(value, v => Step.Rpc = v);
    }

    public BodyMode BodyMode
    {
        get => Step.BodyMode;
        set => Set(value, v => Step.BodyMode = v, [nameof(ShowsBody)]);
    }

    public string Body
    {
        get => Step.Body;
        set => Set(value, v => Step.Body = v);
    }

    /// <summary>Whether the step's own body is used: for what is written here, or when it replaces or merges into what was saved.</summary>
    public bool ShowsBody => (IsApi && WritesRequest) || (IsMqttPublish && WritesMessage) || Step.BodyMode != BodyMode.Keep;

    public bool FailOnErrorStatus
    {
        get => Step.FailOnErrorStatus;
        set => Set(value, v => Step.FailOnErrorStatus = v);
    }

    public ObservableCollection<PairViewModel> Headers { get; } = [];

    [RelayCommand]
    private void AddHeader()
    {
        var pair = new StepPair();
        Step.Headers.Add(pair);
        Headers.Add(new PairViewModel(pair, Remove, Changed));
        Changed();
    }

    // MQTT

    public IReadOnlyList<string> ClientChoices => Choices.Clients();

    public string Client
    {
        get => Step.Client;
        set => Set(value ?? "", v => Step.Client = v, [nameof(MessageChoices)]);
    }

    public IReadOnlyList<string> MessageChoices => [Strings.Step_WriteItHere, .. Choices.FolderMessages(Step.Client)];

    public string MessageChoice
    {
        get => Step.MessageFile.Length == 0 ? Strings.Step_WriteItHere : Step.MessageFile;
        set => Set(value == Strings.Step_WriteItHere || value is null ? "" : value, v => Step.MessageFile = v, [nameof(UsesFolderMessage), nameof(WritesMessage), nameof(ShowsBody), nameof(TopicLabel)]);
    }

    public bool UsesFolderMessage => Step.MessageFile.Length > 0;

    public bool WritesMessage => !UsesFolderMessage;

    public string TopicLabel => IsMqttWait ? Strings.Step_TopicToWaitOn : UsesFolderMessage ? Strings.Step_TopicEmptyKeepsTheMessages : Strings.Step_Topic;

    public string Topic
    {
        get => Step.Topic;
        set => Set(value, v => Step.Topic = v);
    }

    public IReadOnlyList<int> QosChoices { get; } = [0, 1, 2];

    public int Qos
    {
        get => Step.Qos;
        set => Set(value, v => Step.Qos = v);
    }

    public bool Retain
    {
        get => Step.Retain;
        set => Set(value, v => Step.Retain = v);
    }

    // Script

    public IReadOnlyList<ScriptLanguage> Languages { get; } = Enum.GetValues<ScriptLanguage>();

    public ScriptLanguage Language
    {
        get => Step.Language;
        set
        {
            // Code that is still a template gives way to the template of the new language.
            var template = Step.Code.Trim().Length == 0 || Step.Code == ScriptRunner.CSharpTemplate || Step.Code == ScriptRunner.GoTemplate
                           || Step.Code == ScriptRunner.JavaScriptTemplate;
            Set(value, v => Step.Language = v, [nameof(Code), nameof(CodeTip), nameof(CanCheckCode), nameof(LibraryTip), nameof(LibraryExample), nameof(GlobalImportsText)]);
            CodeCheck = null;
            if (template)
                Code = Template(value);
        }
    }

    public static string Template(ScriptLanguage language) => language switch
    {
        ScriptLanguage.Go => ScriptRunner.GoTemplate,
        ScriptLanguage.JavaScript => ScriptRunner.JavaScriptTemplate,
        _ => ScriptRunner.CSharpTemplate,
    };

    public string Code
    {
        get => Step.Code;
        set => Set(value, v => Step.Code = v);
    }

    public string CodeTip => Step.Language switch
    {
        ScriptLanguage.Go => Strings.Step_GoTip,
        ScriptLanguage.JavaScript => Strings.Step_JavaScriptTip,
        _ => Strings.Step_CSharpTip,
    };

    /// <summary>Whether the code can be checked here; Go is checked when it is built.</summary>
    public bool CanCheckCode => Step.Language != ScriptLanguage.Go;

    [ObservableProperty]
    public partial string? CodeCheck { get; set; }

    /// <summary>Reads the C# or JavaScript, with its libraries, and says what is wrong with it, without running it.</summary>
    [RelayCommand]
    private async Task CheckCode()
    {
        IReadOnlyList<string> problems;
        if (Choices.Scripts is { } scripts)
        {
            CodeCheck = Strings.Step_Checking;
            problems = await scripts.CheckAsync(Step, CancellationToken.None);
        }
        else
        {
            problems = Step.Language switch
            {
                ScriptLanguage.CSharp => ScriptRunner.CompileProblems(Step.Code),
                ScriptLanguage.JavaScript => ScriptRunner.JavaScriptProblem(Step.Code) is { } problem ? [problem] : [],
                _ => [],
            };
        }

        CodeCheck = problems.Count == 0 ? Strings.Step_TheCodeCompiles : string.Join("\n", problems.Take(5));
    }

    /// <summary>The global variables the code can use, to insert at the caret.</summary>
    public IReadOnlyList<string> VariableChoices => Choices.Globals?.Invoke() ?? [];

    public bool HasVariableChoices => VariableChoices.Count > 0;

    /// <summary>How the code reads a variable, in its language.</summary>
    public string Reference(string variable) => Step.Language switch
    {
        ScriptLanguage.JavaScript => $"vars.{variable}",
        ScriptLanguage.Go => $"data.Vars[\"{variable}\"]",
        _ => $"Vars[\"{variable}\"]",
    };

    /// <summary>Opens the code in an editor window of its own. Set by the view.</summary>
    public Action<StepViewModel>? OpenEditor { get; set; }

    [RelayCommand]
    private void OpenCodeEditor() => OpenEditor?.Invoke(this);

    // Libraries

    public ObservableCollection<LibraryViewModel> Libraries { get; } = [];

    public bool HasLibraries => Libraries.Count > 0;

    /// <summary>A library being typed, to add to the list.</summary>
    [ObservableProperty]
    public partial string NewLibrary { get; set; } = "";

    /// <summary>What every script of the language has from Settings / Workflow Settings, or null when nothing.</summary>
    public string? GlobalImportsText
    {
        get
        {
            if (Choices.GlobalImports?.Invoke(Step.Language) is not { Count: > 0 } global)
                return null;
            return string.Format(Strings.Step_GlobalImports, string.Join(", ", global));
        }
    }

    public string LibraryTip => Step.Language switch
    {
        ScriptLanguage.Go => Strings.Step_GoLibrariesTip,
        ScriptLanguage.JavaScript => Strings.Step_JavaScriptLibrariesTip,
        _ => Strings.Step_CSharpLibrariesTip,
    };

    public string LibraryExample => Step.Language switch
    {
        ScriptLanguage.Go => "github.com/google/uuid@v1.6.0",
        ScriptLanguage.JavaScript => "https://cdn.jsdelivr.net/npm/lodash@4.17.21/lodash.min.js",
        _ => "Newtonsoft.Json 13.0.3",
    };

    /// <summary>The folder the code works in; empty for the one on Settings / Workflow Settings.</summary>
    public string WorkingFolder
    {
        get => Step.WorkingFolder;
        set => Set(value, v => Step.WorkingFolder = v);
    }

    /// <summary>What an empty working folder means: the settings' folder, or none.</summary>
    public string WorkingFolderPlaceholder => Choices.DefaultWorkingFolder?.Invoke() is { Length: > 0 } folder
        ? string.Format(Strings.Step_WorkingFolderFromSettings, folder)
        : Strings.Step_NoWorkingFolder;

    /// <summary>Picks a folder, or null when none was chosen. Set by the view.</summary>
    public Func<Task<string?>>? PickFolder { get; set; }

    [RelayCommand]
    private async Task BrowseWorkingFolder()
    {
        if (PickFolder is not null && await PickFolder() is { } folder)
        {
            WorkingFolder = folder;
            OnPropertyChanged(nameof(WorkingFolder));
        }
    }

    /// <summary>Adds a folder as a library: its assemblies, scripts or Go modules, as the language takes them.</summary>
    [RelayCommand]
    private async Task AddLibraryFolder()
    {
        if (PickFolder is not null && await PickFolder() is { } folder)
        {
            NewLibrary = folder;
            AddLibrary();
        }
    }

    [RelayCommand]
    private void AddLibrary()
    {
        var library = NewLibrary.Trim();
        if (library.Length == 0 || Step.Libraries.Contains(library))
            return;
        Step.Libraries.Add(library);
        Libraries.Add(new LibraryViewModel(library, RemoveLibrary));
        NewLibrary = "";
        OnPropertyChanged(nameof(HasLibraries));
        Changed();
    }

    private void RemoveLibrary(LibraryViewModel library)
    {
        Step.Libraries.Remove(library.Text);
        Libraries.Remove(library);
        OnPropertyChanged(nameof(HasLibraries));
        Changed();
    }

    // Call workflow

    public IReadOnlyList<WorkflowChoice> WorkflowChoices => Choices.Workflows();

    public WorkflowChoice? WorkflowChoice
    {
        get => WorkflowChoices.FirstOrDefault(w => w.Id == Step.Workflow);
        set => Set(value?.Id ?? "", v => Step.Workflow = v);
    }

    public string Input
    {
        get => Step.Input;
        set => Set(value, v => Step.Input = v);
    }

    public bool Wait
    {
        get => Step.Wait;
        set => Set(value, v => Step.Wait = v);
    }

    // Database

    /// <summary>The database chosen, by name, or that none is.</summary>
    public string DatabaseText => Step.Database.Length == 0 ? Strings.DatabaseStep_NoneChosen
        : Choices.Databases().FirstOrDefault(d => d.Id == Step.Database)?.Name ?? Strings.DatabaseStep_Gone;

    public string TableText => Step.Table.Length == 0 ? Strings.DatabaseStep_NoneChosen : $"{Step.Schema}.{Step.Table}";

    /// <summary>Whether the step uses columns: all database steps but a delete.</summary>
    public bool UsesDatabaseColumns => IsDatabase && Step.Type != StepType.DatabaseDelete;

    /// <summary>Whether the step has filters: a read, an update and a delete.</summary>
    public bool HasDatabaseFilters => Step.Type is StepType.DatabaseRead or StepType.DatabaseUpdate or StepType.DatabaseDelete;

    /// <summary>The columns the step reads or gives values to, or that it reads them all.</summary>
    public string DatabaseColumnsText => Step.Columns.Count == 0
        ? Step.Type == StepType.DatabaseRead ? Strings.DatabaseStep_AllColumns : Strings.DatabaseStep_NoColumns
        : string.Join(", ", Step.Columns.Select(c => c.Match ? c.Column + " 🔑" : c.Column));

    /// <summary>The filters, or that there are none.</summary>
    public string DatabaseFiltersText => Step.Filters.Count(f => f.Column.Trim().Length > 0) switch
    {
        0 => Step.Type == StepType.DatabaseRead ? Strings.DatabaseStep_AllRowsRead : Strings.DatabaseStep_NoFilters,
        1 => Strings.DatabaseStep_OneFilter,
        var count => string.Format(Strings.DatabaseStep_FilterCount, count),
    };

    /// <summary>Opens the window the database step is set up in. Set by the view.</summary>
    public Action<StepViewModel>? OpenDatabaseEditor { get; set; }

    [RelayCommand]
    private void ConfigureDatabase() => OpenDatabaseEditor?.Invoke(this);

    /// <summary>Called by the database step's window after every change it makes to the step.</summary>
    internal void DatabaseEdited()
    {
        OnPropertyChanged(nameof(DatabaseText));
        OnPropertyChanged(nameof(TableText));
        OnPropertyChanged(nameof(DatabaseColumnsText));
        OnPropertyChanged(nameof(DatabaseFiltersText));
        Changed();
    }

    // User input, log and fail

    public string Message
    {
        get => Step.Message;
        set => Set(value, v => Step.Message = v);
    }

    public ObservableCollection<FieldViewModel> Fields { get; } = [];

    /// <summary>No page, then the pages of Workflows / Pages, and one chosen before that has since gone.</summary>
    public IReadOnlyList<PageChoice> PageChoices
    {
        get
        {
            var pages = Choices.Pages();
            var id = Step.PageId.Trim();
            return
            [
                new PageChoice("", Strings.Designer_NoPage),
                .. pages,
                .. id.Length > 0 && pages.All(p => p.Id != id) ? [new PageChoice(id, string.Format(Strings.Designer_MissingPage, id))] : Array.Empty<PageChoice>(),
            ];
        }
    }

    public PageChoice? PageChoice
    {
        get => PageChoices.FirstOrDefault(c => c.Id == Step.PageId.Trim());
        set
        {
            // A list made afresh sets nothing for a moment; that is not a choice.
            if (value is null || value.Id == Step.PageId.Trim())
                return;
            Set(value.Id, v => Step.PageId = v, [nameof(HasPage), nameof(CanCreatePage)]);
        }
    }

    public bool HasPage => Step.PageId.Trim().Length > 0;

    public bool CanCreatePage => !HasPage && Choices.CreatePage is not null;

    /// <summary>Makes a page on Workflows / Pages from the question and its fields, and shows it.</summary>
    [RelayCommand]
    private void CreatePage()
    {
        var name = _workflow is null ? Step.Name : $"{_workflow.Name} - {Step.Name}";
        if (Choices.CreatePage?.Invoke(Step, name) is not { } page)
            return;
        Set(page.Id, v => Step.PageId = v, [nameof(HasPage), nameof(CanCreatePage)]);
        PagesChanged();
    }

    /// <summary>Reads the pages again, as after one was added, renamed or deleted on Workflows / Pages.</summary>
    public void PagesChanged()
    {
        OnPropertyChanged(nameof(PageChoices));
        OnPropertyChanged(nameof(PageChoice));
    }

    [RelayCommand]
    private void AddField()
    {
        var field = new InputField { Name = $"field{Fields.Count + 1}" };
        Step.Fields.Add(field);
        Fields.Add(new FieldViewModel(field, Remove, Changed));
        Changed();
    }

    /// <summary>What goes with a user input step's question, besides the run's id and serial number.</summary>
    public ObservableCollection<PairViewModel> Metadata { get; } = [];

    /// <summary>Paths to the global variables, for a metadata value to be chosen rather than typed.</summary>
    public IReadOnlyList<string> GlobalPaths => VariableChoices.Select(v => $"{{{{vars.{v}}}}}").ToList();

    [RelayCommand]
    private void AddMetadata()
    {
        var pair = new StepPair { Name = $"key{Metadata.Count + 1}" };
        Step.Metadata.Add(pair);
        Metadata.Add(new PairViewModel(pair, RemoveMetadata, Changed));
        Changed();
    }

    // Who the question is for, and whom it is escalated to. A role is the good practice; a user gets a warning.

    /// <summary>"A role" and "A specific user", for <see cref="AssignKindIndex"/> and <see cref="EscalateKindIndex"/>.</summary>
    public IReadOnlyList<string> AssigneeKinds { get; } = [Strings.Step_ARole, Strings.Step_ASpecificUser];

    public int AssignKindIndex
    {
        get => (int)Step.AssignedTo.Kind;
        set => SetKind(Step.AssignedTo, value, [nameof(AssignChoices), nameof(AssignedChoice), nameof(IsAssignedToUser), nameof(AssignPlaceholder), nameof(AssignmentWarning)]);
    }

    public IReadOnlyList<AssigneeChoice> AssignChoices => ChoicesFor(Step.AssignedTo);

    public AssigneeChoice? AssignedChoice
    {
        get => ChosenOf(Step.AssignedTo);
        set => SetChoice(Step.AssignedTo, value, [nameof(AssignmentWarning)]);
    }

    public bool IsAssignedToUser => Step.AssignedTo.Kind == AssigneeKind.User;

    public string AssignPlaceholder => IsAssignedToUser ? Strings.Designer_ChooseAUser : Strings.Designer_ChooseARole;

    public bool Escalate
    {
        get => Step.Escalate;
        set => Set(value, v => Step.Escalate = v, [nameof(AssignmentWarning)]);
    }

    public string EscalateAfter
    {
        get => Step.EscalateAfter;
        set => Set(value, v => Step.EscalateAfter = v, [nameof(AssignmentWarning)]);
    }

    public int EscalateKindIndex
    {
        get => (int)Step.EscalateTo.Kind;
        set => SetKind(Step.EscalateTo, value, [nameof(EscalateChoices), nameof(EscalateChoice), nameof(IsEscalatedToUser), nameof(EscalatePlaceholder), nameof(AssignmentWarning)]);
    }

    public IReadOnlyList<AssigneeChoice> EscalateChoices => ChoicesFor(Step.EscalateTo);

    public AssigneeChoice? EscalateChoice
    {
        get => ChosenOf(Step.EscalateTo);
        set => SetChoice(Step.EscalateTo, value, [nameof(AssignmentWarning)]);
    }

    public bool IsEscalatedToUser => Step.EscalateTo.Kind == AssigneeKind.User;

    public string EscalatePlaceholder => IsEscalatedToUser ? Strings.Designer_ChooseAUser : Strings.Designer_ChooseARole;

    /// <summary>Why the assignment is bad practice, such as a question for one user, or null.</summary>
    public string? AssignmentWarning => WorkflowCheck.AssignmentWarning(Step);

    /// <summary>Whether there are no roles to choose from, because Users has no database or it could not be read.</summary>
    public bool HasNoRoles => Choices.Assignees(AssigneeKind.Role).Count == 0;

    // The roles or users to choose from, with the one saved kept on the list when it is not among them, such as when the users cannot be read.
    private IReadOnlyList<AssigneeChoice> ChoicesFor(Assignee assignee)
    {
        var choices = Choices.Assignees(assignee.Kind);
        return !assignee.IsSet || choices.Any(c => c.Id == assignee.Id)
            ? choices
            : [new AssigneeChoice(assignee.Kind, assignee.Id, assignee.Name.Length > 0 ? assignee.Name : assignee.Id), .. choices];
    }

    private AssigneeChoice? ChosenOf(Assignee assignee) => assignee.IsSet ? ChoicesFor(assignee).FirstOrDefault(c => c.Id == assignee.Id) : null;

    private void SetKind(Assignee assignee, int index, string[] also, [CallerMemberName] string name = "")
    {
        var kind = index == (int)AssigneeKind.User ? AssigneeKind.User : AssigneeKind.Role;
        if (kind == assignee.Kind)
            return;
        Set(kind, k =>
        {
            assignee.Kind = k;
            assignee.Id = "";
            assignee.Name = "";
        }, also, name);
    }

    private void SetChoice(Assignee assignee, AssigneeChoice? choice, string[] also, [CallerMemberName] string name = "")
    {
        // A list made afresh sets nothing for a moment; that is not a choice.
        if (choice is null || choice.Id == assignee.Id)
            return;
        Set(choice, c =>
        {
            assignee.Id = c.Id;
            assignee.Name = c.Name;
        }, also, name);
    }

    /// <summary>Reads the roles and users again, as after the Users page changed them.</summary>
    public void PeopleChanged()
    {
        OnPropertyChanged(nameof(AssignChoices));
        OnPropertyChanged(nameof(AssignedChoice));
        OnPropertyChanged(nameof(EscalateChoices));
        OnPropertyChanged(nameof(EscalateChoice));
        OnPropertyChanged(nameof(HasNoRoles));
    }

    private void RemoveMetadata(PairViewModel pair)
    {
        Step.Metadata.Remove(pair.Pair);
        Metadata.Remove(pair);
        Changed();
    }

    private void Remove(PairViewModel pair)
    {
        Step.Headers.Remove(pair.Pair);
        Headers.Remove(pair);
        Changed();
    }

    private void Remove(FieldViewModel field)
    {
        Step.Fields.Remove(field.Field);
        Fields.Remove(field);
        Changed();
    }

    private void Set<T>(T value, Action<T> apply, string[]? also = null, [CallerMemberName] string name = "")
    {
        apply(value);
        OnPropertyChanged(name);
        foreach (var other in also ?? [])
            OnPropertyChanged(other);
        Changed();
    }

    private void Changed()
    {
        OnPropertyChanged(nameof(Problem));
        _changed();
    }
}

/// <summary>A header of a step's request.</summary>
public sealed class PairViewModel(StepPair pair, Action<PairViewModel> remove, Action changed) : ViewModelBase
{
    public StepPair Pair { get; } = pair;

    public string Name
    {
        get => Pair.Name;
        set
        {
            Pair.Name = value;
            changed();
        }
    }

    public string Value
    {
        get => Pair.Value;
        set
        {
            Pair.Value = value;
            changed();
        }
    }

    public RelayCommand RemoveCommand => field ??= new(() => remove(this));
}

/// <summary>A value a user input step asks for.</summary>
public sealed class FieldViewModel(InputField input, Action<FieldViewModel> remove, Action changed) : ViewModelBase
{
    public InputField Field { get; } = input;

    public string Name
    {
        get => Field.Name;
        set
        {
            Field.Name = value.Trim();
            changed();
        }
    }

    public string Label
    {
        get => Field.Label;
        set
        {
            Field.Label = value;
            changed();
        }
    }

    public string Default
    {
        get => Field.Default;
        set
        {
            Field.Default = value;
            changed();
        }
    }

    public RelayCommand RemoveCommand => field ??= new(() => remove(this));
}

/// <summary>A case of a switch, renamed when what is typed is finished and differs from the other cases.</summary>
public sealed partial class CaseViewModel(string value, Func<CaseViewModel, string, bool> rename, Action<CaseViewModel> remove,
    string label = "", Action<CaseViewModel, string>? labelChanged = null) : ViewModelBase
{
    private string _label = label;

    public string Value { get; private set; } = value;

    /// <summary>The text written on the case's link.</summary>
    public string Label
    {
        get => _label;
        set
        {
            _label = value;
            labelChanged?.Invoke(this, value);
            OnPropertyChanged();
        }
    }

    /// <summary>What is being typed; it becomes the case's value when <see cref="Apply"/> is called.</summary>
    [ObservableProperty]
    public partial string Text { get; set; } = value;

    public void Apply()
    {
        if (Text.Trim() == Value)
            return;
        if (rename(this, Text))
            Value = Text.Trim();
        else
            Text = Value;
    }

    public RelayCommand RemoveCommand => field ??= new(() => remove(this));
}

/// <summary>A library a script uses.</summary>
public sealed class LibraryViewModel(string text, Action<LibraryViewModel> remove) : ViewModelBase
{
    public string Text { get; } = text;

    public RelayCommand RemoveCommand => field ??= new(() => remove(this));
}

/// <summary>A workflow a call workflow step can start.</summary>
public sealed record WorkflowChoice(string Id, string Name)
{
    public override string ToString() => Name;
}

/// <summary>A page of Workflows / Pages a user input step can show, or none, with an empty id.</summary>
public sealed record PageChoice(string Id, string Name)
{
    public override string ToString() => Name;
}

/// <summary>Where the lists of a step's properties come from.</summary>
public sealed class StepChoices
{
    public Func<IReadOnlyList<string>> Clients { get; init; } = () => [];

    public Func<string, IReadOnlyList<string>> FolderMessages { get; init; } = _ => [];

    public Func<IReadOnlyList<string>> Requests { get; init; } = () => [];

    public Func<IReadOnlyList<WorkflowChoice>> Workflows { get; init; } = () => [];

    /// <summary>The pages of Workflows / Pages a user input step can show.</summary>
    public Func<IReadOnlyList<PageChoice>> Pages { get; init; } = () => [];

    /// <summary>Makes and saves a page for a user input step, under about the name given.</summary>
    public Func<WorkflowStep, string, PageChoice>? CreatePage { get; init; }

    /// <summary>Checks code with its libraries; without it, code is checked without them.</summary>
    public ScriptRunner? Scripts { get; init; }

    /// <summary>The working folder on Settings / Workflow Settings, which a step without its own uses.</summary>
    public Func<string>? DefaultWorkingFolder { get; init; }

    /// <summary>The names of the workflow's global variables, for the code editor to insert.</summary>
    public Func<IReadOnlyList<string>>? Globals { get; init; }

    /// <summary>What every script of a language has from the settings: usings for C#, libraries for all.</summary>
    public Func<ScriptLanguage, IReadOnlyList<string>>? GlobalImports { get; init; }

    /// <summary>The databases a database step can use.</summary>
    public Func<IReadOnlyList<DatabaseChoice>> Databases { get; init; } = () => [];

    /// <summary>The roles, or the users, a user input step's question can be assigned to.</summary>
    public Func<AssigneeKind, IReadOnlyList<AssigneeChoice>> Assignees { get; init; } = _ => [];

    /// <summary>Opens one of the databases, to read its schemas, tables and columns.</summary>
    public Func<string, (IDatabaseConnection? Connection, string? Error)>? OpenDatabase { get; init; }

    /// <summary>The connections on Settings / Workflow Settings, which a database step's window can add to.</summary>
    public DatabaseConnections? Connections { get; init; }

    /// <summary>Opens a connection being made, to test it; over the network otherwise.</summary>
    public DatabaseOpener? OpenConnection { get; init; }
}
