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

namespace Joktan.Core.Tests;

public class JsonFormatsTests
{
    [Fact]
    public void Indented_is_indented_and_keeps_text_readable()
    {
        var json = JsonSerializer.Serialize(new { Name = "Grüße <&>" }, JsonFormats.Indented);

        Assert.Contains("\n", json);
        Assert.Contains("Grüße <&>", json);
    }

    [Fact]
    public void Compact_is_on_one_line_and_keeps_text_readable()
    {
        var json = JsonSerializer.Serialize(new { Name = "Grüße" }, JsonFormats.Compact);

        Assert.Equal("{\"Name\":\"Grüße\"}", json);
    }
}
