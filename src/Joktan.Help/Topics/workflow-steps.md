# Workflow steps

Every step has a name and a description, which shows when you point at it. Steps that give a result keep it in
`{{last}}`, and in the variable named under **Result**. A step that can fail has an `error` output: link it to
handle the failure, or leave it and the run fails there. **When it fails** tries such a step again, as many times as
you choose, waiting between tries.

## Flow

- **Start**: where every run begins. Its result is the run's input. Choosing it shows what starts the workflow:
  by hand, the REST API or an MQTT message, and the sample input.
- **End**: finishes the run. Its **Output**, JSON that may use paths, is what a REST call or a calling workflow gets
  back; left empty it gives the last result. A run that reaches a step with nothing after it also ends, with the last
  result.
- **Condition**: a diamond, which goes out by `true` from its right point or `false` from its bottom point. Compare two values, such as `{{vars.reply.code}}` **equals** `200`.
  Numbers compare as numbers, even when one is text. **contains** works on text, lists and object keys. With
  **C# is true**, write C# that gives true or false, such as `(int)Vars["count"]! > 3`.
- **Switch**: compares a value, such as `{{input.status}}`, with its cases in order, and goes out by the first equal
  to it, each case an output of its own, or by `default`. A case may be text, JSON or a path. Renaming a case keeps its
  link.
- **Parallel**: runs its **branches** at the same time. Name the branches as a switch's cases; each is an output,
  linked to the branch's first step. Every branch runs as a run of its own, with a copy of the run's data, until it
  reaches a **Join**. Lead every branch to the same join: when all have reached it, the run goes on from there, with
  `{{last}}` holding each branch's result by its name, such as `{{last.stock}}`, and the variables the branches
  changed. A branch that fails stops the others, and the parallel step goes out by `error`. The branches show on
  [Workflows / Runs](help:workflow-runs) as runs of their own, "Branch *name* of run #*n*"; pausing the run does not
  pause them, killing it kills them, and a long run waits on the same branches after Joktan starts again.
- **Join**: where a parallel step's branches meet. It has nothing to set; link it to what comes after the branches.
- **For each**: goes out by `loop` once for each item of a list, each entry of an object as `{key, value}`, or a
  number of times counting from 0, with the item in a variable, `item` unless you name another, and the count in
  `index`. Link `loop` to the first step to repeat, and the last of them back to the for each; when there are no more,
  it goes out by `done`.
- **While**: goes out by `loop` for as long as its comparison holds, counting in `index`, then by `done`. Link the
  end of the loop back to it, as for a for each. A loop with no way out is stopped at the most steps a run may take.
- **Delay**: waits, such as `30s`, `5m`, `2h` or `1d`, or a path. A pause stops the clock.
- **User input**: asks a question with fields to fill in, and waits for the answer on the
  [website](help:website), on [Workflows / Runs](help:workflow-runs), or from the REST API's `workflow_answer`.
  The answer is an object of the fields. **Page on the website** chooses a page built on
  [Workflows / Pages](help:workflow-pages) to show the question with; its fields are asked for as well as the
  step's. Without one, the website shows the question and a box for each field. **Create a page** makes one for
  you from the step, with the question as its title and a box for each field (a box to tick and a comment when there
  are no fields), and chooses it; change it on Workflows / Pages. With a **Time limit** it goes out by `timeout` when nobody answers. **Metadata** goes with the question for
  whoever answers it: names with constants, or global variables such as `{{vars.customer}}`, chosen from a list. The
  run's id (`runId`), serial number (`serial`) and who is asked (`assignedTo`, with its `kind`, `id` and `name`) are
  always included, and those names cannot be used for your own.

  **Assigned to** says who answers: **A role** from [Users](help:users), so that anyone with the role can answer, or
  **A specific user**. Choosing one user shows a warning, because it is bad practice: when that person is away or
  leaves, nobody else can answer and the run waits. The step shows a problem until it is assigned.

  Switch on **Escalate when it is not answered in time** to send the question to someone else after a while: set
  **Escalate after** (such as `30m` or `4h`, or a path) and **Escalate to**, a role or, with the same warning, a user.
  When the time comes the question is for them instead, an alert says so, and it carries on waiting; the time limit
  still applies, so escalate before it runs out. A long run keeps its escalation time over a restart.
