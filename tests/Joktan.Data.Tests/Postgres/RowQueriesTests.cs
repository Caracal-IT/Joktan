using System;
using System.Collections.Generic;
using System.Linq;
using Joktan.Data.Postgres;
using Xunit;

namespace Joktan.Data.Tests.Postgres;

public class RowQueriesTests
{
    [Fact]
    public void A_select_gives_its_rows_as_json_with_filters_as_parameters()
    {
        var all = RowQueries.Select("s", "t", [], []);
        Assert.Equal("SELECT coalesce(json_agg(t), '[]')::text FROM (SELECT * FROM \"s\".\"t\") t", all.Sql);
        Assert.Empty(all.Parameters);

        var some = RowQueries.Select("s", "t", ["id", "na\"me"],
            [new("qty", FilterKind.Greater, "5"), new("name", FilterKind.Contains, "it's"), new("gone", FilterKind.Equal)], "id", descending: true, limit: 10);
        Assert.Equal("SELECT coalesce(json_agg(t), '[]')::text FROM (SELECT \"id\", \"na\"\"me\" FROM \"s\".\"t\" WHERE \"qty\" > $1 AND strpos(\"name\"::text, $2) > 0 "
                     + "AND \"gone\" IS NULL ORDER BY \"id\" DESC LIMIT 10) t", some.Sql);
        Assert.Equal(["5", "it's"], some.Parameters);
    }

    [Theory]
    [InlineData(FilterKind.Equal, "\"c\" = $1")]
    [InlineData(FilterKind.NotEqual, "\"c\" IS DISTINCT FROM $1")]
    [InlineData(FilterKind.GreaterOrEqual, "\"c\" >= $1")]
    [InlineData(FilterKind.Less, "\"c\" < $1")]
    [InlineData(FilterKind.LessOrEqual, "\"c\" <= $1")]
    [InlineData(FilterKind.StartsWith, "starts_with(\"c\"::text, $1)")]
    [InlineData(FilterKind.IsEmpty, "(\"c\" IS NULL OR \"c\"::text = '')")]
    [InlineData(FilterKind.IsNotEmpty, "(\"c\" IS NOT NULL AND \"c\"::text <> '')")]
    public void Each_kind_of_filter_has_its_test(FilterKind kind, string test)
        => Assert.EndsWith($"WHERE {test}) t", RowQueries.Select("s", "t", [], [new("c", kind, "v")]).Sql);

    [Fact]
    public void Not_equal_to_nothing_is_not_null()
        => Assert.Contains("WHERE \"c\" IS NOT NULL", RowQueries.Select("s", "t", [], [new("c", FilterKind.NotEqual)]).Sql);

    [Fact]
    public void Rows_are_inserted_in_batches_and_given_back()
    {
        var one = Assert.Single(RowQueries.Insert("s", "t", ["a", "b"], [["1", null], ["2", "x"]]));
        Assert.Equal("WITH t AS (INSERT INTO \"s\".\"t\" (\"a\", \"b\") VALUES ($1, $2), ($3, $4) RETURNING *) SELECT coalesce(json_agg(t), '[]')::text FROM t", one.Sql);
        Assert.Equal(["1", null, "2", "x"], one.Parameters);

        var rows = Enumerable.Range(0, 2_500).Select(i => (IReadOnlyList<string?>)[i.ToString()]).ToList();
        var batches = RowQueries.Insert("s", "t", ["a"], rows);
        Assert.Equal([1_000, 1_000, 500], batches.Select(b => b.Parameters.Count));
        Assert.EndsWith("($1000) RETURNING *) SELECT coalesce(json_agg(t), '[]')::text FROM t", batches[1].Sql);

        var wide = Enumerable.Range(0, 100).Select(i => $"c{i}").ToList();
        var wideRows = Enumerable.Range(0, 301).Select(_ => (IReadOnlyList<string?>)wide.Select(c => (string?)c).ToList()).ToList();
        Assert.Equal([30_000, 100], RowQueries.Insert("s", "t", wide, wideRows).Select(b => b.Parameters.Count));

        Assert.Equal(2, RowQueries.Insert("s", "t", [], [[], []]).Count(b => b.Sql.Contains("DEFAULT VALUES")));
        Assert.Throws<ArgumentException>(() => RowQueries.Insert("s", "t", ["a", "b"], [["1"]]));
    }

