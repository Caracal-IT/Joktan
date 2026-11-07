# Workflows

How it compares with n8n and K2, and when to use it: [comparison.md](comparison.md).

Steps drawn on a canvas and joined by links, which Joktan runs: requests, messages, waits, questions and code.

| Item      | Value                                                                      |
|-----------|----------------------------------------------------------------------------|
| Menu path | Workflows / Designer, Workflows / Runs, Settings / Workflow Settings       |
| Assembly  | `src/Joktan.Workflows`                                                      |
| Help      | `workflows`, `workflow-steps`, `workflow-scripts`, `workflow-runs`          |
| Status    | Implemented                                                                |

## Purpose

Chain together what the other pages do, such as a REST request, an MQTT message and an approval, and run it on its
own: by hand, from another program through the REST API, or when a message arrives.

## Workflows

A workflow is a `.workflow.json` file in the workflows folder (default `<data>/workflows/definitions`) or a folder
below it (hidden folders such as `.git` are skipped), named after the workflow, holding an id that never changes, a
name, a description, a kind, triggers, global variables, named sample inputs, steps with their place on the canvas,
and links. A file with the old single `SampleInput` gets it as a sample named "Sample". Names are unique across all
folders. Folders are made from the designer, inside the one chosen; a workflow's Folder setting moves its file; only
an empty folder can be deleted. A link joins one output of a step to the step that follows; an output
has at most one link. Each workflow has exactly one start step, which cannot be deleted.

| Kind  | Time limit                          | Written down             | After a restart                                   |
|-------|-------------------------------------|--------------------------|---------------------------------------------------|
| Short | Settings, 60 s; queue and pause time do not count | When it finishes | Failed, marked as cut off                        |
| Long  | None                                | After every step         | Carries on, or waits paused if the setting is off |

A long run carried on runs again the step that was cut off, which is marked failed in its history. Delays, time
limits of questions and MQTT waits keep their deadlines. A call workflow step waits again on the run it had started;
a short one cut off is started again.

## Steps and outputs

| Step           | Outputs                 | Result                                                   |
|----------------|-------------------------|----------------------------------------------------------|
| Start          | next                    | The input                                                |
| End            | none                    | Its output, or the last result; the run's output         |
| Set variable   | next, error             | None; sets or merges into `vars.name`                    |
| Condition      | true, false, error      | None                                                     |
| Delay          | next                    | None                                                     |
| REST request   | next, error             | `status, code, ok, body, headers, elapsedMs`             |
| gRPC call      | next, error             | As REST                                                  |
| MQTT publish   | next, error             | `topic, payload`                                         |
| Wait for MQTT  | next, timeout, error    | `topic, payload, retain, time`                           |
| Script         | next, error             | What the code gives                                      |
| Call workflow  | next, error             | Its output, or `runId, workflow` when not waited on      |
| User input     | next, timeout, error    | An object of the fields answered                         |
| Log            | next, error             | None                                                     |
| Fail           | none                    | Fails the run with its reason                            |
| Switch         | one per case, default, error | None; the value                                     |
| For each       | loop, done, error       | None; the item, in `vars.<item>`, count in `vars.<index>` |
| While          | loop, done, error       | None; the count in `vars.<index>`                        |
| Read rows      | next, error             | The rows read, or the first row                          |
| Insert rows    | next, error             | The rows inserted (`RETURNING *`), or the first          |
| Update rows    | next, error             | The rows as changed, or the first                        |
| Delete rows    | next, error             | The rows deleted, or the first                           |
| Upsert rows    | next, error             | The rows inserted or updated, or the first               |

Loops keep their place per step in the run (`Loops`), saved with a long run. A for each goes through a list, an
object's entries as `{key, value}`, or a whole number of counts from 0; the state is dropped at `done`. A switch's
outputs are `case:<value>`; it takes the first case equal to the value (as a condition's equals); renaming a case in
the designer moves its link, removing it removes its link. A loop whose `loop` output never leads back is a warning.

A step's `Labels` give text to write on the links leaving its outputs, keyed by output (`true`, `false`, `case:<value>`,
`default`, or any other). The condition and switch panels edit theirs, and a selected link edits its output's label.
Renaming a case moves its label, and removing it removes it. On the canvas, a link shows its label halfway along, or
the output's name when it has none and is not `next`. Labels change nothing about how a workflow runs.