- **Call workflow**: starts another workflow with an input. With **Wait for its output** on, its output is the
  result, and its failure is this step's; killing this run kills it too. Off, this run carries on at once with
  `{"runId": ...}`, and the two run side by side. Workflows cannot call each other more than 10 deep.
- **Fail**: stops the run as failed, with a reason.

## Data

- **Set variable**: sets `vars.name` to a value: JSON that may use paths, or text. With **Merge**, a JSON object
  is merged into what the variable holds.
- **Log**: writes a line, which may use paths, in the run's log.

## Database

Five steps work on a table of a PostgreSQL database: one on [Data / Database](help:data), or a connection added on
Settings / Workflow Settings or with **New connection…** while setting the step up. Each is set up in a window of its
own: select the step and press **Configure…**. Choose the database, then its schema and table; the columns are read
from the database itself. Changes are saved as you make them.

- **Read rows**: reads the ticked columns, or all of them when none are ticked, of the rows that pass every filter, in
  the order of a column if you choose one, up to a limit (0 for all).
- **Insert rows**: inserts a row with a value for each ticked column; the others get their defaults. Switch on **A row
  for each item of a list** to insert one row for each item of a list such as `{{vars.orders}}`; while each row's
  values are worked out, its item is in `{{vars.item}}` (or the variable you name), as in `{{vars.item.name}}`.
- **Update rows**: sets the ticked columns of the rows that pass the filters.
- **Delete rows**: deletes the rows that pass the filters.
- **Upsert rows**: updates the row whose **Key** columns match, or inserts it when there is none, for one row or one
  for each item of a list. The primary key is the key to start with; key columns must be the primary key or have a
  unique index.

A value is text, JSON or a path; an empty one is NULL, or for an insert the column's default. A filter compares a
column with a value: equals, greater than, contains and so on, or is empty and is not empty.

**Update rows** and **Delete rows** never change every row. They need at least one filter, every filter needs a value,
and at least one must compare with a value. If a value comes out empty when the workflow runs, such as
`{{input.id}}` when the input has no `id`, the step fails by `error` and nothing is changed. The step is marked in the
designer until its filters are right.

The result, kept in **Keep the result in the variable**, is the list of rows read, or of rows as they are after the
change (with the ids the database numbered them with), or the rows deleted. Switch on **Only the first row** for a
single row, or nothing when there is none. Any column's value can also go in a variable of its own, a global variable
or a new one: the first row's as a single value, or with **All rows** a list of every row's, such as the new `id`
after an insert. Each step runs as one transaction: everything is done, or nothing is.

## Messages and requests

- **REST request**: sends a request saved in the [API Client](help:api-client), with the environment in use, or one
  written on the step: method, address, headers and body. A saved request's body can be sent as it is, replaced, or
  have a JSON object merged into it, so one request serves many runs. The result is
  `{"status", "code", "ok", "body", "headers", "elapsedMs"}`, with the body as JSON when it is JSON. An answer that
  is not a success goes out by `error`, unless that is switched off.
- **gRPC call**: calls a method saved in the API Client, or one written here, found with server reflection. The body
  is the request message as JSON, and can replace or be merged into a saved one.
- **MQTT publish**: publishes from a connected client on [Dev Tools / Brokers / MQTT](help:mqtt-clients): one of its
  [folder messages](help:mqtt-folder-messages), with its varying fields, settings, and body kept, replaced or merged
  into, and its topic changed if you give one; or a topic and body written here.
- **Wait for MQTT**: waits for a message on a topic, which may have `+` and `#`. Give a test to wait for a message
  that passes it, using `{{message.payload...}}`, such as `{{message.payload.level}}` **is greater than** `80`. The
  result is the message: `{"topic", "payload", "retain", "time"}`. With a **Time limit** it goes out by `timeout`.

## Code

- **Script**: runs C#, JavaScript or Go, in a code editor of its own, with libraries. See
  [Workflow scripts](help:workflow-scripts).
