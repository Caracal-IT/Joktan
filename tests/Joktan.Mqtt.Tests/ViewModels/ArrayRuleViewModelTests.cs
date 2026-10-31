using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System;
using Avalonia.Media;
using Avalonia;
using Joktan.Mqtt.ViewModels;
using Joktan.Mqtt;
using Xunit;

namespace Joktan.Mqtt.Tests.ViewModels;

public class ArrayRuleViewModelTests
{
    [Fact]
    public void Shows_a_fixed_size_without_a_maximum()
    {
        var model = new ArrayRuleViewModel(new ArrayRule("a", 3, 3));

        Assert.Equal(("a", "3", ""), (model.Path, model.Items, model.MaxItems));
    }

    [Fact]
    public void Shows_a_range_of_sizes()
    {
        var model = new ArrayRuleViewModel(new ArrayRule("a", 1, 5));

        Assert.Equal(("1", "5"), (model.Items, model.MaxItems));
    }

    [Fact]
    public void Makes_a_rule_from_what_was_typed()
    {
        var fixedSize = new ArrayRuleViewModel { Path = " a ", Items = " 2 " }.ToRule();
        var range = new ArrayRuleViewModel { Path = "a", Items = "2", MaxItems = "4" }.ToRule();

        Assert.Equal(new ArrayRule("a", 2, 2), fixedSize);
        Assert.Equal(new ArrayRule("a", 2, 4), range);
    }

    [Theory]
    [InlineData("", "1", "", "Every array size needs an array path")]
    [InlineData("a", "x", "", "Array a: the number of items must be a whole number")]
    [InlineData("a", "1", "x", "Array a: the largest number of items must be a whole number")]
    [InlineData("a", "-1", "", "Array a: the number of items must not be negative")]
    [InlineData("a", "3", "2", "Array a: the number must not be greater than the largest number")]
    public void Says_what_is_wrong(string path, string items, string maxItems, string message)
    {
        var model = new ArrayRuleViewModel { Path = path, Items = items, MaxItems = maxItems };

        Assert.Equal(message, Assert.Throws<FormatException>(model.ToRule).Message);
    }
}
