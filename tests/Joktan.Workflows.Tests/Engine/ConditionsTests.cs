using System.Text.Json.Nodes;
using Joktan.Workflows.Engine;
using Joktan.Workflows.Model;
using Xunit;

namespace Joktan.Workflows.Tests.Engine;

public class ConditionsTests
{
    private static bool Compare(string left, CompareOperator op, string right = "null")
        => Conditions.Compare(JsonNode.Parse(left), op, JsonNode.Parse(right));

    [Theory]
    [InlineData("3", CompareOperator.Equals, "\"3\"", true)]
    [InlineData("3.0", CompareOperator.Equals, "3", true)]
    [InlineData("\"ok\"", CompareOperator.Equals, "\"ok\"", true)]
    [InlineData("{\"a\":1}", CompareOperator.Equals, "{\"a\":1}", true)]
    [InlineData("\"ok\"", CompareOperator.NotEquals, "\"no\"", true)]
    [InlineData("10", CompareOperator.GreaterThan, "\"9\"", true)]
    [InlineData("\"10\"", CompareOperator.GreaterThan, "\"9\"", true)]
    [InlineData("\"b\"", CompareOperator.GreaterThan, "\"a\"", true)]
    [InlineData("5", CompareOperator.GreaterOrEqual, "5", true)]
    [InlineData("4", CompareOperator.LessThan, "5", true)]
    [InlineData("5", CompareOperator.LessOrEqual, "4", false)]
    [InlineData("\"alarm raised\"", CompareOperator.Contains, "\"raised\"", true)]
    [InlineData("[1, 2, 3]", CompareOperator.Contains, "2", true)]
    [InlineData("{\"id\": 1}", CompareOperator.Contains, "\"id\"", true)]
    [InlineData("\"plant/a\"", CompareOperator.StartsWith, "\"plant/\"", true)]
    public void Values_compare_as_numbers_when_both_are_numbers(string left, CompareOperator op, string right, bool expected)
        => Assert.Equal(expected, Compare(left, op, right));

    [Theory]
    [InlineData("null", true)]
    [InlineData("\"\"", true)]
    [InlineData("[]", true)]
    [InlineData("{}", true)]
    [InlineData("0", false)]
    [InlineData("\"x\"", false)]
    public void Empty_means_nothing_there(string value, bool empty)
    {
        Assert.Equal(empty, Compare(value, CompareOperator.IsEmpty));
        Assert.Equal(!empty, Compare(value, CompareOperator.IsNotEmpty));
        Assert.False(Conditions.TakesRight(CompareOperator.IsEmpty));
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("\"false\"", false)]
    [InlineData("\"yes\"", true)]
    [InlineData("0", false)]
    [InlineData("2", true)]
    [InlineData("null", false)]
    public void What_counts_as_true(string value, bool expected)
        => Assert.Equal(expected, Conditions.IsTrue(JsonNode.Parse(value)));
}
