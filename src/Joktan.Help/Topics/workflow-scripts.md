# Workflow scripts

A **Script** step runs code: C# or JavaScript inside Joktan, or Go with the go command. What the code gives back is
the step's result, as JSON. Each language starts with an example. A script that runs longer than the limit on
Settings / Workflow Settings is stopped and fails, and killing the run stops it too.

## The code editor

The code is written in an editor with line numbers and the colours of its language. **Open in a window** opens it
in a large editor window of its own, beside its libraries; both edit the same step, and asking again brings the
window forward. **Insert a global variable** puts one of the workflow's global variables at the caret, as the
language reads it: `Vars["name"]` in C#, `vars.name` in JavaScript, `data.Vars["name"]` in Go. **Check the code**
finds mistakes in C# and JavaScript, with their libraries, without running it.

## Libraries

Under **Libraries**, add what the code uses, one at a time:

- **C#**: NuGet packages, such as `Newtonsoft.Json 13.0.3`, or a name alone for the latest, with the packages they
  need; or `.dll` files. Add `using` lines at the top of the code.
- **JavaScript**: script files, or their addresses, such as
  `https://cdn.jsdelivr.net/npm/lodash@4.17.21/lodash.min.js`. They run before the code, in order.
- **Go**: modules, such as `github.com/google/uuid@v1.6.0`, or `module => folder` for one on this computer.

**Add a folder…** adds a folder, which brings everything in it: its `.dll` files for C#, its `.js` files for
JavaScript, in the order of their names, or its Go modules, the folder itself or each folder in it with a `go.mod`.

Libraries are fetched the first time a script runs, and kept, so each is fetched once; fetching and building have
up to five minutes of their own, apart from the script's time limit. NuGet packages come from the feeds on
Settings / Workflow Settings, nuget.org unless changed: any number, each a feed's address or a folder of `.nupkg`
files. A package's latest version is the highest any feed has, and a version comes from the first feed that has it.

## Working folder

A script step can name the folder its code works in, which may use paths such as `{{vars.folder}}`; without one, it
uses the working folder on Settings / Workflow Settings, if there is one, and the folder must be there.

- A **Go** program runs in it, so `os.ReadFile("data.csv")` reads from it.
- **C#** has it as `WorkingFolder`, and `InFolder("data.csv")` gives the path of a file in it. C# runs inside Joktan,
  whose own working folder is not changed for one script.
- **JavaScript** has it as `workingFolder`, with `readText(path)`, `writeText(path, text)` and `fileExists(path)`,
  which reach only the files in it. Without a working folder, JavaScript has no files at all.

Settings / Workflow Settings also has **usings for every C# script**, such as `System.Globalization`, and libraries
for every script of each language, which come before a step's own; the editor says which they are. Go only allows
imports a program uses, so a module there is available to import, not imported.

Only run code you trust: C# runs with everything Joktan can do.

## C#

A C# script sees `Input`, `Vars` and `Last` as `JsonNode` values, with `System.Text.Json.Nodes`, `System.IO` and
LINQ at hand, and `Log("...")` writes to the run's log. Changes to `Vars` are kept.

```csharp
var count = (int?)Vars["count"] ?? 0;
Vars["count"] = count + 1;
Log($"Seen {count + 1} times");
return new { total = (double)Input!["price"]! * (int)Input["qty"]! };
```

A condition step can be written in C# too, as an expression that gives true or false.

## JavaScript

A JavaScript script sees `input`, `vars` and `last` as plain objects, and `log(...)` writes to the run's log. It is
the body of a function, so `return` gives the result. What it changes in `vars` is kept for the steps after. It runs
inside Joktan without the network, and with files only in its working folder; errors say which line of your code
they come from.

```javascript
const items = input.items ?? [];
vars.seen = (vars.seen ?? 0) + items.length;
return { names: items.map(i => i.name), total: items.reduce((sum, i) => sum + i.price, 0) };
```

## Go

A Go script is a whole program in `package main`. The run's data comes in on stdin as
`{"input": ..., "vars": ..., "last": ...}`; write the result to stdout as JSON. Lines written to stderr go to the run's
log, and a program that stops with an exit code other than 0 fails the step.

Go must be installed, and Joktan builds the program with the go command on Settings / Workflow Settings; **Check**
there shows the version it finds. A program is built once, the first time it runs, and the build counts towards its
time limit, so a first run can take a few seconds.
