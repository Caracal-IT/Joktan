using System;
using System.Linq;
using System.Threading.Tasks;
using Joktan.Tests.TestSupport;
using Joktan.Workflows.Engine;
using Joktan.Workflows.Model;
using Joktan.Workflows.Steps;
using Joktan.Workflows.Tests.Engine;
using Xunit;

namespace Joktan.Workflows.Tests.Steps;

[Collection(ProcessStateCollection.Name)]
public class ScriptRunnerTests : EngineTest
{
    private static WorkflowStep JavaScript(string code, string result = "")
        => new() { Type = StepType.Script, Language = ScriptLanguage.JavaScript, Code = code, ResultVariable = result };

    [Fact]
    public async Task JavaScript_reads_the_run_and_gives_back_its_result()
    {
        var flow = new Flow().Then(JavaScript("return { doubled: input.n * 2, names: input.items.map(i => i.name) };", "out")).Then(Flow.End("{{vars.out}}"));

        var run = await RunAsync(flow, """{"n": 21, "items": [{"name": "a"}, {"name": "b"}]}""");

        Assert.Equal(RunStatus.Completed, run.Status);
        Assert.Equal("""{"doubled":42,"names":["a","b"]}""", JsonText.Of(run.Output));
    }

    [Fact]
    public async Task What_JavaScript_changes_in_vars_is_kept_for_the_steps_after()
    {
        var flow = new Flow()
            .Then(Flow.Set("count", "1"))
            .Then(JavaScript("vars.count += 1; vars.added = 'yes'; delete vars.gone;"))
            .Then(Flow.End("{\"count\": {{vars.count}}, \"added\": \"{{vars.added}}\"}"));

        var run = await RunAsync(flow);

        Assert.Equal("""{"count":2,"added":"yes"}""", JsonText.Of(run.Output));
    }

    [Fact]
    public async Task JavaScript_can_read_the_result_before_and_write_to_the_log()
    {
        var flow = new Flow()
            .Then(Flow.CSharp("return new { level = 7 };"))
            .Then(JavaScript("log('level ' + last.level); log({ seen: true }); return last.level > 5;"));

        var run = await RunAsync(flow);

        Assert.Equal("true", JsonText.Of(run.Output));
        Assert.Contains(run.Log, l => l.Text == "level 7");
        Assert.Contains(run.Log, l => l.Text == """{"seen":true}""");
    }

    [Fact]
    public async Task A_JavaScript_error_says_which_line_of_the_script()
    {
        var run = await RunAsync(new Flow().Then(JavaScript("const a = 1;\nthrow new Error('no stock');")));

        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Contains("no stock", run.Error);
        Assert.Contains("Line 2", run.Error);
    }

    [Fact]
    public async Task JavaScript_that_runs_too_long_is_stopped()
    {
        Settings.ScriptTimeLimitSeconds = 1;

        var run = await RunAsync(new Flow().Then(JavaScript("while (true) {}")));

        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Contains("longer than 1s", run.Error);
    }

    [Fact]
    public async Task Killing_a_run_stops_its_JavaScript()
    {
        Settings.ScriptTimeLimitSeconds = 600;
        var flow = new Flow().Then(JavaScript("while (true) {}"));
        Library.Save(flow.Workflow);
        var run = Engine.Start(flow.Workflow, null, RunTrigger.Manual);
        await Until(() => run.Steps.Count == 2, "The script never started.");

        Engine.Kill(run.Id);

        var finished = await Engine.WhenFinished(run.Id).WaitAsync(Patience, TestContext.Current.CancellationToken);
        Assert.Equal(RunStatus.Killed, finished.Status);
    }

    [Fact]
    public void JavaScript_that_cannot_be_read_is_found_before_it_runs()
    {
        Assert.Null(ScriptRunner.JavaScriptProblem(ScriptRunner.JavaScriptTemplate));

        var problem = ScriptRunner.JavaScriptProblem("const x = ;\nreturn x;");

        Assert.NotNull(problem);
        Assert.StartsWith("Line 1", problem);
    }

