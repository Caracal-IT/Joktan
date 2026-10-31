using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System;
using Joktan.Core.Controls;
using Joktan.Core.ViewModels;
using Joktan.Core;
using Joktan.Tests.TestSupport;
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
