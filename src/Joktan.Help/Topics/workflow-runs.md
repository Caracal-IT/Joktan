# Workflow runs

**Workflows / Runs** shows every run, newest first, and is where you look after them. Each run has a serial number,
such as **#42**: one more than the run before it, of any workflow, never used twice, even after runs are cleared or
Joktan starts again. Steps read it as `{{run.serial}}`, and the REST API takes it wherever it takes a run's id. Choose which runs to see:
all, those under way, those waiting for input, those that failed or were killed, or those finished.

## A run

The run selected is drawn on its workflow as it was when the run started, so later changes do not confuse it:

- green steps and links are those the run took, so you can see which way each condition went;
- a red step is where it failed;
- an amber step is where it is waiting: in a delay, for a message, for another workflow, or for you;
- a number on a step is how many times it ran, as in a loop or with retries;
- steps it never reached are faded.

**Compact** and **Detailed** at the top of the diagram choose how runs are drawn, as in the
[designer](help:workflows): small tiles running from top to bottom, or boxes that show what each step does running
from left to right. Swimlanes show in both. The choice is kept for every run, and never changes the run itself.

Under it, **Steps** lists each step it took, with the output it left by and how long it took. Choose one to see its
result and, if it failed, why. **Data** shows the run's input, variables and output, and **Log** what happened.

## Looking after runs

- **Pause** stops a run before its next step, or at once in a delay, which then stands still. **Resume** carries on.
- **Kill** stops it at once, with any workflow it is waiting on.
- **Run again** starts the workflow again with the same input.
- **Remove** takes away a finished run, and **Clear finished** all of them. The settings keep a number of finished
  runs, dropping the oldest.
- **Start a run** starts any workflow by hand: choose one of its sample inputs, which fills in the input, and change
  it or type another before pressing **Start**.

A run waiting for input shows its question, with the values offered filled in; change them and press **Answer**.
The person it is for can answer it on the [website](help:website) instead, where it is one of their work items; the
log then says who answered.
It also says who the question is for (a role or a user on [Users](help:users)) and, if the step escalates, when and
to whom it is escalated, or whom it was escalated from.

The same can be done from the REST API, and by the agent: `workflow_runs`, `workflow_run`, `workflow_pause`,
`workflow_resume`, `workflow_kill` and `workflow_answer`. See [Workflows](help:workflows) for starting runs.
