using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Joktan.Workflows.Engine;
using Joktan.Workflows.Model;

namespace Joktan.Workflows.Steps;

/// <summary>
/// A parallel step: each of its branches runs as a run of its own, from the step its output leads to, with a copy of
/// the run's data, until it reaches a join. When all have, the run goes on from that join, with each branch's result by
/// name and the variables the branches changed. A branch that fails stops the others.
/// </summary>
public static class ParallelSteps
{
    public static async Task<StepResult> RunAsync(WorkflowStep step, StepContext context)
    {
        var engine = context.Engine;
        var run = context.Run;
        var branches = StepTypes.Cases(step)
            .Select(name => (name, run.Workflow.Links.FirstOrDefault(l => l.From == step.Id && l.Output == StepOutputs.Case(name))?.To))
            .Where(b => b.To is not null)
            .ToList();
        if (branches.Count == 0)
            throw new StepFailure($"{step.Name} has no branch that leads to a step.");

        // After a restart, the branches already started are waited on rather than others started.
        var children = new List<(string Name, WorkflowRun Run)>();
        foreach (var (name, to) in branches)
        {
            var child = run.BranchRuns.TryGetValue(name, out var id) && engine.Find(id) is { Interrupted: false } started ? started : null;
            if (child is null)
            {
                child = engine.StartBranch(run, name, to!);
                run.BranchRuns[name] = child.Id;
                context.Log($"Started branch {name}, run #{child.Serial}.");
            }
            else
            {
                context.Log($"Waiting again for branch {name}, run #{child.Serial}.");
            }

            children.Add((name, child));
        }

        if (run.Kind == WorkflowKind.Long)
            engine.Store.Save(run);

        try
        {
            await context.WaitingAsync(() => WaitForAllAsync(engine, children, context.Cancel));
        }
        catch (StepFailure)
        {
            run.BranchRuns.Clear();
            throw;
        }
        catch (System.OperationCanceledException) when (!context.IsShuttingDown)
        {
            // The branches go with the run, unless Joktan is only closing.
            foreach (var (_, child) in children)
                engine.Kill(child.Id);
            run.BranchRuns.Clear();
            throw;
        }

        run.BranchRuns.Clear();
        var results = new JsonObject();
        foreach (var (name, child) in children)
        {
            results[name] = child.Output?.DeepClone();

            // What a branch changed comes back, branch after branch in their order; what it left alone does not.
            var vars = child.Data[Templates.Vars] as JsonObject ?? [];
            foreach (var (key, value) in vars)
            {
                if (!JsonNode.DeepEquals(value, child.ForkVars?[key]))
                    context.Vars[key] = value?.DeepClone();
            }
        }

        var joins = children.Select(c => c.Run.JoinedAt).OfType<string>().Distinct().ToList();
        if (joins.Count > 1)
            throw new StepFailure($"The branches of {step.Name} met at different joins: {string.Join(", ", joins.Select(j => run.Workflow.Steps.FirstOrDefault(s => s.Id == j)?.Name ?? j))}. Lead them all to one.");

        // With no join, there is nothing after the branches, so the run ends with their results.
        return new StepResult(StepOutputs.Next, results) { NextStep = joins.FirstOrDefault() };
    }

    // Waits until every branch has finished; at the first that fails or is killed, stops the others and fails the step.
    private static async Task<bool> WaitForAllAsync(WorkflowEngine engine, List<(string Name, WorkflowRun Run)> children, CancellationToken cancel)
    {
        var waiting = children.ToDictionary(c => engine.WhenFinished(c.Run.Id).WaitAsync(cancel), c => c);
        while (waiting.Count > 0)
        {
            var done = await Task.WhenAny(waiting.Keys);
            var (name, _) = waiting[done];
            waiting.Remove(done);
            var finished = await done;
            if (finished.Status == RunStatus.Completed)
                continue;

            foreach (var other in waiting.Values)
                engine.Kill(other.Run.Id);
            throw new StepFailure(finished.Status == RunStatus.Killed
                ? $"Branch {name} (run #{finished.Serial}) was killed."
                : $"Branch {name} (run #{finished.Serial}) failed: {finished.Error}");
        }

        return true;
    }
}
