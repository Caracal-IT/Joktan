using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Joktan.Data.Postgres;
using Joktan.Workflows.Engine;
using Joktan.Workflows.Model;
using Npgsql;

namespace Joktan.Workflows.Steps;

/// <summary>
/// Database steps: read the rows of a table that pass the step's filters, insert or upsert a row or one for each
/// item of a list, or update or delete the rows that pass the filters. Each runs in one transaction, so it happens
/// whole or not at all. An update or delete never runs without filters whose values are all there.
/// </summary>
public static class DatabaseSteps
{
    public static async Task<StepResult> RunAsync(WorkflowStep step, StepContext context)
    {
        if (StepTypes.DatabaseActionOf(step.Type) is not { } action)
            throw new StepFailure($"{step.Type} is not a database step.");
        if (step.Table.Trim().Length == 0)
            throw new StepFailure("Choose the table the step works on.");

        // Everything is worked out before the database is opened, so a step that cannot run sends nothing.
        var statements = action switch
        {
            DatabaseAction.Insert => Insert(step, context, upsert: false),
            DatabaseAction.Upsert => Insert(step, context, upsert: true),
            DatabaseAction.Update => [Update(step, context)],
            DatabaseAction.Delete => [Delete(step, context)],
            _ => [Read(step, context)],
        };
        if (statements.Count == 0)
        {
            context.Log("There were no rows to insert.");
            Assign(step, context, []);
            return StepResult.Next(Shape(step, []));
        }

        var (connection, error) = context.Services.OpenDatabase(step.Database);
        if (connection is null)
            throw new StepFailure(error ?? "The database could not be opened.");

        IReadOnlyList<string> answers;
        await using (connection)
        {
            try
            {
                answers = await connection.RunAsync(statements, context.Cancel);
            }
            catch (Exception e) when (e is NpgsqlException or System.Net.Sockets.SocketException or TimeoutException or InvalidOperationException
                                          || (e is OperationCanceledException && !context.Cancel.IsCancellationRequested))
            {
                throw new StepFailure($"The database said: {(e is PostgresException postgres ? postgres.MessageText : e.Message)}");
            }
        }

        var rows = new JsonArray(answers.SelectMany(Rows).Select(r => r?.DeepClone()).ToArray());
        var table = $"{Schema(step)}.{step.Table.Trim()}";
        context.Log(action switch
        {
            DatabaseAction.Read => $"Read {Count(rows.Count)} from {table}.",
            DatabaseAction.Insert => $"Inserted {Count(rows.Count)} into {table}.",
            DatabaseAction.Upsert => $"Inserted or updated {Count(rows.Count)} of {table}.",
            DatabaseAction.Update => $"Updated {Count(rows.Count)} of {table}.",
            _ => $"Deleted {Count(rows.Count)} from {table}.",
        });
        Assign(step, context, rows);
        return StepResult.Next(Shape(step, rows));
    }

    // The rows read or changed, or the first of them.
    private static JsonNode? Shape(WorkflowStep step, JsonArray rows) => step.FirstRowOnly ? rows.FirstOrDefault()?.DeepClone() : rows;

    private static string Schema(WorkflowStep step) => step.Schema.Trim() is { Length: > 0 } schema ? schema : "public";

    private static SqlText Read(WorkflowStep step, StepContext context)
    {
        var columns = Used(step).Select(c => c.Column.Trim()).Distinct().ToList();
        return RowQueries.Select(Schema(step), step.Table.Trim(), columns, Filters(step, context), step.OrderBy.Trim(), step.Descending, Math.Max(0, step.Limit));
    }

    // Each column asked for goes in its variable, from the rows read or changed: the first row's value, or every row's.
    private static void Assign(WorkflowStep step, StepContext context, JsonArray rows)
    {
        foreach (var column in step.Columns.Where(c => c.Column.Trim().Length > 0 && c.Variable.Trim().Length > 0))
        {
            var name = column.Column.Trim();
            context.Vars[column.Variable.Trim()] = column.AllRows
                ? new JsonArray(rows.Select(r => (r as JsonObject)?[name]?.DeepClone()).ToArray())
                : (rows.FirstOrDefault() as JsonObject)?[name]?.DeepClone();
        }
    }

    private static IReadOnlyList<SqlText> Insert(WorkflowStep step, StepContext context, bool upsert)
    {
        var columns = Used(step).ToList();
        var names = columns.Select(c => c.Column.Trim()).ToList();
        if (names.Distinct().Count() != names.Count)
            throw new StepFailure("A column is given a value more than once.");
        var keys = columns.Where(c => c.Match).Select(c => c.Column.Trim()).ToList();
        if (upsert && keys.Count == 0)
            throw new StepFailure("Choose the key columns that say which row is the one to update.");

        var values = RowValues(step, context, columns);

        // A row with no key would always be inserted, never matched.
        if (upsert && values.SelectMany(row => keys.Select(k => row[names.IndexOf(k)])).Any(string.IsNullOrWhiteSpace))
            throw new StepFailure($"A key column ({string.Join(", ", keys)}) came out empty, so the row to update is not known. Nothing was changed.");
        if (values.Count == 0)
            return [];
        return upsert
            ? RowQueries.Upsert(Schema(step), step.Table.Trim(), names, keys, values)
            : RowQueries.Insert(Schema(step), step.Table.Trim(), names, values);
    }