    [Fact]
    public void An_update_sets_values_before_the_filters_parameters()
    {
        var update = RowQueries.Update("s", "t", [new("name", "new"), new("qty", null)], [new("id", FilterKind.Equal, "7")]);
        Assert.Equal("WITH t AS (UPDATE \"s\".\"t\" SET \"name\" = $1, \"qty\" = $2 WHERE \"id\" = $3 RETURNING *) SELECT coalesce(json_agg(t), '[]')::text FROM t", update.Sql);
        Assert.Equal(["new", null, "7"], update.Parameters);
        Assert.Throws<ArgumentException>(() => RowQueries.Update("s", "t", [], [new("id", FilterKind.Equal, "1")]));
    }

    [Fact]
    public void Updates_and_deletes_are_never_made_without_filters_with_values()
    {
        IReadOnlyList<RowFilter>[] unsafeFilters =
        [
            [],
            [new("id", FilterKind.Equal)],
            [new("id", FilterKind.Equal, "  ")],
            [new("id", FilterKind.Equal, "1"), new("name", FilterKind.Contains, "")],
            [new("done", FilterKind.IsNotEmpty)],
            [new("done", FilterKind.IsEmpty), new("sent", FilterKind.IsNotEmpty)],
        ];
        foreach (var filters in unsafeFilters)
        {
            Assert.NotNull(RowQueries.NarrowingProblem(filters));
            Assert.Throws<ArgumentException>(() => RowQueries.Update("s", "t", [new("name", "x")], filters));
            Assert.Throws<ArgumentException>(() => RowQueries.Delete("s", "t", filters));
        }

        Assert.Null(RowQueries.NarrowingProblem([new("id", FilterKind.Equal, "0"), new("done", FilterKind.IsEmpty)]));
        var delete = RowQueries.Delete("s", "t", [new("id", FilterKind.Less, "10"), new("done", FilterKind.IsEmpty)]);
        Assert.Equal("WITH t AS (DELETE FROM \"s\".\"t\" WHERE \"id\" < $1 AND (\"done\" IS NULL OR \"done\"::text = '') RETURNING *) SELECT coalesce(json_agg(t), '[]')::text FROM t", delete.Sql);
        Assert.Equal(["10"], delete.Parameters);
    }

    [Fact]
    public void An_upsert_updates_the_other_columns_of_the_row_its_keys_match()
    {
        var upsert = Assert.Single(RowQueries.Upsert("s", "t", ["id", "name", "qty"], ["id"], [["1", "tea", "2"]]));
        Assert.Equal("WITH t AS (INSERT INTO \"s\".\"t\" (\"id\", \"name\", \"qty\") VALUES ($1, $2, $3) ON CONFLICT (\"id\") DO UPDATE SET \"name\" = EXCLUDED.\"name\", "
                     + "\"qty\" = EXCLUDED.\"qty\" RETURNING *) SELECT coalesce(json_agg(t), '[]')::text FROM t", upsert.Sql);
        Assert.Contains("ON CONFLICT (\"a\", \"b\") DO UPDATE SET \"a\" = EXCLUDED.\"a\" RETURNING *", RowQueries.Upsert("s", "t", ["a", "b"], ["a", "b"], [["1", "2"]])[0].Sql);
        Assert.Throws<ArgumentException>(() => RowQueries.Upsert("s", "t", ["a"], [], [["1"]]));
        Assert.Throws<ArgumentException>(() => RowQueries.Upsert("s", "t", ["a"], ["b"], [["1"]]));
    }
}
