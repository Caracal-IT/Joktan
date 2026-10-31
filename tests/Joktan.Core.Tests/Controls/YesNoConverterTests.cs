using System;
using System.Globalization;
using Joktan.Core.Controls;
using Xunit;

namespace Joktan.Core.Tests.Controls;

public class YesNoConverterTests
{
    [Theory]
    [InlineData(true, "Yes")]
    [InlineData(false, "No")]
    [InlineData(null, "No")]
    [InlineData("true", "No")]
    public void Shows_a_boolean_as_a_word(object? value, string expected)
    {
        Assert.Equal(expected, YesNoConverter.Instance.Convert(value, typeof(string), null, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Does_not_convert_back()
    {
        Assert.Throws<NotSupportedException>(
            () => YesNoConverter.Instance.ConvertBack("Yes", typeof(bool), null, CultureInfo.InvariantCulture));
    }
}