    [Fact]
    public async Task The_JavaScript_template_runs()
    {
        var run = await RunAsync(new Flow().Then(JavaScript(ScriptRunner.JavaScriptTemplate)), """{"name": "Ada"}""");

        Assert.Equal(RunStatus.Completed, run.Status);
        Assert.Equal("Hello, Ada", (string?)run.Output!["greeting"]);
        Assert.Equal(1, (int?)run.Data[Templates.Vars]!["greeted"]);
    }

    [Fact]
    public async Task A_CSharp_script_can_change_vars_and_log()
    {
        var run = await RunAsync(new Flow().Then(Flow.CSharp("Vars[\"x\"] = 5; Log(\"hi\"); return Vars[\"x\"];")));

        Assert.Equal("5", JsonText.Of(run.Output));
        Assert.Contains(run.Log, l => l.Text == "hi");
    }

    [Fact]
    public async Task CSharp_that_does_not_compile_fails_the_step()
    {
        var run = await RunAsync(new Flow().Then(Flow.CSharp("return nope;")));

        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Contains("does not compile", run.Error);
        Assert.NotEmpty(ScriptRunner.CompileProblems("return nope;"));
    }

    [Fact]
    public async Task A_condition_can_be_written_in_CSharp()
    {
        var flow = new Flow();
        var check = flow.Add(new WorkflowStep { Type = StepType.Condition, Operator = CompareOperator.CSharp, Left = "(int)Input![\"n\"]! > 3" });
        var big = flow.Add(Flow.End("\"big\""));
        var small = flow.Add(Flow.End("\"small\""));
        flow.Link(flow.Last, check).Link(check, big, StepOutputs.True).Link(check, small, StepOutputs.False);

        Assert.Equal("\"big\"", JsonText.Of((await RunAsync(flow, """{"n": 4}""")).Output));
        Assert.Equal("\"small\"", JsonText.Of((await RunAsync(flow, """{"n": 2}""")).Output));
    }

    [Fact]
    public async Task A_Go_program_reads_the_run_on_stdin_and_gives_its_result_on_stdout()
    {
        Assert.SkipUnless(await GoIsInstalled(), "Go is not installed.");
        Settings.ScriptTimeLimitSeconds = 300;
        var step = new WorkflowStep { Type = StepType.Script, Language = ScriptLanguage.Go, Code = ScriptRunner.GoTemplate };

        var run = await RunAsync(new Flow("Go", WorkflowKind.Long).Then(step), """{"name": "Ada"}""");

        Assert.Equal(RunStatus.Completed, run.Status);
        Assert.Equal("Hello, Ada", (string?)run.Output!["greeting"]);
        Assert.Contains(run.Log, l => l.Text == "Hello, Ada");
    }

    [Fact]
    public async Task Go_that_does_not_build_fails_the_step()
    {
        Assert.SkipUnless(await GoIsInstalled(), "Go is not installed.");
        Settings.ScriptTimeLimitSeconds = 300;
        var step = new WorkflowStep { Type = StepType.Script, Language = ScriptLanguage.Go, Code = "package main\nfunc main() { nope }" };

        var run = await RunAsync(new Flow("Go", WorkflowKind.Long).Then(step));

        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Contains("does not build", run.Error);
    }

    [Fact]
    public async Task A_missing_go_command_is_reported()
    {
        Settings.GoCommand = "joktan-no-such-go";
        var step = new WorkflowStep { Type = StepType.Script, Language = ScriptLanguage.Go, Code = "package main\nfunc main() {}\n// " + Guid.NewGuid() };

        var run = await RunAsync(new Flow().Then(step));

        Assert.Equal(RunStatus.Failed, run.Status);
        Assert.Contains("could not be started", run.Error);
    }

    private static async Task<bool> GoIsInstalled()
    {
        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("go", "version") { RedirectStandardOutput = true, UseShellExecute = false });
            if (process is null)
                return false;
            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
