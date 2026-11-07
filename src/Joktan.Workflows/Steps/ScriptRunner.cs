using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Jint;
using Joktan.Core;
using Joktan.Workflows.Engine;
using Joktan.Workflows.Model;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace Joktan.Workflows.Steps;

/// <summary>
/// What C# code in a workflow can use. A script sees these as plain names: <c>Input</c>, <c>Vars</c>,
/// <c>Last</c>, <c>Log("…")</c> and <c>Cancel</c>, and what it returns is the step's result.
/// </summary>
public sealed class ScriptGlobals
{
    /// <summary>What the run was started with.</summary>
    public JsonNode? Input { get; init; }

    /// <summary>The run's variables, which a script may change.</summary>
    public JsonObject Vars { get; init; } = [];

    /// <summary>The result of the step before.</summary>
    public JsonNode? Last { get; init; }

    /// <summary>The folder the code works in, from the step or Settings / Workflow Settings, or null when none is set.</summary>
    public string? WorkingFolder { get; init; }

    /// <summary>A path within the working folder, such as InFolder("data.csv"); an absolute path stays as it is.</summary>
    public string InFolder(string path)
        => WorkingFolder is null ? throw new InvalidOperationException("The step has no working folder; set one on the step or on Settings / Workflow Settings.")
            : System.IO.Path.GetFullPath(System.IO.Path.Combine(WorkingFolder, path));

    /// <summary>A message the MQTT wait is testing, if any.</summary>
    public JsonNode? Message { get; init; }

    /// <summary>Set when the run is killed or the script runs out of time.</summary>
    public CancellationToken Cancel { get; init; }

    internal Action<string> Logger { get; init; } = _ => { };

    /// <summary>Writes a line in the run's log.</summary>
    public void Log(string text) => Logger(text);
}

/// <summary>Runs the code of script steps: C# inside Joktan, and Go with the go command, each within the time limit of the settings.</summary>
public sealed class ScriptRunner(WorkflowSettings settings)
{
    public const string CSharpTemplate = """
        // Input, Vars, Last and Log("...") are at hand. What you return is the step's result.
        var name = (string?)Input?["name"] ?? "world";
        Log($"Hello, {name}");
        return new { greeting = $"Hello, {name}", at = DateTime.Now };
        """;

    public const string GoTemplate = """
        package main

        // The run's data comes in on stdin as {"input": ..., "vars": ..., "last": ...}.
        // Write the step's result to stdout as JSON. Lines on stderr go to the run's log.

        import (
        	"encoding/json"
        	"fmt"
        	"os"
        )

        type Data struct {
        	Input map[string]any `json:"input"`
        	Vars  map[string]any `json:"vars"`
        	Last  any            `json:"last"`
        }

        func main() {
        	var data Data
        	if err := json.NewDecoder(os.Stdin).Decode(&data); err != nil {
        		fmt.Fprintln(os.Stderr, err)
        		os.Exit(1)
        	}
        	name, _ := data.Input["name"].(string)
        	if name == "" {
        		name = "world"
        	}
        	fmt.Fprintln(os.Stderr, "Hello,", name)
        	json.NewEncoder(os.Stdout).Encode(map[string]any{"greeting": "Hello, " + name})
        }
        """;

    public const string JavaScriptTemplate = """
        // input, vars, last and log("...") are at hand. What you return is the step's result,
        // and what you change in vars is kept for the steps after.
        const name = input?.name ?? "world";
        log(`Hello, ${name}`);
        vars.greeted = (vars.greeted ?? 0) + 1;
        return { greeting: `Hello, ${name}`, at: new Date().toISOString() };
        """;

    // A script is the body of a function, so that it can return its result.
    private const string JavaScriptStart = "(function () {\n";
    private const string JavaScriptEnd = "\n})()";

    private static readonly string[] Imports =
    [
        "System", "System.Collections.Generic", "System.IO", "System.Linq", "System.Net.Http", "System.Text", "System.Text.Json",
        "System.Text.Json.Nodes", "System.Text.RegularExpressions", "System.Threading", "System.Threading.Tasks",
    ];

