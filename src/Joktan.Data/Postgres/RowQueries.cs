using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Joktan.Data.Resources;

namespace Joktan.Data.Postgres;

/// <summary>How a filter compares a column with its value.</summary>
public enum FilterKind
{
    Equal,
    NotEqual,
    Greater,
    GreaterOrEqual,
    Less,
    LessOrEqual,

    /// <summary>The column, as text, has the value in it.</summary>
    Contains,

    /// <summary>The column, as text, starts with the value.</summary>
    StartsWith,

    /// <summary>The column is NULL or empty text; takes no value.</summary>
    IsEmpty,

    /// <summary>The column is neither NULL nor empty text; takes no value.</summary>
    IsNotEmpty,
}

/// <summary>A test a row must pass, on one column. A null value tests for NULL with equal and not equal.</summary>
public sealed record RowFilter(string Column, FilterKind Kind, string? Value = null);

/// <summary>A statement and its parameters, $1, $2 and so on, each sent as text for PostgreSQL to read as its column's type.</summary>
public sealed record SqlText(string Sql, IReadOnlyList<string?> Parameters);

/// <summary>
/// The SQL a workflow's database step sends. Each statement gives one value: the rows it read, inserted or
/// changed, as a JSON array of objects, which keeps numbers, booleans and JSON columns as they are.
/// Names are quoted and values are parameters, as in <see cref="PostgresSql"/>.
/// </summary>
public static class RowQueries
{
    /// <summary>PostgreSQL takes at most 65535 parameters in a statement; rows are inserted in batches well under that.</summary>
    public const int MaxParameters = 30_000;

    /// <summary>The most rows one insert statement holds.</summary>
    public const int MaxRowsPerInsert = 1_000;

    /// <summary>The rows passing every filter, with the columns given or all of them, in order, at most <paramref name="limit"/> unless it is 0.</summary>
    public static SqlText Select(string schema, string table, IReadOnlyList<string> columns, IReadOnlyList<RowFilter> filters,
        string orderBy = "", bool descending = false, int limit = 0)
    {
        var parameters = new List<string?>();
        var list = columns.Count == 0 ? "*" : string.Join(", ", columns.Select(PostgresSql.Quote));
        var sql = new StringBuilder($"SELECT {list} FROM {PostgresSql.Table(schema, table)}");
        Where(sql, filters, parameters);
        if (orderBy.Length > 0)
            sql.Append(" ORDER BY ").Append(PostgresSql.Quote(orderBy)).Append(descending ? " DESC" : "");
        if (limit > 0)
            sql.Append(" LIMIT ").Append(limit);
        return new($"SELECT coalesce(json_agg(t), '[]')::text FROM ({sql}) t", parameters);
    }

    /// <summary>
    /// Inserts rows, each with a value for every column given, in as many statements as the rows need; each
    /// gives the rows as inserted, with their numbered ids and defaults. With no columns, each row is all defaults.
    /// </summary>
    public static IReadOnlyList<SqlText> Insert(string schema, string table, IReadOnlyList<string> columns, IReadOnlyList<IReadOnlyList<string?>> rows)
    {
        var target = PostgresSql.Table(schema, table);
        if (columns.Count == 0)
            return rows.Select(_ => Returning($"INSERT INTO {target} DEFAULT VALUES RETURNING *", [])).ToList();

        var perStatement = Math.Clamp(MaxParameters / columns.Count, 1, MaxRowsPerInsert);
        var names = string.Join(", ", columns.Select(PostgresSql.Quote));
        var statements = new List<SqlText>();
        foreach (var batch in rows.Chunk(perStatement))
        {
            var parameters = new List<string?>();
            var values = batch.Select(row =>
            {
                if (row.Count != columns.Count)
                    throw new ArgumentException(string.Format(Strings.RowQueries_RowValues, row.Count, columns.Count), nameof(rows));
                var start = parameters.Count;
                parameters.AddRange(row);
                return "(" + string.Join(", ", row.Select((_, i) => $"${start + i + 1}")) + ")";
            });
            statements.Add(Returning($"INSERT INTO {target} ({names}) VALUES {string.Join(", ", values)} RETURNING *", parameters));
        }

        return statements;
    }

    /// <summary>
    /// Sets columns to values in every row passing the filters, and gives those rows as changed. There must be a
    /// filter: an update of every row is never made.
    /// </summary>
    public static SqlText Update(string schema, string table, IReadOnlyList<KeyValuePair<string, string?>> values, IReadOnlyList<RowFilter> filters)
    {
        if (values.Count == 0)
            throw new ArgumentException(Strings.RowQueries_NoColumns, nameof(values));
        RequireNarrowing(filters, Strings.RowQueries_UpdateNeedsFilter);
        var parameters = values.Select(v => v.Value).ToList();
        var sql = new StringBuilder($"UPDATE {PostgresSql.Table(schema, table)} SET ");
        sql.Append(string.Join(", ", values.Select((v, i) => $"{PostgresSql.Quote(v.Key)} = ${i + 1}")));
        Where(sql, filters, parameters);
        sql.Append(" RETURNING *");
        return Returning(sql.ToString(), parameters);
    }

