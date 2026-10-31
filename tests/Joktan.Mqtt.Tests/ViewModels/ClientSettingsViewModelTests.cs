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

public class ClientSettingsViewModelTests
{
    [Theory]
    [InlineData("broker", true)]
    [InlineData(" broker.example.com ", true)]
    [InlineData("127.0.0.1", true)]
    [InlineData("", false)]
    [InlineData("  ", false)]
    [InlineData("two words", false)]
    public void Checks_a_host(string host, bool expected)
        => Assert.Equal(expected, ClientSettingsViewModel.IsValidHost(host));

    [Theory]
    [InlineData("1", 1)]
    [InlineData(" 1883 ", 1883)]
    [InlineData("65535", 65535)]
    [InlineData("0", null)]
    [InlineData("65536", null)]
    [InlineData("-1", null)]
    [InlineData("abc", null)]
    [InlineData("", null)]
    public void Reads_a_port(string port, int? expected)
        => Assert.Equal(expected, ClientSettingsViewModel.ParsePort(port));
}
