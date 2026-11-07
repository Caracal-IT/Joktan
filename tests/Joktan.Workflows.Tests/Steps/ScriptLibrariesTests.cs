using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Joktan.Tests.TestSupport;
using Joktan.Workflows.Engine;
using Joktan.Workflows.Model;
using Joktan.Workflows.Steps;
using Joktan.Workflows.Tests.Engine;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Joktan.Workflows.Tests.Steps;

[Collection(ProcessStateCollection.Name)]
public class ScriptLibrariesTests : EngineTest
{
    // A package in a folder feed: an assembly built here from C#, and what it depends on.
    private static void Pack(string feed, string id, string version, string framework, string code, params (string Id, string Version)[] dependencies)
    {
        var references = AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic && a.Location.Length > 0
            && (a.GetName().Name is "System.Runtime" or "System.Private.CoreLib" or "netstandard" || dependencies.Any(d => d.Id == a.GetName().Name)))
            .Select(a => MetadataReference.CreateFromFile(a.Location)).ToList();
        foreach (var (dependency, depVersion) in dependencies)
        {
            using var archive = ZipFile.OpenRead(Path.Combine(feed, $"{dependency}.{depVersion}.nupkg"));
            var dll = archive.Entries.First(e => e.Name.EndsWith(".dll"));
            using var memory = new MemoryStream();
            dll.Open().CopyTo(memory);
            references.Add(MetadataReference.CreateFromImage(memory.ToArray()));
        }

        var compilation = CSharpCompilation.Create(id, [CSharpSyntaxTree.ParseText(code)], references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.True(emitted.Success, string.Join(" ", emitted.Diagnostics));

        Directory.CreateDirectory(feed);
        using var package = ZipFile.Open(Path.Combine(feed, $"{id}.{version}.nupkg"), ZipArchiveMode.Create);
        using (var writer = new StreamWriter(package.CreateEntry($"{id}.nuspec").Open()))
        {
            var deps = string.Concat(dependencies.Select(d => $"""<dependency id="{d.Id}" version="[{d.Version}, )" />"""));
            writer.Write($"""
                <?xml version="1.0" encoding="utf-8"?>
                <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
                  <metadata><id>{id}</id><version>{version}</version>
                    <dependencies><group targetFramework=".NETFramework4.6.2" /><group targetFramework="{framework}">{deps}</group></dependencies>
                  </metadata>
                </package>
                """);
        }

        using (var dll = package.CreateEntry($"lib/{framework.TrimStart('.').ToLowerInvariant().Replace("netstandard", "netstandard")}/{id}.dll").Open())
            dll.Write(image.ToArray());
        using (var old = package.CreateEntry($"lib/net462/{id}.dll").Open())
            old.Write([1, 2, 3]);
    }

    [Fact]
    public async Task A_CSharp_script_uses_NuGet_packages_and_what_they_depend_on()
    {
        var feed = Data.At("feed");
        var suffix = Guid.NewGuid().ToString("N")[..6];
        Pack(feed, "Shout" + suffix, "1.0.0", "netstandard2.0", $"public static class Shout{suffix} {{ public static string Loud(string s) => s.ToUpperInvariant() + \"!\"; }}");
        Pack(feed, "Greet" + suffix, "2.1.0", "net8.0", $"public static class Greet{suffix} {{ public static string Hello(string n) => Shout{suffix}.Loud(\"hello \" + n); }}", ("Shout" + suffix, "1.0.0"));
        Settings.NuGetFeeds = [feed];
        var step = Flow.CSharp($"return Greet{suffix}.Hello((string)Input![\"name\"]!);");
        step.Libraries = [$"Greet{suffix} 2.1.0"];

        var run = await RunAsync(new Flow().Then(step), """{"name": "ada"}""");

        Assert.Equal(RunStatus.Completed, run.Status);
        Assert.Equal("\"HELLO ADA!\"", JsonText.Of(run.Output));
        Assert.True(Directory.Exists(Path.Combine(WorkflowSettings.LibrariesFolder, "nuget", "shout" + suffix, "1.0.0")));
    }

    [Fact]
    public async Task The_latest_version_is_taken_when_none_is_given_and_a_missing_package_fails_the_step()
    {
        var feed = Data.At("feed");
        var suffix = Guid.NewGuid().ToString("N")[..6];
        Pack(feed, "Ver" + suffix, "1.0.0", "net8.0", $"public static class Ver{suffix} {{ public static int N => 1; }}");
        Pack(feed, "Ver" + suffix, "1.10.0", "net8.0", $"public static class Ver{suffix} {{ public static int N => 10; }}");
        Pack(feed, "Ver" + suffix, "2.0.0-beta", "net8.0", $"public static class Ver{suffix} {{ public static int N => 99; }}");
        Settings.NuGetFeeds = [feed];
        var latest = Flow.CSharp($"return Ver{suffix}.N;");
        latest.Libraries = [$"Ver{suffix}"];
        var missing = Flow.CSharp("return 1;");
        missing.Libraries = ["Nothing.Here 1.0.0"];

        Assert.Equal("10", JsonText.Of((await RunAsync(new Flow().Then(latest))).Output));
        var failed = await RunAsync(new Flow().Then(missing));
        Assert.Equal(RunStatus.Failed, failed.Status);
        Assert.Contains("Nothing.Here", failed.Error);
    }