A failed step goes out by `error`. With no link there, the run fails. A step with an error output can be tried
again up to 100 times, with a wait between tries that a pause stops. A run with no step after the one it took ends
with the last result.

REST and gRPC steps use a request saved in the API Client, in the environment in use, or one written on the step;
MQTT publish uses a client's folder message, with its varying fields and settings, or one written on the step. A
saved body is sent as it is, replaced, or has a JSON object merged into it (as the MQTT proxy merges). The saved
request or message is never changed. gRPC calls written on the step find the method by server reflection.

User input steps put `Metadata` (name and value pairs, each a constant or a path) on the pending question as a JSON
object, after `runId`, `serial` and `assignedTo` (`kind`, `id`, `name`), which are always first and cannot be overridden
(the check flags them, a blank name with a value, and names used twice). `workflow_run` returns the metadata with the
question; Workflows / Runs shows it.

Each user input step is **assigned to** a role (by name) or a user (by id, with the name kept for when the users
cannot be read) from [Users](../users/spec.md); the choices come from `IWorkflowServices.Assignees`. A step that is not
assigned shows a problem. Assigning to a user, or escalating to one, is allowed but shows a non-blocking warning that it
is bad practice. **Escalate** is optional: after `EscalateAfter` (a duration or a path) a question not yet answered is
reassigned to `EscalateTo`, logged, raised as `WorkflowEngine.Escalated` (the app shows an alert) and keeps waiting
until it is answered or its time limit runs out. The check warns when the escalation would come at or after the time
limit. The escalation time is kept on the run (`EscalateAt`) like `Deadline`, so a long run restarted after it is
escalated already. The pending question carries `AssignedTo`, `EscalateTo`, `EscalateAt`, `Escalated` and
`FirstAssignedTo`; `workflow_run` returns them.

## Database steps