    /// <summary>How long fetching a script's libraries and building it may take, which is apart from its time limit.</summary>
    public static readonly TimeSpan PreparationLimit = TimeSpan.FromMinutes(5);

    // Compiled once per piece of code and its references, as compiling takes far longer than running.
    private static readonly ConcurrentDictionary<string, Lazy<ScriptRunner<object>>> Compiled = new();
    private static readonly SemaphoreSlim GoBuilds = new(1, 1);
    private static readonly System.Net.Http.HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(2) };

    private static ScriptOptions Options => ScriptOptions.Default
        .WithImports(Imports)
        .WithReferences(typeof(object).Assembly, typeof(Enumerable).Assembly, typeof(JsonNode).Assembly, typeof(System.Net.Http.HttpClient).Assembly,
            typeof(System.Text.RegularExpressions.Regex).Assembly, typeof(ScriptGlobals).Assembly, typeof(System.Text.Json.JsonSerializer).Assembly)
        .WithOptimizationLevel(Microsoft.CodeAnalysis.OptimizationLevel.Release);

    public Task<StepResult> RunAsync(WorkflowStep step, StepContext context)
    {
        var folder = WorkingFolder(step, context);
        return step.Language switch
        {
            ScriptLanguage.Go => GoAsync(step.Code, Libraries(step), folder, context),
            ScriptLanguage.JavaScript => JavaScriptAsync(step.Code, Libraries(step), folder, context),
            _ => CSharpAsync(step.Code, Libraries(step), folder, context),
        };
    }

    // The step's working folder, or that of the settings, which must be there; null when neither names one.
    private string? WorkingFolder(WorkflowStep step, StepContext context)
    {
        var named = step.WorkingFolder.Trim().Length > 0 ? context.Render(step.WorkingFolder).Trim() : settings.WorkingFolder.Trim();
        if (named.Length == 0)
            return null;
        var folder = LocalPath(named);
        return Directory.Exists(folder) ? folder : throw new StepFailure($"There is no working folder {folder}.");
    }

    // The libraries every script of the language has, from the settings, then the step's own.
    private List<string> Libraries(WorkflowStep step)
    {
        var global = step.Language switch
        {
            ScriptLanguage.Go => settings.GoLibraries,
            ScriptLanguage.JavaScript => settings.JavaScriptLibraries,
            _ => settings.CSharpLibraries,
        };
        return global.Concat(step.Libraries).Select(l => l.Trim()).Where(l => l.Length > 0).Distinct().ToList();
    }

    /// <summary>The usings every C# script has: those always there, then those on Settings / Workflow Settings.</summary>
    public IReadOnlyList<string> CSharpImports => [.. Imports, .. settings.CSharpImports.Select(i => i.Trim().TrimEnd(';')).Where(i => i.Length > 0)];

    /// <summary>What is wrong with a step's C# or JavaScript, fetching its libraries first; empty when nothing is.</summary>
    public async Task<IReadOnlyList<string>> CheckAsync(WorkflowStep step, CancellationToken cancel)
    {
        try
        {
            switch (step.Language)
            {
                case ScriptLanguage.CSharp:
                    using (var prepare = Prepare(cancel, out var token))
                    {
                        var references = await CSharpReferencesAsync(Libraries(step), token);
                        var script = CSharpScript.Create<object>(step.Code, OptionsWith(references, CSharpImports), typeof(ScriptGlobals));
                        return script.Compile().Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).Select(d => d.ToString()).ToList();
                    }
                case ScriptLanguage.JavaScript:
                    using (var prepare = Prepare(cancel, out var token))
                        await JavaScriptLibrariesAsync(Libraries(step), token);
                    return JavaScriptProblem(step.Code) is { } problem ? [problem] : [];
                default:
                    return [];
            }
        }
        catch (StepFailure e)
        {
            return [e.Message];
        }
    }

    /// <summary>Runs C# that gives a value, such as a condition, against some data.</summary>
    public async Task<JsonNode?> EvaluateAsync(string code, JsonObject data, StepContext context)
    {
        // An expression alone, such as (int)Vars["count"] > 3, is given back as it is.
        var expression = code.Trim().TrimEnd(';');
        var script = expression.Contains("return", StringComparison.Ordinal) ? code : $"return {expression};";
        using var limit = Limit(context.Cancel, out var token);
        return JsonText.From(await RunCSharpAsync(script, Globals(data, context, token), token, context.Cancel));
    }

    /// <summary>Why the C# does not compile, or nothing; for the designer.</summary>
    public static IReadOnlyList<string> CompileProblems(string code, IReadOnlyList<string>? references = null)
    {
        var script = CSharpScript.Create<object>(code, OptionsWith(references ?? []), typeof(ScriptGlobals));
        return script.Compile().Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).Select(d => d.ToString()).ToList();
    }

    private static ScriptOptions OptionsWith(IReadOnlyList<string> references, IReadOnlyList<string>? imports = null)
    {
        var options = imports is null ? Options : Options.WithImports(imports);
        return references.Count == 0 ? options : options.AddReferences(references.Select(r => Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(r)));
    }

    /// <summary>The assemblies a C# script's libraries give it: .dll files, and NuGet packages with what they depend on.</summary>
    private async Task<IReadOnlyList<string>> CSharpReferencesAsync(IReadOnlyList<string> libraries, CancellationToken cancel)
    {
        var files = new List<string>();
        var packages = new List<(string Id, string? Version)>();
        foreach (var library in libraries)
        {
            // A folder brings every assembly in it.
            if (IsLocal(library) && Directory.Exists(LocalPath(library)))
                files.AddRange(Directory.EnumerateFiles(LocalPath(library), "*.dll").Order(StringComparer.Ordinal));
            else if (library.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                files.Add(LocalFile(library));
            else
                packages.Add(NuGetPackages.Parse(library));
        }

        if (packages.Count > 0)
        {
            try
            {
                files.AddRange(await new NuGetPackages(settings.Feeds, Path.Combine(WorkflowSettings.LibrariesFolder, "nuget")).RestoreAsync(packages, cancel));
            }
            catch (Exception e) when (e is System.Net.Http.HttpRequestException or IOException or InvalidDataException or JsonException or System.Xml.XmlException)
            {
                throw new StepFailure($"The NuGet packages could not be fetched: {e.Message}");
            }
        }

        return files;
    }

    // A file or folder a library names, with ~ for the home folder.
    private static string LocalPath(string library)
        => Path.GetFullPath(library.StartsWith('~')
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), library[1..].TrimStart('/', '\\'))
            : library);

    private static bool IsLocal(string library)
        => !library.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !library.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
           && (library.StartsWith('~') || Path.IsPathRooted(library));

    private static string LocalFile(string library)
        => LocalPath(library) is var path && File.Exists(path) ? path : throw new StepFailure($"There is no library {library}.");

    /// <summary>The source of each of a JavaScript script's libraries: files as they are, addresses fetched once and kept.</summary>
    private static async Task<IReadOnlyList<(string Name, string Source)>> JavaScriptLibrariesAsync(IReadOnlyList<string> libraries, CancellationToken cancel)
    {
        var sources = new List<(string, string)>();
        foreach (var library in libraries)
        {
            if (!library.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !library.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                // A folder brings every script in it, in the order of their names.
                if (Directory.Exists(LocalPath(library)))
                {
                    foreach (var file in Directory.EnumerateFiles(LocalPath(library), "*.js").Order(StringComparer.Ordinal))
                        sources.Add((file, await File.ReadAllTextAsync(file, cancel)));
                }
                else
                {
                    sources.Add((library, await File.ReadAllTextAsync(LocalFile(library), cancel)));
                }

                continue;
            }

            var kept = Path.Combine(WorkflowSettings.LibrariesFolder, "js", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(library)))[..16].ToLowerInvariant() + ".js");
            if (!File.Exists(kept))
            {
                string source;
                try
                {
                    using var response = await Http.GetAsync(library, cancel);
                    if (!response.IsSuccessStatusCode)
                        throw new StepFailure($"{library} could not be fetched: {(int)response.StatusCode} {response.ReasonPhrase}");
                    source = await response.Content.ReadAsStringAsync(cancel);
                }
                catch (System.Net.Http.HttpRequestException e)
                {
                    throw new StepFailure($"{library} could not be fetched: {e.Message}");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(kept)!);
                await File.WriteAllTextAsync(kept, source, cancel);
            }

            sources.Add((library, await File.ReadAllTextAsync(kept, cancel)));
        }

        return sources;
    }

    /// <summary>Why the JavaScript cannot be read, or null; for the designer.</summary>
    public static string? JavaScriptProblem(string code)
    {
        try
        {
            Jint.Engine.PrepareScript(JavaScriptStart + code + JavaScriptEnd);
            return null;
        }
        catch (ScriptPreparationException e) when (e.InnerException is Acornima.ParseErrorException parse)
        {
            return Where(parse.Description, parse.LineNumber);
        }
    }

    // A line number counts from the user's code, not the function it is wrapped in.
    private static string Where(string message, int line) => line > 1 ? $"Line {line - 1}: {message}" : message;

    private async Task<StepResult> JavaScriptAsync(string code, IReadOnlyList<string> libraries, string? folder, StepContext context)
    {
        IReadOnlyList<(string Name, string Source)> sources;
        using (var prepare = Prepare(context.Cancel, out var preparing))
            sources = await Run(() => JavaScriptLibrariesAsync(libraries, preparing), preparing, context);
        using var limit = Limit(context.Cancel, out var token);
        var data = new JsonObject
        {
            [Templates.Input] = context.Data[Templates.Input]?.DeepClone(),
            [Templates.Vars] = context.Vars.DeepClone(),
            [Templates.Last] = context.Data[Templates.Last]?.DeepClone(),
        };
        try
        {
            // On a thread of its own, so a long loop cannot hold up the app; the engine stops itself at the limit.
            var (result, vars) = await Task.Run(() => RunJavaScript(code, sources, data, folder, context, token), token).WaitAsync(token);

            // What the script did to vars is kept, as a C# script's changes are.
            if (vars is JsonObject changed)
            {
                context.Vars.Clear();
                foreach (var (name, value) in changed.ToList())
                {
                    changed.Remove(name);
                    context.Vars[name] = value;
                }
            }

            return StepResult.Next(result);
        }
        catch (Exception e) when ((e is OperationCanceledException || e is Jint.Runtime.ExecutionCanceledException) && token.IsCancellationRequested && !context.Cancel.IsCancellationRequested)
        {
            throw new StepFailure($"The script took longer than {settings.ScriptTimeLimitSeconds}s.");
        }
        catch (Jint.Runtime.ExecutionCanceledException)
        {
            throw new OperationCanceledException(context.Cancel);
        }
        catch (Jint.Runtime.JavaScriptException e)
        {
            throw new StepFailure("The JavaScript failed: " + Where(e.Error.ToString(), e.Location.Start.Line));
        }
        catch (Acornima.ParseErrorException e)
        {
            throw new StepFailure("The JavaScript cannot be read: " + Where(e.Description, e.LineNumber));
        }
    }

    private static (JsonNode? Result, JsonNode? Vars) RunJavaScript(string code, IReadOnlyList<(string Name, string Source)> libraries, JsonObject data,
        string? folder, StepContext context, CancellationToken cancel)
    {
        var engine = new Jint.Engine(options => options
            .CancellationToken(cancel)
            .LimitMemory(256 * 1024 * 1024)
            .LimitRecursion(1000));
        var json = new Jint.Native.Json.JsonParser(engine);
        foreach (var name in new[] { Templates.Input, Templates.Vars, Templates.Last })
            engine.SetValue(name, json.Parse(JsonText.Of(data[name])));
        engine.SetValue("log", new Action<Jint.Native.JsValue>(value =>
        {
            var text = value.IsString() ? value.AsString() : AsJson(engine, value);
            context.Engine.Post(() => context.Log(text));
        }));

        // With a working folder, and only then, the script can read and write the files in it.
        engine.SetValue("workingFolder", folder);
        if (folder is not null)
        {
            string Inside(string path)
            {
                var full = Path.GetFullPath(Path.Combine(folder, path));
                return full.StartsWith(Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                    ? full
                    : throw new Jint.Runtime.JavaScriptException($"{path} is outside the working folder.");
            }

            engine.SetValue("readText", new Func<string, string>(path => File.ReadAllText(Inside(path))));
            engine.SetValue("writeText", new Action<string, string>((path, text) =>
            {
                var full = Inside(path);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllText(full, text);
            }));
            engine.SetValue("fileExists", new Func<string, bool>(path => File.Exists(Inside(path))));
        }

        // Libraries run first, in order, so what they define is there for the script.
        foreach (var (name, source) in libraries)
        {
            try
            {
                engine.Execute(source, name);
            }
            catch (Jint.Runtime.JavaScriptException e)
            {
                throw new StepFailure($"The library {name} failed: {e.Error}");
            }
            catch (ScriptPreparationException e)
            {
                throw new StepFailure($"The library {name} cannot be read: {e.InnerException?.Message ?? e.Message}");
            }
        }

        var result = engine.Evaluate(JavaScriptStart + code + JavaScriptEnd);
        return (FromJavaScript(engine, result), FromJavaScript(engine, engine.GetValue(Templates.Vars)));
    }

    private static string AsJson(Jint.Engine engine, Jint.Native.JsValue value)
        => new Jint.Native.Json.JsonSerializer(engine).Serialize(value) is var text && !text.IsUndefined() ? text.AsString() : "undefined";

    private static JsonNode? FromJavaScript(Jint.Engine engine, Jint.Native.JsValue value)
        => value.IsUndefined() || value.IsNull() ? null : Templates.Parse(AsJson(engine, value));

    private async Task<StepResult> CSharpAsync(string code, IReadOnlyList<string> libraries, string? folder, StepContext context)
    {
        IReadOnlyList<string> references;
        using (var prepare = Prepare(context.Cancel, out var preparing))
            references = await Run(() => CSharpReferencesAsync(libraries, preparing), preparing, context);
        using var limit = Limit(context.Cancel, out var token);
        var result = await RunCSharpAsync(code, Globals(context.Data, context, token, folder), token, context.Cancel, references);
        return StepResult.Next(JsonText.From(result));
    }

    // Fetching libraries and building, within their own limit.
    private static async Task<T> Run<T>(Func<Task<T>> preparation, CancellationToken limit, StepContext context)
    {
        try
        {
            return await preparation();
        }
        catch (OperationCanceledException) when (limit.IsCancellationRequested && !context.Cancel.IsCancellationRequested)
        {
            throw new StepFailure($"Fetching the libraries and building took longer than {PreparationLimit.TotalMinutes:0} minutes.");
        }
    }

    private static ScriptGlobals Globals(JsonObject data, StepContext context, CancellationToken cancel, string? folder = null) => new()
    {
        WorkingFolder = folder,
        Input = data[Templates.Input],
        Vars = data[Templates.Vars] as JsonObject ?? context.Vars,
        Last = data[Templates.Last],
        Message = data[Templates.Message],
        Cancel = cancel,
        Logger = text => context.Engine.Post(() => context.Log(text)),
    };

    private async Task<object?> RunCSharpAsync(string code, ScriptGlobals globals, CancellationToken limit, CancellationToken run, IReadOnlyList<string>? references = null)
    {
        references ??= [];
        var imports = CSharpImports;
        var key = code + "\n\0" + string.Join("\n", references) + "\n\0" + string.Join(";", imports);
        var runner = Compiled.GetOrAdd(key, _ => new Lazy<ScriptRunner<object>>(() => Compile(code, key, references, imports))).Value;
        try
        {
            // On a thread of its own, so a script that never awaits cannot hold up the app; one that ignores Cancel is left behind at the limit.
            var running = Task.Run(() => runner(globals, limit), limit);
            return await running.WaitAsync(limit);
        }
        catch (OperationCanceledException) when (limit.IsCancellationRequested && !run.IsCancellationRequested)
        {
            throw new StepFailure($"The script took longer than {settings.ScriptTimeLimitSeconds}s.");
        }
    }

    private static ScriptRunner<object> Compile(string code, string key, IReadOnlyList<string> references, IReadOnlyList<string> imports)
    {
        var script = CSharpScript.Create<object>(code, OptionsWith(references, imports), typeof(ScriptGlobals));
        var errors = script.Compile().Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error).ToList();
        if (errors.Count > 0)
        {
            Compiled.TryRemove(key, out _);

            // An error with no place in the code comes from the usings of Settings / Workflow Settings.
            if (errors.Any(e => !e.Location.IsInSource))
                throw new StepFailure($"A using on Settings / Workflow Settings cannot be found ({string.Join(", ", imports.Except(Imports))}): "
                                      + string.Join(" ", errors.Where(e => !e.Location.IsInSource).Take(2).Select(e => e.GetMessage())));
            throw new StepFailure("The C# does not compile: " + string.Join(" ", errors.Take(3).Select(e => e.ToString())));
        }

        return script.CreateDelegate();
    }

    private static CancellationTokenSource Prepare(CancellationToken run, out CancellationToken token)
    {
        var limit = CancellationTokenSource.CreateLinkedTokenSource(run);
        limit.CancelAfter(PreparationLimit);
        token = limit.Token;
        return limit;
    }

    private CancellationTokenSource Limit(CancellationToken run, out CancellationToken token)
    {
        var limit = CancellationTokenSource.CreateLinkedTokenSource(run);
        limit.CancelAfter(TimeSpan.FromSeconds(settings.ScriptTimeLimitSeconds));
        token = limit.Token;
        return limit;
    }

    private async Task<StepResult> GoAsync(string code, IReadOnlyList<string> libraries, string? folder, StepContext context)
    {
        string program;
        using (var prepare = Prepare(context.Cancel, out var preparing))
            program = await Run(() => BuildGoAsync(code, libraries, preparing), preparing, context);
        using var limit = Limit(context.Cancel, out var token);
        try
        {
            var input = new JsonObject
            {
                [Templates.Input] = context.Data[Templates.Input]?.DeepClone(),
                [Templates.Vars] = context.Vars.DeepClone(),
                [Templates.Last] = context.Data[Templates.Last]?.DeepClone(),
            };
            var (exit, output, errors) = await RunProcessAsync(program, [], folder ?? Path.GetDirectoryName(program)!, JsonText.Of(input), token);
            foreach (var line in errors.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                context.Log(line);
            if (exit != 0)
                throw new StepFailure($"The Go program stopped with exit code {exit}{(errors.Trim().Length > 0 ? ": " + errors.Trim().Split('\n')[^1] : ".")}");
            return StepResult.Next(output.Trim().Length == 0 ? null : Templates.Parse(output.Trim()));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested && !context.Cancel.IsCancellationRequested)
        {
            throw new StepFailure($"The Go program took longer than {settings.ScriptTimeLimitSeconds}s.");
        }
    }

    // Built once into a folder named after the code and its modules, and run from there afterwards.
    private async Task<string> BuildGoAsync(string code, IReadOnlyList<string> libraries, CancellationToken cancel)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code + "\n\0" + string.Join("\n", libraries))))[..16].ToLowerInvariant();
        var folder = Path.Combine(WorkflowSettings.GoBuildFolder, hash);
        var program = Path.Combine(folder, OperatingSystem.IsWindows() ? "step.exe" : "step");
        if (File.Exists(program))
            return program;

        await GoBuilds.WaitAsync(cancel);
        try
        {
            if (File.Exists(program))
                return program;
            Directory.CreateDirectory(folder);

            // A module on this computer is put in with a replace; others are fetched with go get, at the version given.
            var mod = new StringBuilder("module joktan/step\n\ngo 1.21\n");
            var fetched = new List<string>();
            var locals = new List<(string Module, string Folder)>();
            foreach (var library in libraries)
            {
                var arrow = library.IndexOf("=>", StringComparison.Ordinal);
                if (arrow >= 0)
                {
                    var named = LocalPath(library[(arrow + 2)..].Trim());
                    if (!Directory.Exists(named))
                        throw new StepFailure($"There is no folder {named} for the module {library[..arrow].Trim()}.");
                    locals.Add((library[..arrow].Trim(), named));
                }
                else if (IsLocal(library) && Directory.Exists(LocalPath(library)))
                {
                    // A folder brings the module it is, or every module in the folders in it, each named by its go.mod.
                    var found = GoModules(LocalPath(library));
                    if (found.Count == 0)
                        throw new StepFailure($"{library} holds no Go module: no go.mod in it, or in the folders in it.");
                    locals.AddRange(found);
                }
                else
                {
                    fetched.Add(library);
                }
            }

            foreach (var (module, local) in locals)
            {
                // Quoted, as a folder may have spaces in it.
                mod.Append($"\nrequire {module} v0.0.0\nreplace {module} => \"{local.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"\n");
            }

            await File.WriteAllTextAsync(Path.Combine(folder, "go.mod"), mod.ToString(), cancel);
            await File.WriteAllTextAsync(Path.Combine(folder, "main.go"), code, cancel);
            foreach (var module in fetched)
                await RunGoAsync(["get", module], folder, $"The module {module} could not be fetched", cancel);
            if (libraries.Count > 0)
                await RunGoAsync(["mod", "tidy"], folder, "The modules could not be sorted out", cancel);
            var (exit, _, errors) = await RunProcessAsync(settings.Go, ["build", "-o", Path.GetFileName(program), "."], folder, null, cancel);
            if (exit != 0)
                throw new StepFailure("The Go does not build: " + string.Join(" ", errors.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(5)));
            return program;
        }
        finally
        {
            GoBuilds.Release();
        }
    }

    // The module a folder is, by its go.mod, or the modules of the folders in it.
    private static List<(string Module, string Folder)> GoModules(string folder)
    {
        static string? ModuleOf(string directory)
        {
            var mod = Path.Combine(directory, "go.mod");
            return File.Exists(mod)
                ? File.ReadLines(mod).Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("module ", StringComparison.Ordinal))?["module ".Length..].Trim().Trim('"')
                : null;
        }

        if (ModuleOf(folder) is { } own)
            return [(own, folder)];
        return Directory.EnumerateDirectories(folder).Order(StringComparer.Ordinal)
            .Select(d => (Module: ModuleOf(d), Folder: d)).Where(m => m.Module is not null).Select(m => (m.Module!, m.Folder)).ToList();
    }

    // Runs the go command in a build folder, failing the step with what it said if it fails.
    private async Task RunGoAsync(IReadOnlyList<string> arguments, string folder, string failure, CancellationToken cancel)
    {
        var (exit, _, errors) = await RunProcessAsync(settings.Go, arguments, folder, null, cancel);
        if (exit != 0)
            throw new StepFailure($"{failure}: " + string.Join(" ", errors.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(5)));
    }

    private static async Task<(int Exit, string Output, string Errors)> RunProcessAsync(string command, IEnumerable<string> arguments, string folder, string? input, CancellationToken cancel)
    {
        var start = new ProcessStartInfo(command)
        {
            WorkingDirectory = folder,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        Process process;
        try
        {
            process = Process.Start(start) ?? throw new StepFailure($"{command} could not be started.");
        }
        catch (Win32Exception e)
        {
            throw new StepFailure($"{command} could not be started: {e.Message}. Install Go, or set where it is on Settings / Workflow Settings.");
        }

        using (process)
        {
            var output = process.StandardOutput.ReadToEndAsync(cancel);
            var errors = process.StandardError.ReadToEndAsync(cancel);
            if (input is not null)
                await process.StandardInput.WriteAsync(input);
            process.StandardInput.Close();
            try
            {
                await process.WaitForExitAsync(cancel);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // It had already stopped.
                }

                throw;
            }

            return (process.ExitCode, await output, await errors);
        }
    }
}