    [Fact]
    public async Task The_code_is_checked_with_its_libraries()
    {
        var step = new WorkflowStep { Type = StepType.Script, Code = "return Missing.Thing;", Libraries = [Data.At("none.dll")] };

        var problems = await Engine.Scripts.CheckAsync(step, TestContext.Current.CancellationToken);

        Assert.Contains("There is no library", Assert.Single(problems));
    }

    [Theory]
    [InlineData("net10.0", true)]
    [InlineData("net8.0", true)]
    [InlineData("netstandard2.1", true)]
    [InlineData(".NETStandard2.0", true)]
    [InlineData("netcoreapp3.1", true)]
    [InlineData("net462", false)]
    [InlineData(".NETFramework4.7.2", false)]
    [InlineData("net99.0", false)]
    public void Frameworks_that_suit_this_dotnet_are_ranked_newest_first(string framework, bool suits)
    {
        Assert.Equal(suits, NuGetPackages.Rank(framework) >= 0);
        Assert.True(NuGetPackages.Rank("net8.0") > NuGetPackages.Rank("netstandard2.1"));
        Assert.True(NuGetPackages.Rank("net10.0") > NuGetPackages.Rank("net8.0"));
    }

    [Fact]
    public void Versions_compare_by_their_numbers_with_releases_after_previews()
    {
        Assert.True(NuGetPackages.Compare("1.10.0", "1.9.0") > 0);
        Assert.True(NuGetPackages.Compare("2.0.0", "2.0.0-beta") > 0);
        Assert.Equal(0, NuGetPackages.Compare("1.0", "1.0"));
        Assert.Equal(("Newtonsoft.Json", "13.0.3"), NuGetPackages.Parse(" Newtonsoft.Json 13.0.3 "));
        Assert.Equal(("Dapper", "2.1.35"), NuGetPackages.Parse("Dapper@2.1.35"));
        Assert.Equal(("Polly", (string?)null), NuGetPackages.Parse("Polly"));
    }

    [Fact]
    public async Task A_JavaScript_library_is_run_before_the_code()
    {
        var library = Data.Write("libs/shout.js", "var shout = function (s) { return s.toUpperCase() + '!'; };");
        var step = new WorkflowStep { Type = StepType.Script, Language = ScriptLanguage.JavaScript, Code = "return shout(input.word);", Libraries = [library] };

        var run = await RunAsync(new Flow().Then(step), """{"word": "hey"}""");

        Assert.Equal("\"HEY!\"", JsonText.Of(run.Output));
    }

    [Fact]
    public async Task A_JavaScript_library_that_fails_or_is_missing_fails_the_step()
    {
        var broken = Data.Write("libs/broken.js", "throw new Error('nope');");
        var failing = new WorkflowStep { Type = StepType.Script, Language = ScriptLanguage.JavaScript, Code = "return 1;", Libraries = [broken] };
        var missing = new WorkflowStep { Type = StepType.Script, Language = ScriptLanguage.JavaScript, Code = "return 1;", Libraries = [Data.At("libs", "gone.js")] };

        Assert.Contains("broken.js failed", (await RunAsync(new Flow().Then(failing))).Error);
        Assert.Contains("There is no library", (await RunAsync(new Flow().Then(missing))).Error);
    }

    [Fact]
    public async Task A_Go_program_uses_a_module_on_this_computer()
    {
        Assert.SkipUnless(GoIsInstalled(), "Go is not installed.");
        var module = Data.At("greet");
        Data.Write("greet/go.mod", "module example.com/greet\n\ngo 1.21\n");
        Data.Write("greet/greet.go", "package greet\n\nfunc Hello(n string) string { return \"hello \" + n }\n");
        var code = """
            package main

            import (
            	"encoding/json"
            	"os"

            	"example.com/greet"
            )

            func main() {
            	var data struct{ Input map[string]string `json:"input"` }
            	json.NewDecoder(os.Stdin).Decode(&data)
            	json.NewEncoder(os.Stdout).Encode(greet.Hello(data.Input["name"]))
            }
            """ + "\n// " + Guid.NewGuid();
        var step = new WorkflowStep { Type = StepType.Script, Language = ScriptLanguage.Go, Code = code, Libraries = [$"example.com/greet => {module}"] };

        var run = await RunAsync(new Flow("Go", WorkflowKind.Long).Then(step), """{"name": "ada"}""");

        Assert.True(run.Status == RunStatus.Completed, run.Error);
        Assert.Equal("\"hello ada\"", JsonText.Of(run.Output));
    }