    // One row's values, or with many rows one for each item, which is in the item variable while its row is worked out, as a for each does.
    private static List<IReadOnlyList<string?>> RowValues(WorkflowStep step, StepContext context, List<DatabaseColumn> columns)
    {
        if (!step.ManyRows)
            return [Values(columns, context)];

        var items = context.Value(step.Items) switch
        {
            JsonArray list => list,
            null => [],
            _ => throw new StepFailure($"{step.Items} is not a list, so its items cannot be made rows."),
        };
        var variable = step.ItemVariable.Trim() is { Length: > 0 } named ? named : "item";
        var had = context.Vars.TryGetPropertyValue(variable, out var before);
        var values = new List<IReadOnlyList<string?>>();
        try
        {
            foreach (var item in items)
            {
                context.Vars[variable] = item?.DeepClone();
                values.Add(Values(columns, context));
            }
        }
        finally
        {
            if (had)
                context.Vars[variable] = before?.DeepClone();
            else
                context.Vars.Remove(variable);
        }

        return values;
    }

    private static SqlText Update(WorkflowStep step, StepContext context)
    {
        var columns = Used(step).ToList();
        if (columns.Count == 0)
            throw new StepFailure("Choose the columns to change, and their values.");
        var filters = NarrowingFilters(step, context, "changed");
        var values = Values(columns, context);
        return RowQueries.Update(Schema(step), step.Table.Trim(), columns.Select((c, i) => new KeyValuePair<string, string?>(c.Column.Trim(), values[i])).ToList(), filters);
    }

    private static SqlText Delete(WorkflowStep step, StepContext context)
        => RowQueries.Delete(Schema(step), step.Table.Trim(), NarrowingFilters(step, context, "deleted"));

    // The filters of an update or delete, which must say which rows with values that are there: a missing value never chooses every row.
    private static List<RowFilter> NarrowingFilters(WorkflowStep step, StepContext context, string done)
    {
        var filters = Filters(step, context);
        if (filters.Count == 0)
            throw new StepFailure($"Add a filter for the rows, so that not every row is {done}. Nothing was {done}.");
        if (filters.FirstOrDefault(f => RowQueries.TakesValue(f.Kind) && string.IsNullOrWhiteSpace(f.Value)) is { } blank)
        {
            var written = step.Filters.First(f => f.Column.Trim() == blank.Column).Value;
            throw new StepFailure($"The filter on {blank.Column} came out empty ({(written.Trim().Length == 0 ? "no value is given" : written.Trim())}), "
                                  + $"so the rows are not known. Nothing was {done}.");
        }

        if (!filters.Any(f => RowQueries.TakesValue(f.Kind)))
            throw new StepFailure($"At least one filter must compare a column with a value, so that not every row is {done}. Nothing was {done}.");
        return filters;
    }

    // The columns a read reads, or an insert, update or upsert gives values to.
    private static IEnumerable<DatabaseColumn> Used(WorkflowStep step)
        => step.Columns.Where(c => c.Column.Trim().Length > 0 && (step.Type == StepType.DatabaseRead || c.SetsValue));

    private static IReadOnlyList<string?> Values(IEnumerable<DatabaseColumn> columns, StepContext context)
        => columns.Select(c => Text(context.Value(c.Value))).ToList();

    private static List<RowFilter> Filters(WorkflowStep step, StepContext context)
        => step.Filters.Where(f => f.Column.Trim().Length > 0)
            .Select(f => new RowFilter(f.Column.Trim(), Kind(f.Operator), Conditions.TakesRight(f.Operator) ? Text(context.Value(f.Value)) : null))
            .ToList();

    /// <summary>How a filter's comparison is made in SQL; C#, which a filter cannot use, is taken as equals.</summary>
    public static FilterKind Kind(CompareOperator op) => op switch
    {
        CompareOperator.NotEquals => FilterKind.NotEqual,
        CompareOperator.GreaterThan => FilterKind.Greater,
        CompareOperator.GreaterOrEqual => FilterKind.GreaterOrEqual,
        CompareOperator.LessThan => FilterKind.Less,
        CompareOperator.LessOrEqual => FilterKind.LessOrEqual,
        CompareOperator.Contains => FilterKind.Contains,
        CompareOperator.StartsWith => FilterKind.StartsWith,
        CompareOperator.IsEmpty => FilterKind.IsEmpty,
        CompareOperator.IsNotEmpty => FilterKind.IsNotEmpty,
        _ => FilterKind.Equal,
    };

    /// <summary>
    /// A value as the text PostgreSQL reads as the column's type: text as it is, other JSON as JSON, which suits
    /// numbers, true and false, and json columns; nothing is NULL.
    /// </summary>
    public static string? Text(JsonNode? value) => value switch
    {
        null => null,
        JsonValue text when text.GetValueKind() == JsonValueKind.String => text.GetValue<string>(),
        _ => value.ToJsonString(Core.JsonFormats.Compact),
    };

    private static JsonArray Rows(string json)
    {
        try
        {
            return JsonNode.Parse(json) as JsonArray ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string Count(int rows) => rows == 1 ? "1 row" : $"{rows} rows";
}
