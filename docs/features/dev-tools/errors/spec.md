# Errors

A developer tool for looking through logs for errors over a window of time, following their stack traces into
the code, and asking the agent whether one is a bug. The logs can be Joktan's own, another Elasticsearch's, or
log files.

| Item      | Value                                          |
|-----------|------------------------------------------------|
| Menu path | Dev Tools / Errors, and Settings / Log Sources |
| Status    | Implemented                                    |
| Code      | `src/Joktan.Logs/`                             |

## Purpose

Find out what went wrong in a period, without Kibana or a terminal, and get a first opinion on whether it is a bug.
Nothing is watched in the background: logs are read only when **Search** is pressed.

## Errors page

1. Page header: title "Errors", subtitle "Dev Tools / Errors".
2. A card with what to look for: the **Source**, the **Window** (last 15 minutes, last hour, last 24 hours, last
   7 days, or custom), and for a custom window **From** and **To** as `yyyy-MM-dd HH:mm` in this computer's time
   (an offset such as `+02:00` may follow). **App** and **Containing** narrow it to one app's logs, or to those with
   a text in their message or stack trace. **Warnings**, **Errors** and **Critical** choose the levels; errors and
   critical to begin with. **Search** runs it, and **Cancel** stops it while it runs.
3. What was found, such as "12 logs in 3 groups.", or why the source could not be read. At most the newest 500 logs
   are read, and the page says so when there were more.
4. On the left, the groups, the one seen last first: a red dot (amber for warnings), the exception's type or else
   the message, the app and when it was last seen, and how many times.
5. On the right, the chosen group: its type and message, **Ask the agent**, its level, app, module, how many times,
   and when it was first and last seen. Then where its app's code is, with **Choose folder**; **In the code**, the
   frames of the stack trace that name a file, each with **Open** when the file was found, and the lines around the
   chosen frame's, its own marked; the stack trace; and when each log was written, the newest 100.

### Groups

Logs are the same error when they share the app, the exception's type and the first frame's method; the file and
line are left out, as they move when code changes. A log with neither is grouped by its message, with numbers,
GUIDs and quoted values left out. The newest log of a group is the one shown.

### Following the code

The path in a frame is where the app was built. The longest end of it found in the app's source folder is taken,
so `/ci/work/billing/src/Charges.cs` is `src/Charges.cs` there. Nothing outside the folder is read, whatever the
path, links included. The code is as it is now, so a line that has moved since the error was logged says so.
.NET frames (`at … in file:line n`) give the method too; Java, Python and Node frames give the file and line.

### Asking the agent

**Ask the agent** opens the agent panel and puts a request in its message box, which is not sent, as with a prompt:
the source, app, times, level, type, message and stack trace (6,000 characters at most), and, when a source folder
is set, to read the code with `source_read` and `source_search` and say whether it is a bug, the likely cause and a
fix, without changing anything. Without a folder, it asks for a judgement from the stack trace alone.

## Settings / Log Sources

Each change is saved as it is made, once it is valid, in `settings/log-sources.json`. API keys and passwords are in
the credential store under "Joktan Log Sources", one account per source.

| Source | What is set |
|---|---|
| Joktan (Elasticsearch) | Always there. The address and sign-in are those on Settings / Telemetry. The indices are `logs-joktan*`, which takes in both the data stream Joktan writes to and the collector's. An optional API key for reading, for when the one on Settings / Telemetry may only write |
| Joktan (log files) | Always there. `joktan-*.log` in the folder Settings / Telemetry writes to |
| Another Elasticsearch | A name, the address, the sign-in (none, API key, or username and password) and the indices, `logs-*` to begin with |
| Log files | A name, a file or a folder, a filter (`*.log` to begin with; several separated by semicolons) and whether subfolders are read |

- Names must be given and differ, ignoring case, since the agent finds sources by name. Joktan's two cannot be
  renamed or removed.
- **Test** reads the last day's logs at any level and says whether the source could be read.
- **Source folders**: an app, as its logs name it, and the folder of its code. An empty app stands for any app the
  source has no other folder for. Joktan's two sources start with one for `joktan`. **Choose folder** on the Errors
  page adds or changes the one for the chosen group's app.

### Reading Elasticsearch

One `_search` for the newest logs in the window, asking for fields rather than whole documents. The usual ECS
names are asked for, which logs written by an OpenTelemetry collector also answer to through Elasticsearch's
aliases: `log.level`, `message`, `service.name`, `error.type` and `error.stack_trace`, with `app`, `module` and
`attributes.*` besides. Levels and apps are matched whatever their case, and a level's other names count too:
`err`, `fatal`, `crit` and so on. 401 and 403 have messages of their own.

### Reading files

A file last written before the window began is passed over. A line starting with a time (`2026-11-03T12:00:00`,
with a space, a comma for the fraction or an offset, perhaps in brackets) starts a log; the lines after it, such as
a stack trace, belong to it. The level is the first word near the start that names one (`ERROR`, `ERR`, `FATAL`,
`WARN` and so on), and Joktan's `app=` and `module=` follow it. A line that is a JSON object is a log of its own, in
ECS or Serilog's compact format. A time with no offset is in this computer's time zone.

## Agent tools

| Tool | Risk | What it does |
|---|---|---|
| `logs_sources` | Read | The sources and their source folders |
| `logs_errors` | Read | Searches as the page does, setting the page's source, window, levels and filters, and returns the groups (50 at most, stack traces cut at 4,000 characters) |
| `source_search` | Read | Lines holding a text in an app's code, ignoring case; build output, packages and version control folders are left out, as are files over 2 MB and binary ones |
| `source_read` | Read | Up to 400 lines of a file in an app's code, by a path relative to the folder or as a stack trace gives it |

The source tools only read inside the folder set for the app, and a source with one folder only uses it when no app
is named. No tool changes log sources.