    [Fact]
    public async Task Usings_and_libraries_on_the_settings_are_had_by_every_script()
    {
        Settings.CSharpImports = ["System.Globalization", "", " System.Security.Cryptography; "];
        var csharp = Flow.CSharp("return CultureInfo.InvariantCulture.Name + SHA256.HashData(new byte[0]).Length;");
        var library = Data.Write("libs/twice.js", "var twice = function (n) { return n * 2; };");
        Settings.JavaScriptLibraries = [library];
        var javascript = new WorkflowStep { Type = StepType.Script, Language = ScriptLanguage.JavaScript, Code = "return twice(21);" };

        Assert.Equal("\"32\"", JsonText.Of((await RunAsync(new Flow().Then(csharp))).Output));
        Assert.Equal("42", JsonText.Of((await RunAsync(new Flow("Js").Then(javascript))).Output));
        Assert.Empty(await Engine.Scripts.CheckAsync(csharp, TestContext.Current.CancellationToken));
        Assert.Contains("System.Globalization", Engine.Scripts.CSharpImports);
    }

    [Fact]
    public async Task A_using_that_is_not_there_fails_the_step_saying_so()
    {
        Settings.CSharpImports = ["No.Such.Namespace"];

        var run = await RunAsync(new Flow().Then(Flow.CSharp("return 1;")));

        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Contains("Settings / Workflow Settings", run.Error);
        Assert.Contains("No.Such.Namespace", run.Error);
    }

    // An assembly built here, written to a file.
    private static void Dll(string path, string code)
    {
        var references = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && a.Location.Length > 0 && a.GetName().Name is "System.Runtime" or "System.Private.CoreLib")
            .Select(a => MetadataReference.CreateFromFile(a.Location));
        var compilation = CSharpCompilation.Create(Path.GetFileNameWithoutExtension(path), [CSharpSyntaxTree.ParseText(code)], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Assert.True(compilation.Emit(path).Success);
    }

    [Fact]
    public async Task Packages_are_found_across_several_feeds()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        Pack(Data.At("first"), "A" + suffix, "1.0.0", "net8.0", $"public static class A{suffix} {{ public static int N => 1; }}");
        Pack(Data.At("second"), "B" + suffix, "1.0.0", "net8.0", $"public static class B{suffix} {{ public static int N => 2; }}");
        Pack(Data.At("second"), "A" + suffix, "3.0.0", "net8.0", $"public static class A{suffix} {{ public static int N => 3; }}");
        Settings.NuGetFeeds = [Data.At("first"), "", Data.At("second")];
        var step = Flow.CSharp($"return A{suffix}.N * 10 + B{suffix}.N;");
        step.Libraries = [$"A{suffix}", $"B{suffix} 1.0.0"];

        var run = await RunAsync(new Flow().Then(step));