    /// <summary>Deletes every row passing the filters, and gives those rows. There must be a filter: every row is never deleted.</summary>
    public static SqlText Delete(string schema, string table, IReadOnlyList<RowFilter> filters)
    {
        RequireNarrowing(filters, Strings.RowQueries_DeleteNeedsFilter);
        var parameters = new List<string?>();
        var sql = new StringBuilder($"DELETE FROM {PostgresSql.Table(schema, table)}");
        Where(sql, filters, parameters);
        sql.Append(" RETURNING *");
        return Returning(sql.ToString(), parameters);
    }

    /// <summary>
    /// Inserts rows as <see cref="Insert"/> does, except that a row whose <paramref name="keys"/> columns match one
    /// already there changes that row's other columns instead. The key columns must be among the columns, and must
    /// have a unique index or be the primary key, as PostgreSQL's ON CONFLICT needs. Each statement gives the rows as they now are.
    /// </summary>
    public static IReadOnlyList<SqlText> Upsert(string schema, string table, IReadOnlyList<string> columns, IReadOnlyList<string> keys,
        IReadOnlyList<IReadOnlyList<string?>> rows)
    {
        if (keys.Count == 0)
            throw new ArgumentException(Strings.RowQueries_NoKeys, nameof(keys));
        if (keys.Any(k => !columns.Contains(k)))
            throw new ArgumentException(Strings.RowQueries_KeyWithoutValue, nameof(keys));

        // With nothing but keys to change, a key is set to itself, so that the row is still given back.
        var others = columns.Where(c => !keys.Contains(c)).ToList();
        var set = string.Join(", ", (others.Count > 0 ? others : [keys[0]]).Select(c => $"{PostgresSql.Quote(c)} = EXCLUDED.{PostgresSql.Quote(c)}"));
        var conflict = $" ON CONFLICT ({string.Join(", ", keys.Select(PostgresSql.Quote))}) DO UPDATE SET {set} RETURNING *";
        return Insert(schema, table, columns, rows)
            .Select(s => s with { Sql = s.Sql.Replace(" RETURNING *) SELECT", conflict + ") SELECT", StringComparison.Ordinal) })
            .ToList();
    }

    /// <summary>Whether a filter needs a value: all but is empty and is not empty.</summary>
    public static bool TakesValue(FilterKind kind) => kind is not (FilterKind.IsEmpty or FilterKind.IsNotEmpty);

    /// <summary>
    /// What is wrong with filters that choose rows to change or delete, or null: there must be one, every value
    /// one needs must be there and not blank, and at least one must compare with a value, so that a missing
    /// value can never choose every row.
    /// </summary>
    public static string? NarrowingProblem(IReadOnlyList<RowFilter> filters)
    {
        if (filters.Count == 0)
            return Strings.RowQueries_NoFilter;
        if (filters.FirstOrDefault(f => TakesValue(f.Kind) && string.IsNullOrWhiteSpace(f.Value)) is { } blank)
            return string.Format(Strings.RowQueries_FilterWithoutValue, blank.Column);
        return filters.Any(f => TakesValue(f.Kind)) ? null : Strings.RowQueries_NoValueFilter;
    }

    /// <param name="sentence">What follows the problem, such as that rows are only changed when filters say which.</param>
    private static void RequireNarrowing(IReadOnlyList<RowFilter> filters, string sentence)
    {
        if (NarrowingProblem(filters) is { } problem)
            throw new ArgumentException(string.Format(Strings.RowQueries_ProblemAndRule, problem, sentence), nameof(filters));
    }

    // A statement that changes rows, made to give them as JSON.
    private static SqlText Returning(string statement, IReadOnlyList<string?> parameters)
        => new($"WITH t AS ({statement}) SELECT coalesce(json_agg(t), '[]')::text FROM t", parameters);

    // The filters, all of which must pass, each value a parameter after those already given.
    private static void Where(StringBuilder sql, IReadOnlyList<RowFilter> filters, List<string?> parameters)
    {
        if (filters.Count == 0)
            return;
        sql.Append(" WHERE ").Append(string.Join(" AND ", filters.Select(f => Test(f, parameters))));
    }

    private static string Test(RowFilter filter, List<string?> parameters)
    {
        var column = PostgresSql.Quote(filter.Column);
        switch (filter.Kind)
        {
            case FilterKind.IsEmpty:
                return $"({column} IS NULL OR {column}::text = '')";
            case FilterKind.IsNotEmpty:
                return $"({column} IS NOT NULL AND {column}::text <> '')";
            case FilterKind.Equal when filter.Value is null:
                return $"{column} IS NULL";
            case FilterKind.NotEqual when filter.Value is null:
                return $"{column} IS NOT NULL";
        }

        parameters.Add(filter.Value);
        var p = "$" + parameters.Count;
        return filter.Kind switch
        {
            FilterKind.NotEqual => $"{column} IS DISTINCT FROM {p}",
            FilterKind.Greater => $"{column} > {p}",
            FilterKind.GreaterOrEqual => $"{column} >= {p}",
            FilterKind.Less => $"{column} < {p}",
            FilterKind.LessOrEqual => $"{column} <= {p}",
            FilterKind.Contains => $"strpos({column}::text, {p}) > 0",
            FilterKind.StartsWith => $"starts_with({column}::text, {p})",
            _ => $"{column} = {p}",
        };
    }
}
