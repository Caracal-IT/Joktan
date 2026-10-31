using System;
using System.Linq;
using System.Text.Json.Nodes;
using Joktan.Mqtt.ViewModels;
using Xunit;

namespace Joktan.Mqtt.Tests.ViewModels;

public class FieldRuleViewModelTests
{
    private static readonly FieldType[] AllTypes = Enum.GetValues<FieldType>();

    [Fact]
    public void There_is_a_name_for_every_type_and_kind_of_value()
    {
        Assert.Equal(AllTypes.Length, FieldRuleViewModel.TypeNames.Count);
        Assert.Equal(Enum.GetValues<FieldRuleViewModel.ValueKind>().Length, FieldRuleViewModel.ValueKindNames.Count);
    }

    [Theory]
    [InlineData(FieldType.Counter, true, false, false, false, false)]
    [InlineData(FieldType.RandomInteger, false, true, false, false, false)]
    [InlineData(FieldType.RandomNumber, false, true, true, false, false)]
    [InlineData(FieldType.OneOf, false, false, false, true, false)]
    [InlineData(FieldType.Cycle, false, false, false, true, false)]
    [InlineData(FieldType.Fixed, false, false, false, false, true)]
    [InlineData(FieldType.Guid, false, false, false, false, false)]
    public void Shows_the_settings_of_the_type_chosen(FieldType type, bool counter, bool range, bool decimals, bool values, bool isFixed)
    {
        var model = new FieldRuleViewModel { TypeIndex = (int)type };

        Assert.Equal(counter, model.ShowCounter);
        Assert.Equal(range, model.ShowRange);
        Assert.Equal(decimals, model.ShowDecimals);
        Assert.Equal(values, model.ShowValues);
        Assert.Equal(isFixed, model.ShowFixed);
    }

    [Theory]
    [InlineData(FieldRuleViewModel.ValueKind.Text, true, false, false)]
    [InlineData(FieldRuleViewModel.ValueKind.Number, true, false, false)]
    [InlineData(FieldRuleViewModel.ValueKind.Json, true, false, false)]
    [InlineData(FieldRuleViewModel.ValueKind.Boolean, false, true, false)]
    [InlineData(FieldRuleViewModel.ValueKind.Null, false, false, false)]
    [InlineData(FieldRuleViewModel.ValueKind.List, false, false, true)]
    public void Shows_the_editor_of_the_kind_of_fixed_value(FieldRuleViewModel.ValueKind kind, bool text, bool toggle, bool choices)
    {
        var model = new FieldRuleViewModel { TypeIndex = (int)FieldType.Fixed, ValueKindIndex = (int)kind };

        Assert.Equal(text, model.ShowFixedText);
        Assert.Equal(toggle, model.ShowFixedSwitch);
        Assert.Equal(choices, model.ShowChoices);
    }

    [Fact]
    public void The_fixed_value_editors_stay_hidden_for_other_types()
    {
        var model = new FieldRuleViewModel { TypeIndex = (int)FieldType.Guid, ValueKindIndex = (int)FieldRuleViewModel.ValueKind.Json };

        Assert.False(model.ShowFixedText);
    }

    [Fact]
    public void A_negative_selection_counts_as_the_first()
    {
        var model = new FieldRuleViewModel { TypeIndex = -1, ValueKindIndex = -1 };

        Assert.Equal(FieldType.UnixSeconds, model.Type);
        Assert.Equal(FieldRuleViewModel.ValueKind.Text, model.Kind);
    }

    [Fact]
    public void A_field_needs_a_path()
    {
        Assert.Equal(
            "Every varying field needs a field path",
            Assert.Throws<FormatException>(new FieldRuleViewModel { Path = "  " }.ToRule).Message);
    }

    [Fact]
    public void Makes_a_counter()
    {
        var rule = new FieldRuleViewModel { Path = " n ", TypeIndex = (int)FieldType.Counter, Start = "5", Step = "-2" }.ToRule();

        Assert.Equal("n", rule.Path);
        Assert.Equal((5, -2), (rule.Start, rule.Step));
    }

    [Fact]
    public void Makes_a_random_number_accepting_a_decimal_comma()
    {
        var rule = new FieldRuleViewModel { Path = "x", TypeIndex = (int)FieldType.RandomNumber, Min = "1,5", Max = "2.5", Decimals = "3" }.ToRule();

        Assert.Equal((1.5, 2.5, 3), (rule.Min, rule.Max, rule.Decimals));
    }

    [Theory]
    [InlineData(FieldType.Counter, "the start must be a whole number")]
    [InlineData(FieldType.RandomInteger, "the minimum must be a whole number")]
    [InlineData(FieldType.RandomNumber, "the minimum must be a number")]
    public void Says_which_setting_is_not_a_number(FieldType type, string expected)
    {
        var model = new FieldRuleViewModel { Path = "a", TypeIndex = (int)type, Start = "x", Min = "x" };

        Assert.Equal($"Field a: {expected}", Assert.Throws<FormatException>(model.ToRule).Message);
    }

    [Fact]
    public void Passes_on_what_the_rule_check_finds_without_its_quotes()
    {
        var model = new FieldRuleViewModel { Path = "a", TypeIndex = (int)FieldType.RandomInteger, Min = "3", Max = "2" };

        Assert.Equal("Field a: min must not be greater than max", Assert.Throws<FormatException>(model.ToRule).Message);
    }

    [Fact]
    public void Reads_comma_separated_values_as_JSON_or_else_text()
    {
        var rule = new FieldRuleViewModel { Path = "a", TypeIndex = (int)FieldType.OneOf, Values = """ 42, true, "quoted", plain, null ,, """ }.ToRule();

        Assert.Equal("[42,true,\"quoted\",\"plain\",null]", new JsonArray(rule.Values.Select(v => v?.DeepClone()).ToArray()).ToJsonString());
    }