        // The latest A is the one in the second feed.
        Assert.Equal("32", JsonText.Of(run.Output));
    }

    [Fact]
    public async Task A_folder_brings_its_assemblies_to_CSharp()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        Dll(Data.At("dlls", $"One{suffix}.dll"), $"public static class One{suffix} {{ public static int N => 1; }}");
        Dll(Data.At("dlls", $"Two{suffix}.dll"), $"public static class Two{suffix} {{ public static int N => 2; }}");
        var step = Flow.CSharp($"return One{suffix}.N + Two{suffix}.N;");
        step.Libraries = [Data.At("dlls")];

        Assert.Equal("3", JsonText.Of((await RunAsync(new Flow().Then(step))).Output));
    }

    [Fact]
    public async Task A_folder_brings_its_scripts_to_JavaScript_in_the_order_of_their_names()
    {
        Data.Write("js/b.js", "var greeting = prefix + ' world';");
        Data.Write("js/a.js", "var prefix = 'hello';");
        Data.Write("js/notes.txt", "not a script");
        var step = new WorkflowStep { Type = StepType.Script, Language = ScriptLanguage.JavaScript, Code = "return greeting;", Libraries = [Data.At("js")] };

        Assert.Equal("\"hello world\"", JsonText.Of((await RunAsync(new Flow().Then(step))).Output));
    }

    [Fact]
    public async Task A_folder_brings_its_Go_modules()
    {
        Assert.SkipUnless(GoIsInstalled(), "Go is not installed.");
        Data.Write("mods/greet/go.mod", "module example.com/greet\n\ngo 1.21\n");
        Data.Write("mods/greet/greet.go", "package greet\n\nfunc Hello() string { return \"hello\" }\n");
        Data.Write("mods/shout/go.mod", "module example.com/shout\n\ngo 1.21\n");
        Data.Write("mods/shout/shout.go", "package shout\n\nimport \"strings\"\n\nfunc Loud(s string) string { return strings.ToUpper(s) }\n");
        var code = """
            package main

            import (
            	"encoding/json"
            	"os"

            	"example.com/greet"
            	"example.com/shout"
            )

            func main() { json.NewEncoder(os.Stdout).Encode(shout.Loud(greet.Hello())) }
            """ + "\n// " + Guid.NewGuid();
        var step = new WorkflowStep { Type = StepType.Script, Language = ScriptLanguage.Go, Code = code, Libraries = [Data.At("mods")] };

        var run = await RunAsync(new Flow("Go", WorkflowKind.Long).Then(step));

        Assert.True(run.Status == RunStatus.Completed, run.Error);
        Assert.Equal("\"HELLO\"", JsonText.Of(run.Output));
    }

    [Fact]
    public async Task CSharp_and_JavaScript_reach_files_through_their_working_folder()
    {
        Data.Write("work/data.txt", "from the folder");
        var csharp = Flow.CSharp("return File.ReadAllText(InFolder(\"data.txt\")) + \" in \" + System.IO.Path.GetFileName(WorkingFolder);");
        csharp.WorkingFolder = "{{input.folder}}";
        var javascript = new WorkflowStep
        {
            Type = StepType.Script, Language = ScriptLanguage.JavaScript, WorkingFolder = Data.At("work"),
            Code = "writeText('out/copy.txt', readText('data.txt').toUpperCase()); return fileExists('out/copy.txt');",
        };
        var input = $$"""{"folder": {{System.Text.Json.JsonSerializer.Serialize(Data.At("work"))}}}""";

        var first = await RunAsync(new Flow().Then(csharp), input);
        Assert.True(first.Status == RunStatus.Completed, first.Error);
        Assert.Equal("\"from the folder in work\"", JsonText.Of(first.Output));
        Assert.Equal("true", JsonText.Of((await RunAsync(new Flow("Js").Then(javascript))).Output));
        Assert.Equal("FROM THE FOLDER", File.ReadAllText(Data.At("work", "out", "copy.txt")));
    }

    [Fact]
    public async Task JavaScript_cannot_reach_outside_its_working_folder_or_files_without_one()
    {
        Data.Write("work/a.txt", "a");
        Data.Write("secret.txt", "no");
        var escaping = new WorkflowStep { Type = StepType.Script, Language = ScriptLanguage.JavaScript, WorkingFolder = Data.At("work"), Code = "return readText('../secret.txt');" };
        var without = new WorkflowStep { Type = StepType.Script, Language = ScriptLanguage.JavaScript, Code = "return typeof readText + ' ' + workingFolder;" };

        Assert.Contains("outside the working folder", (await RunAsync(new Flow().Then(escaping))).Error);
        Assert.Equal("\"undefined null\"", JsonText.Of((await RunAsync(new Flow("Js").Then(without))).Output));
    }

    [Fact]
    public async Task The_working_folder_of_the_settings_is_used_and_must_be_there()
    {
        Data.Write("default/x.txt", "x");
        Settings.WorkingFolder = Data.At("default");
        var step = Flow.CSharp("return System.IO.Path.GetFileName(WorkingFolder);");

        Assert.Equal("\"default\"", JsonText.Of((await RunAsync(new Flow().Then(step))).Output));

        Settings.WorkingFolder = Data.At("missing");
        Assert.Contains("There is no working folder", (await RunAsync(new Flow().Then(step))).Error);
    }

    [Fact]
    public async Task A_Go_program_runs_in_its_working_folder()
    {
        Assert.SkipUnless(GoIsInstalled(), "Go is not installed.");
        Data.Write("gowork/input.txt", "read from the working folder");
        var code = """
            package main

            import (
            	"encoding/json"
            	"os"
            )

            func main() {
            	text, _ := os.ReadFile("input.txt")
            	json.NewEncoder(os.Stdout).Encode(string(text))
            }
            """ + "\n// " + Guid.NewGuid();
        var step = new WorkflowStep { Type = StepType.Script, Language = ScriptLanguage.Go, Code = code, WorkingFolder = Data.At("gowork") };

        var run = await RunAsync(new Flow("Go", WorkflowKind.Long).Then(step));

        Assert.Equal("\"read from the working folder\"", JsonText.Of(run.Output));
    }

    private static bool GoIsInstalled()
    {
        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("go", "version") { RedirectStandardOutput = true, UseShellExecute = false });
            process?.WaitForExit();
            return process?.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
