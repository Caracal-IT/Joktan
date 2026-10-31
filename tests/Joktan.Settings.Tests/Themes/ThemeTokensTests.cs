using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Joktan.Settings.Themes;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Settings.Tests.Themes;

public class ThemeTokensTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly ThemeToken[] All = [.. ThemeTokens.Colors, .. ThemeTokens.Fonts, .. ThemeTokens.TextSizes];

    private static List<string> KeysIn(XElement parent) => parent.Elements()
        .Select(e => (string?)e.Attribute(Xaml + "Key"))
        .OfType<string>()
        .ToList();

    private static XElement Tokens() => XElement.Load(RepositoryFiles.At("src", "Joktan.Core", "Styles", "Tokens.axaml"));

    [Fact]
    public void Every_token_has_a_different_key_and_a_label_and_group()
    {
        Assert.Equal(All.Length, All.Select(t => t.Key).Distinct().Count());
        Assert.All(All, token =>
        {
            Assert.NotEmpty(token.Label);
            Assert.NotEmpty(token.Group);
        });
    }

    // BuiltInThemes throws at startup for a colour or text size that Tokens.axaml does not hold.
    [Fact]
    public void Every_token_is_a_resource_in_the_tokens_file()
    {
        var root = Tokens();
        var keys = root.Descendants().Select(e => (string?)e.Attribute(Xaml + "Key")).ToHashSet();

        Assert.All(All, token => Assert.Contains(token.Key, keys));
    }

    [Fact]
    public void Light_and_dark_hold_the_same_resources()
    {
        var dictionaries = Tokens().Descendants()
            .Where(e => (string?)e.Attribute(Xaml + "Key") is "Light" or "Dark")
            .ToDictionary(e => (string)e.Attribute(Xaml + "Key")!);

        Assert.Equal(KeysIn(dictionaries["Light"]).Order(), KeysIn(dictionaries["Dark"]).Order());
    }
}