    [Fact]
    public void A_list_needs_a_value()
    {
        var model = new FieldRuleViewModel { Path = "a", TypeIndex = (int)FieldType.Cycle };

        Assert.Equal("Field a: values must list at least one value", Assert.Throws<FormatException>(model.ToRule).Message);
    }

    [Theory]
    [InlineData(FieldRuleViewModel.ValueKind.Text, "42", "\"42\"")]
    [InlineData(FieldRuleViewModel.ValueKind.Number, "42", "42")]
    [InlineData(FieldRuleViewModel.ValueKind.Number, "1,5", "1.5")]
    [InlineData(FieldRuleViewModel.ValueKind.Json, """{"a":1}""", """{"a":1}""")]
    public void Makes_a_fixed_value(FieldRuleViewModel.ValueKind kind, string text, string expected)
    {
        var rule = new FieldRuleViewModel { Path = "a", TypeIndex = (int)FieldType.Fixed, ValueKindIndex = (int)kind, FixedText = text }.ToRule();

        Assert.Equal(expected, rule.Value!.ToJsonString());
    }

    [Fact]
    public void Makes_a_true_or_false_and_a_null()
    {
        var flag = new FieldRuleViewModel { Path = "a", TypeIndex = (int)FieldType.Fixed, ValueKindIndex = (int)FieldRuleViewModel.ValueKind.Boolean, FixedBool = true }.ToRule();
        var nothing = new FieldRuleViewModel { Path = "a", TypeIndex = (int)FieldType.Fixed, ValueKindIndex = (int)FieldRuleViewModel.ValueKind.Null }.ToRule();

        Assert.True(flag.Value!.GetValue<bool>());
        Assert.Null(nothing.Value);
    }

    [Theory]
    [InlineData(FieldRuleViewModel.ValueKind.Number, "abc", "Field a: the value must be a number")]
    [InlineData(FieldRuleViewModel.ValueKind.Json, "{", "Field a: the value is not valid JSON")]
    [InlineData(FieldRuleViewModel.ValueKind.List, "", "Field a: list at least one allowed value")]
    public void Says_what_is_wrong_with_a_fixed_value(FieldRuleViewModel.ValueKind kind, string text, string message)
    {
        var model = new FieldRuleViewModel { Path = "a", TypeIndex = (int)FieldType.Fixed, ValueKindIndex = (int)kind, FixedText = text };

        Assert.Equal(message, Assert.Throws<FormatException>(model.ToRule).Message);
    }

    [Fact]
    public void The_dropdown_follows_the_list_and_keeps_the_value_picked()
    {
        var model = new FieldRuleViewModel { TypeIndex = (int)FieldType.Fixed, ValueKindIndex = (int)FieldRuleViewModel.ValueKind.List };

        model.Values = "a, b";
        Assert.Equal(["a", "b"], model.Choices);
        Assert.Equal("a", model.Choice);

        model.Choice = "b";
        model.Values = "b, c";
        Assert.Equal("b", model.Choice);

        model.Values = "c";
        Assert.Equal("c", model.Choice);
    }

    [Fact]
    public void A_list_starts_with_the_value_written_so_far()
    {
        var model = new FieldRuleViewModel { TypeIndex = (int)FieldType.Fixed, FixedText = "a, b" };

        model.ValueKindIndex = (int)FieldRuleViewModel.ValueKind.List;

        Assert.Equal("a, b", model.Values);
        Assert.Equal(["a", "b"], model.Choices);
    }

    [Fact]
    public void Makes_a_fixed_value_from_a_list()
    {
        var model = new FieldRuleViewModel
        {
            Path = "a",
            TypeIndex = (int)FieldType.Fixed,
            ValueKindIndex = (int)FieldRuleViewModel.ValueKind.List,
            Values = "1, 2",
        };
        model.Choice = "2";

        var rule = model.ToRule();

        Assert.Equal(2, rule.Value!.GetValue<int>());
        Assert.Equal(2, rule.Values.Count);
    }

    public static TheoryData<string> Rules =>
    [
        """{"type":"unixSeconds"}""",
        """{"type":"guid"}""",
        """{"type":"counter","start":5,"step":2}""",
        """{"type":"randomInteger","min":1,"max":9}""",
        """{"type":"randomNumber","min":0.5,"max":1.5,"decimals":4}""",
        """{"type":"oneOf","values":[1,"a",true]}""",
        """{"type":"cycle","values":["x","y"]}""",
        """{"type":"fixed","value":"text"}""",
        """{"type":"fixed","value":42}""",
        """{"type":"fixed","value":1.5}""",
        """{"type":"fixed","value":true}""",
        """{"type":"fixed","value":false}""",
        """{"type":"fixed","value":null}""",
        """{"type":"fixed","value":{"a":[1,2]}}""",
        """{"type":"fixed","value":[1,2]}""",
        """{"type":"fixed","value":2,"values":[1,2]}""",
        """{"type":"fixed","value":"b","values":["a","b"]}""",
    ];

    [Theory]
    [MemberData(nameof(Rules))]
    public void A_rule_shown_in_the_editor_comes_back_the_same(string json)
    {
        var settings = MessageSettings.Parse("{\"fields\":{\"p\":" + json + "}}");

        var rule = new FieldRuleViewModel(settings.Fields[0]).ToRule();

        Assert.Equal(settings.ToJson(), (settings with { Fields = [rule] }).ToJson());
    }
}