Five step types in the palette's Database group: `DatabaseRead`, `DatabaseInsert`, `DatabaseUpdate`, `DatabaseDelete`,
`DatabaseUpsert`. A step names `Database` (the id of a database on Data / Database or of a connection in
`settings/workflows.json`), `Schema`, `Table`, `Columns` (`Column`, `SetsValue`, `Value`, `Variable`, `AllRows`,
`Match`), `Filters` (`Column`, `Operator`, `Value`; any comparison but C#), and for reads `OrderBy`, `Descending`,
`Limit` (0 for all); inserts and upserts take `ManyRows` with `Items` and `ItemVariable`, as a for each. The step is
set up in a window (Configure…) that reads schemas, tables and columns through `IWorkflowServices.OpenDatabase`.

SQL is built by `Joktan.Data.Postgres.RowQueries`: names quoted, values parameters sent as untyped text so PostgreSQL
reads each as its column's type (JSON values as JSON text). Every statement returns its rows as one JSON array
(`json_agg`), so numbers, booleans and json columns keep their types. Inserts are batched under 30,000 parameters and
1,000 rows; an upsert is `INSERT … ON CONFLICT (keys) DO UPDATE SET` the other columns (a key to itself when there are
none), so key columns need a unique index. All statements of a step run in one transaction.

Updates and deletes never touch every row. `RowQueries.Update`/`Delete` refuse filters that are missing, have a blank
value where one is needed, or that only test for emptiness; the step checks the same after its paths are filled in,
before opening the database, and fails with nothing sent (`Equal` with a missing value would otherwise become `IS
NULL`); the designer shows the problem on the step. An upsert fails when a key comes out empty.

The result is the rows (or the first row, `FirstRowOnly`), and each column with a `Variable` sets `vars.<name>` from
the rows: the first row's value, or with `AllRows` a list. A column kept only for its variable has `SetsValue` false.

Connections (`DatabaseConnections`) are kept in `settings/workflows.json` with their passwords in the credential store
(service "Joktan Workflows", account `database:<id>`); Data / Database passwords come from its own store.

## Global variables, input and serial numbers

A run's input is JSON. Global variables (name, value, required) are set in order when a run starts, before the first
step, into `vars`; a value is JSON, text or paths. A required one that comes out empty refuses the run (WorkflowException,
so REST answers 422 and nothing runs). Names must be letters, digits and underscores, not starting with a digit, and
unique. The designer offers the paths of the sample inputs (objects' fields, lists by their first item) in an editable
list.

Every run gets a serial number, one more than the last given, kept in `runs/serial.txt` and never reused; a lost
counter starts above the highest run kept. It is `{{run.serial}}`, shown as `#n`, and runs can be found by it.

## Data and paths

A run's data is `{input, vars, last, run}`. Text settings fill in `{{path}}`, with dots and `[index]`; a path that
starts with anything else is left for the API Client's environment. A setting that is a path alone keeps its JSON;
other text is read as JSON when it is JSON. Comparisons compare numbers as numbers, even as text.

## Scripts

| Language   | Where it runs                     | Sees                                         | Gives                      |
|------------|-----------------------------------|----------------------------------------------|----------------------------|
| C#         | In Joktan, with Roslyn scripting; usings on the settings too | `Input`, `Vars`, `Last`, `Log`, `Cancel` | Its return value |
| JavaScript | In Joktan, with Jint; no files or network; 256 MB | `input`, `vars`, `last`, `log`  | Its return value; `vars` changes kept |
| Go         | `go build` once per code, then run | stdin `{input, vars, last}`; stderr to the log | stdout as JSON           |

Each has the script time limit (30 s) and stops when the run is killed. C# compiled code is kept per code, references
and usings.

Libraries are listed on the step, after those of the settings for its language: C# NuGet packages (`Id Version`,
`Id@Version` or `Id` for the latest release; dependencies followed, using the nuspec group and lib folder that best suit
this .NET; packages the runtime has skipped; feeds on the settings, in order, each an address or a folder of .nupkg
files, the latest version being the highest any feed has and a version coming from the first feed with it) or .dll
files; JavaScript files or addresses, run in order before the code; Go modules fetched with `go get`, or
`module => folder` put in with a quoted `replace`. A folder as a library brings its .dll files, its .js files in name
order, or its Go modules (itself, or each folder in it with a go.mod).

A working folder, the step's (with paths) or else the settings', must exist: Go runs in it; C# has `WorkingFolder`
and `InFolder(path)`, as the app's own current directory is shared; JavaScript has `workingFolder`, `readText`,
`writeText` and `fileExists`, confined to it, and no file access without one. C# imports System.IO by default. Libraries are kept in `<data>/workflows/libraries` and Go builds by a hash of code and
modules. Fetching and building have five minutes of their own.

The code is edited in AvaloniaEdit with TextMate colours for its language, following the app's theme, in the step's
panel and in a window of its own (one per step) beside its libraries. A list inserts a global variable at the caret
as the language reads it.

## Triggers

- By hand, always: **Run** on the designer with the sample input, **Start a run** on Runs with any input.
- REST: when switched on, the `workflow_start` call of the REST API and agent, which can wait for the output.
- MQTT: a client and topic filter; each matching message starts a run with its payload, which must be JSON, as input,
  and its topic as `{{run.topic}}`; one that is not JSON starts nothing and is reported as the trigger's problem. All MQTT triggers can be
  switched off on Settings / Workflow Settings.
- Schedule: `Triggers.Scheduled`, `Triggers.Schedule` (the folder message schedule, `MessageSchedule`, written as
  in a `.send.json`: once, interval or daily) and `Triggers.ScheduleInput` (a sample's name; empty for `{}`).
  `ScheduleTriggers` checks every second and starts runs with trigger `Schedule` and the due time as detail; times
  already past are skipped, not run late. A schedule starts from Joktan's start, or when switched on or changed; an
  unchanged one keeps its place when the workflow is saved. The designer edits it with the shared `ScheduleEditor`
  (Joktan.Mqtt, without Manual) and shows the next run. All schedules can be switched off on Settings / Workflow
  Settings.
- Call workflow steps, at most 10 deep.

## Running

At most the settings' number of runs are running at once (10); more are queued. Runs only waiting do not count.
Pause takes effect before the next step, or at once in a delay; a run waiting for an answer cannot be paused. Kill
stops a run at once, and the run it waits on. A run fails after the settings' most steps (100,000), and keeps only its
latest 500 step records. Finished runs beyond the number kept (200) are removed, oldest first.

## Designer

The left panel has icon tabs for the workflow tree and the steps palette; the right is the properties. Each side panel
resizes with a splitter, collapses into its tabs (a tab chosen opens it), or floats in a window of its own (its content
moves there; closing the window moves it back, as does leaving the page). Widths and collapsing are kept in the
settings. Run has a list of the sample inputs and "Type it in…", which shows a box for the input.

Workflow list and palette on the left, canvas in the middle, properties on the right. Each palette step has its
picture on a tile in its group's colour. Palette steps are dragged onto the canvas, within the page (a card follows
the pointer; the system's drag and drop is not used, as macOS aborts a drag carrying only in-app data), or clicked to
add after the step selected, linked from its free `next` output. Selecting the start step shows the triggers and
sample input, as does selecting nothing; the start step's box lists the triggers.

Each step shows its summary and, under it in italics, up to two lines of its description (inside a diamond, one).
Steps are drawn as flowchart shapes, with the header in the type's colour cut to the shape and the ports on the
outline: Start and End rounded (terminator), Condition a diamond (in at the left point, true right, false bottom,
error lower right), Delay a D, REST/gRPC/MQTT steps parallelograms, Call workflow a box with side bars, User input a
box with a sloping top, Script a hexagon, Fail an octagon, Log a page with a wavy foot, Set variable a box. On the canvas,
steps are dragged to move them, outputs to steps to link them, and the background to pan; the wheel scrolls and,
with Ctrl or Cmd, zooms. Delete removes the step or link selected. Steps with problems carry a warning, with the
reason as a tip. Changes are saved 0.6 s after the last one.

Swimlanes are optional: a workflow's `Lanes` (each a `Name` and `Height`) are stacked from `LanesTop` and drawn
right across the view, alternate ones tinted, with the name read upwards in a strip at the left of the view that
stays put as it pans. A step is in the lane its middle is in; there is no lane field on a step. The first lane added
goes round every step; later ones go under the last, 200 high. A lane is resized by dragging its bottom edge (the
pointer becomes a resize cursor) or on the Swimlanes card; heights snap to the grid, are never less than 80 or than
the steps in the lane need, and the steps under it move by the same amount so they keep to their lanes. Removing a
lane moves the lanes under it, and their steps, up. Lanes have no effect on running and show on the Runs diagram.

## Runs

The run list, newest first, filtered by All, Under way, Waiting for input, Failed or killed, and Finished. The run
selected is drawn on its workflow as it was when it started: steps it took green, where it failed red, where it waits
amber, with how many times each ran, links taken lit and steps not reached faded. Below: its steps with results and
errors, its data, and its log; and a question waiting for an answer, with the values offered.

## REST API and agent

`workflow_list`, `workflow_start` (risky; needs the workflow's REST trigger; `wait`, `wait_seconds`), `workflow_runs`,
`workflow_run`, `workflow_pause`, `workflow_resume`, `workflow_kill` (risky) and `workflow_answer` (risky).

Building, so an agent can make a whole workflow without the designer: `workflow_step_types` (each step type's outputs and
settings, and the parts of a workflow), `workflow_get` (the definition as JSON, with its folder), `workflow_save` (change;
the whole definition, by `Id` or new, in an optional `folder`; names are matched in any case, missing step ids and names
are filled in, steps all at 0,0 are laid out by `DiagramLayout.Arrange` in columns by distance from the start; a name
used by another workflow is refused; it answers the `WorkflowCheck` problems and names that are not there: MQTT clients,
folder messages, databases, pages, workflows to call), `workflow_delete` (risky), `workflow_assignees` (the roles and users),
`workflow_pages`, `workflow_page_get`, `workflow_page_save` (change; answers `PageBuilderViewModel.ProblemOf`) and
`workflow_page_delete` (risky). The designer and the Pages page follow the libraries' `Changed` events.

The Databases category (`DatabaseTools`, through `IWorkflowServices.Databases`/`OpenDatabase`): `database_list`,
`database_tables`, `database_rows`, `database_row_insert` (change) and `database_table_create` (risky, as a column's
type and default are SQL; makes the schema when needed, with a numbered `id` primary key unless `with_id` is false).
There is no tool that runs SQL typed by the agent.

[../../demos/order-approval.md](../../demos/order-approval.md) is a demo that uses every step type but gRPC, with the
prompt that has the agent build it from scratch, and a prepared copy in `profile/ettiene`.

## Settings

`settings/workflows.json`: database connections (no passwords), folder, runs at once, short time limit, most steps, carry on long runs, MQTT triggers, schedules, webhook triggers, the error workflow,
finished runs kept, script time limit, C# usings and libraries for every script of each language, the NuGet feeds (a
single feed saved before becomes the first), the scripts' working folder, the go command, and the designer's panel
widths and collapsing. Runs are kept in `<data>/workflows/runs`. The workflows folder
is a row on Settings / Folders and Backup; runs and Go builds are not backed up.

## Parallel branches

`StepType.Parallel` has named branches in `Cases`, each an output `case:<name>` (shared with Switch, so the case editor,
renaming and labels work for both; `StepTypes.HasCases`, `OutputText(output, type)` draws a branch by its name).
`ParallelSteps.RunAsync` starts each linked branch with `WorkflowEngine.StartBranch`: a run of the same workflow, as the
parent has it, from the branch's first step, `Trigger = Branch`, `ParentRunId`, `Branch`, a copy of the parent's data
with its own `run.id`/`run.serial` and `run.branch`, and `ForkVars` (the variables at the start). The engine stops a
branch run at the first `Join` it reaches, recording `JoinedAt` and its last result as output. The parent waits on all
(`WaitingAsync`, so it does not hold back the queue); the first branch to fail or be killed kills the others and fails
the step (`error`). Otherwise the result is `{branch: output}`, the variables a branch changed from `ForkVars` are copied
back in branch order, and `StepResult.NextStep` sends the run to the join (or, with no join, the run ends there;
branches at different joins fail the step). `BranchRuns` (branch → run id) is saved so a long run waits on the same
branches after a restart. Killing the parent kills its branches; pausing it does not pause them. `WorkflowCheck` wants
two linked branches meeting at one join (`FirstJoins`), and flags a join no branch reaches.

## Error workflows

`Workflow.ErrorWorkflow`, or `WorkflowSettings.ErrorWorkflow` for workflows naming none, is started by
`WorkflowEngine.Finish` when a run fails (`RunTrigger.Error`, detail the failed run's id) with
`{workflow: {id, name}, run: {id, serial, trigger, started, ended}, error, step: {id, name, error}, input}`. Not for
branch runs, runs `Interrupted` by closing, runs of an error workflow (so failures never chain), or a workflow naming
itself. A missing or refused error workflow is logged on the failed run.

## Webhooks

`WorkflowTriggers.Webhook`, `WebhookPath` (matched trimmed of slashes, in lower case; `WebhookTriggers.PathProblem`
allows letters, digits, `-`, `_`, `.` between slashes, no `.`/`..`), `WebhookNeedsKey` (on by default) and
`WebhookWait`. `WebhookTriggers` (Workflows) finds the workflow and answers: POST's JSON body or GET's query is the
input, `run.request` is `{method, path, query}`; 202 with `{runId, serial, status}` at once, or with `WebhookWait` up to
60 s: 200 with `output`, 500 with `error`, or 202 still under way; 400 for a body that is not JSON, 405 for other
methods, 422 when the run is refused, 503 when `WorkflowSettings.WebhookTriggers` is off. The REST API (`RestApi.Webhooks`,
set by the app) routes `/hooks/<path>` there after its host and origin checks, with `Authorize` unless the workflow needs
no key; 404 for a path no workflow has. The designer shows the address from `RestApiUrl` and warns of a path another
workflow uses (`SharedWith`); a duplicated workflow starts with its webhook off.

