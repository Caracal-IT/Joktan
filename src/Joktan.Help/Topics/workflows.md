# Workflows

A workflow is a set of steps, drawn as shapes on a canvas and joined by links, that Joktan runs for you: send REST
and gRPC requests and MQTT messages, wait for messages, branch and loop, ask a person, run C#, JavaScript or Go, and
call other workflows. Design them on **Workflows / Designer**, and watch and manage their runs on
[Workflows / Runs](help:workflow-runs). How they run is set on **Settings / Workflow Settings**.

## The designer

The left panel has two icon tabs: the **workflows**, in their folders, and the **steps** to add. The canvas is in the
middle, and the **properties** of what is selected are on the right. As in Visual Studio, each side panel can be:

- **resized** by dragging the gap between it and the canvas;
- **collapsed** into its icon tabs with «/», and opened again by choosing a tab;
- **floated** in a window of its own with ⧉, and put back by closing that window. Steps can still be dragged from a
  floating palette onto the canvas.

Their sizes, and which are collapsed, are kept for next time.

## Making a workflow

1. Choose a folder on the workflows tab, or none, and press **New workflow**. It starts with a **Start** step linked
   to an **End** step.
2. On the steps tab, drag a step onto the canvas, or click it to add it after the step selected, linked from its
   `next` output. Every step is described in [Workflow steps](help:workflow-steps).
3. Drag from an output on the right of a step, such as `next`, `true` or `error`, to another step to link them.
   Each output leads to one step; linking it again replaces the old link.
4. Click a step to change its settings on the right. Click the **Start** step to set its global variables, what
   starts the workflow and its sample inputs; its box lists what starts it. Click the background for the workflow's
   own settings: its name, folder, description and how it runs, with the rest again.

Each kind of step has its own shape, colour and picture, as on a flowchart: a rounded box where a run starts and
ends, a diamond for a condition, a box pointed on its left for a switch, a box with its top corners cut off for a
loop, a parallelogram for a request or message going out or coming in, a hexagon for a script, a box with a sloping
top for a question, a box with bars at its sides for another workflow, a box with a round end for a delay, a page
with a wavy foot for a log line, and an octagon, like a stop sign, for a failure.

Each step shows its summary and, in italics under it, its description, so what a step is for shows at a glance.
Drag a step to move it, and drag the background to move around. The wheel scrolls; with Ctrl or Cmd it zooms, as do
**+** and **−**. **Fit** shows every step. **Delete** or Backspace removes the step or link selected; the start step
stays. Changes are saved as you make them. A step with something wrong, or one nothing leads to, has an amber **!**;
point at it to see why.

## Folders

Each workflow is a `.workflow.json` file in the folder on Settings / Workflow Settings, or in a folder below it, so
workflows can be kept in order, with a project and in version control. On the workflows tab, type a name and press
**New folder** to make one inside the folder chosen; new workflows go there too. A workflow's **Folder**, in its own
settings, moves its file. **Delete the folder** removes one that is empty. Names stay different across all folders,
as the REST API starts workflows by name.

## Short and long workflows

- **Short running** workflows finish while you wait, such as an answer to a REST call. They are stopped, and fail,
  if they run longer than the time limit on Settings / Workflow Settings. Time in the queue or paused does not count.
  They are not carried over if Joktan closes.
- **Long running** workflows may take hours or days, such as waiting for a person to approve something. They have no
  time limit, are written down after every step, and carry on after Joktan starts again: a delay ends when it would
  have, a question keeps its deadline, a loop goes on from where it was, and a workflow being waited on is waited on
  again rather than started twice. The step that was cut off is run again, so a request it sent may be sent twice.

## What starts a workflow

These are set on the **Start** step, or with nothing selected. A run's input is always JSON.

- **By hand:** **Run** on the designer starts it with the sample input chosen beside it, or one typed in, and
  **Start a run** on Workflows / Runs likewise.
- **The REST API:** switch on **Start from the REST API and the agent**. Then `POST /api/workflow_start` with
  `{"workflow": "name", "input": {...}}` starts it; add `"wait": true` to be answered when it has finished, with its
  output. The answer has the run's serial number. The call must be switched on on [Settings / REST API](help:rest-api).
  The agent in the panel can start it the same way.
- **An MQTT message:** switch on **Start when an MQTT message arrives**, and choose a client on
  [Dev Tools / Brokers / MQTT](help:mqtt-clients) and a topic, which may have `+` and `#`. Each message starts a run,
  with its payload, which must be JSON, as the input; `{{run.topic}}` is its topic. A message that is not JSON starts
  no run, and says so under the trigger. The client must be connected. Be careful that the workflow does not publish
  to the topic it listens on, or it starts itself again and again.
- **A schedule:** switch on **Start on a schedule**, and choose when, as for a folder message's
  [schedule](help:mqtt-message-settings): **Once** at a date and time, **Recurring at a fixed interval** (every *N*
  seconds, minutes or hours, with an optional start, end and **Maximum runs**), or **Recurring daily** at set times
  on the days chosen. **Input** is the sample input each run starts with, or an empty `{}`. Below it is when the
  next run is due. A run starts at each time even while the one before is still going; times that pass while
  Joktan is closed are skipped, and the schedule, with its count of runs, starts again from when Joktan starts or the
  schedule is changed.
- **Another workflow**, with a [Call workflow](help:workflow-steps) step.

## Global variables and sample inputs

**Global variables**, on the Start step, are set when a run starts, before its first step, and every step reads them
as `{{vars.name}}`. A value is JSON or text, or taken from the input: choose a path the sample inputs hold, such as
`{{input.order.id}}`, from its list, or type one. A **Required** one that comes out empty refuses the run before it
starts, and the REST API answers with why.

**Sample inputs** are inputs, as JSON, to start a run by hand with. Each has a name to choose it by, beside **Run**
and on Workflows / Runs; **Type it in…** takes any input instead.

## Data and paths

A run holds its data as JSON, which any text setting of a step can use with a path in double braces:

- `{{input}}`: what the run was started with, such as `{{input.order.id}}`.
- `{{vars.name}}`: variables: the global variables, those set by a **Set variable** step, a loop's item and count,
  and those kept from a step's result.
- `{{last}}`: the result of the step before.
- `{{run.serial}}`, `{{run.id}}`, `{{run.workflow}}`, `{{run.started}}` and, for an MQTT trigger, `{{run.topic}}`.

Go further in with dots and `[index]`: `{{vars.reply.body.items[0].name}}`. A path alone keeps its JSON, so
`{"item": {{vars.item}}}` puts in a whole object; put text in quotes, as in `{"name": "{{input.name}}"}`. Any other
`{{name}}` is left for the API Client's environment, so saved requests keep their `{{baseUrl}}`.

## Settings / Workflow Settings

- **Workflows folder**: where the `.workflow.json` files and their folders are.
- **Runs at once**: more wait in the queue. Runs that are only waiting do not count.
- **Time limit of short workflows** and **Most steps one run may take**, which stops a loop with no way out.
- **Carry on long workflows when Joktan starts**: off, they wait paused until resumed.
- **Start workflows on MQTT messages**: off, every MQTT trigger is ignored.
- **Start workflows on their schedules**: off, every schedule is ignored.
- **Finished runs to keep**, and for [scripts](help:workflow-scripts), their time limit, the usings and libraries
  every script has, the NuGet feeds, the working folder and the go command.

The workflows folder can be backed up on [Settings / Folders and Backup](help:folders-and-backup).
