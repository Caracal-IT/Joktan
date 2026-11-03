# Errors

Looks through logs for errors in a window of time, groups them, follows their stack traces into the code, and asks
the agent whether one is a bug. Logs are only read when you press **Search**; nothing watches them in the
background.

## Searching

1. Choose the **Source**: Joktan's own logs in Elasticsearch or in log files, or one you added on
   **Settings / Log Sources**.
2. Choose the **Window**: the last 15 minutes, hour, 24 hours or 7 days, or **Custom** with **From** and **To**, such
   as `2026-11-03 14:30`, in this computer's time.
3. Choose the levels: **Warnings**, **Errors** and **Critical**. Errors and critical are chosen to begin with.
4. Optionally, give an **App**, to see one app's logs only, or a text the logs must contain.
5. Press **Search**.

At most the newest 500 logs are read. When there are more, the page says so; make the window smaller to see the rest.

## Groups

Logs of the same error are grouped: the same app, exception type and first frame of the stack trace, or, when there
is no exception, the same message with numbers and quoted values left out. The group seen last is first. Choose one
to see its message, stack trace and when each log was written.

## Into the code

Set where an app's code is with **Choose folder**, or on **Settings / Log Sources**. **In the code** then lists the
frames of the stack trace with the file each names in that folder, even though the app was built somewhere else.
Choose a frame to see the lines around it, or press **Open** to open the file. Frames in files that are not in the
folder, such as the framework's, are shown fainter.

The code is read as it is now. If it has changed since the error was logged, a line may have moved, and the page
says so when the file is shorter than the line.

## Asking the agent

**Ask the agent** opens the [agent](help:agent) panel with a request in its message box: the error, its stack trace,
and how to read the code. Add to it if you like, and send it. The agent reads the code with its own read-only tools,
`source_read` and `source_search`, which never look outside the source folder, and says whether it thinks this is a
bug, the likely cause and a fix. It changes nothing.

You can also ask the agent yourself, such as "any errors in the Billing logs since this morning?"; it searches with
`logs_errors`, as this page does.

## Log sources

On **Settings / Log Sources**:

- **Joktan (Elasticsearch)** reads Joktan's logs from the Elasticsearch on Settings / Telemetry, with its sign-in.
  If that key may only write logs, give a key that may read here.
- **Joktan (log files)** reads the files Settings / Telemetry writes.
- **Add Elasticsearch** adds another one: its address, sign-in and indices, such as `logs-*`.
- **Add log files** adds a file or a folder of them, with a filter, `*.log` to begin with, and whether to read the
  folders inside it. Plain text logs whose lines start with a time are read, with the lines after each, such as a
  stack trace; so are JSON lines in ECS or Serilog's format.
- **Source folders**: for each app, as its logs name it, where its code is. Leave the app empty for any other app.
- **Test** checks that a source can be read.

API keys and passwords are kept in the system's credential store.
