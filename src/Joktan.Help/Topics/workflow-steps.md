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
- **For each**: goes out by `loop` once for each item of a list, each entry of an object as `{key, value}`, or a
  number of times counting from 0, with the item in a variable, `item` unless you name another, and the count in
  `index`. Link `loop` to the first step to repeat, and the last of them back to the for each; when there are no more,
  it goes out by `done`.
- **While**: goes out by `loop` for as long as its comparison holds, counting in `index`, then by `done`. Link the
  end of the loop back to it, as for a for each. A loop with no way out is stopped at the most steps a run may take.
- **Delay**: waits, such as `30s`, `5m`, `2h` or `1d`, or a path. A pause stops the clock.
- **User input**: asks a question with fields to fill in, and waits for the answer on
  [Workflows / Runs](help:workflow-runs), or from the REST API's `workflow_answer`. The answer is an object of the
  fields. With a **Time limit** it goes out by `timeout` when nobody answers.
- **Call workflow**: starts another workflow with an input. With **Wait for its output** on, its output is the
  result, and its failure is this step's; killing this run kills it too. Off, this run carries on at once with
  `{"runId": ...}`, and the two run side by side. Workflows cannot call each other more than 10 deep.
- **Fail**: stops the run as failed, with a reason.

## Data

- **Set variable**: sets `vars.name` to a value: JSON that may use paths, or text. With **Merge**, a JSON object
  is merged into what the variable holds.
- **Log**: writes a line, which may use paths, in the run's log.

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
