using System;
using System.Linq;
using System.Text;
using Xunit;

namespace Joktan.Mqtt.Tests;

public class NumberOffersTests
{
    private const string Climate = """
        { "zone": "zone1", "readings": [
            { "ts": "2026-10-31T11:33:48.022Z", "sensor": "temperature", "value": 20.8, "unit": "C" },
            { "ts": "2026-10-31T11:33:48.022Z", "sensor": "humidity", "value": 40, "unit": "%" } ] }
        """;

    private static ReceivedMessage Message(string payload, string topic = "a/b", bool gzip = false)
    {
        var bytes = Encoding.UTF8.GetBytes(payload);
        return new ReceivedMessage(topic, gzip ? Gzip.Compress(bytes) : bytes, 0, false, DateTime.Now);
    }

    private static string[] Paths(ReceivedMessage message, TopicPattern? pattern = null)
        => NumberOffers.Find(message, pattern).Select(o => o.Path).ToArray();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_array_with_a_key_is_offered_by_it_and_not_by_position(bool gzip)
    {
        var paths = Paths(Message(Climate, gzip: gzip));

        Assert.Equal(["readings[sensor=*].value", "readings[sensor=temperature].value", "readings[sensor=humidity].value"], paths);
    }

    [Fact]
    public void The_key_is_not_the_time_or_the_unit()
    {
        using var document = System.Text.Json.JsonDocument.Parse(Climate);

        Assert.Equal("sensor", NumberOffers.KeyOfItems(document.RootElement.GetProperty("readings")));
    }

    [Theory]
    [InlineData("""[ { "v": 1 }, { "v": 2 } ]""")]
    [InlineData("""[ { "s": "a" }, { "s": "a" } ]""")]
    [InlineData("""[ { "s": "a" }, { "t": "b" } ]""")]
    [InlineData("""[ 1, 2 ]""")]
    [InlineData("""[ ]""")]
    public void Items_with_nothing_to_tell_them_apart_have_no_key(string json)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);

        Assert.Null(NumberOffers.KeyOfItems(document.RootElement));
    }

    [Fact]
    public void A_whole_number_is_a_key_only_with_a_name_like_one()
    {
        using var channel = System.Text.Json.JsonDocument.Parse("""[ { "channel": 1, "v": 1 }, { "channel": 2, "v": 2 } ]""");
        using var count = System.Text.Json.JsonDocument.Parse("""[ { "count": 1, "v": 1 }, { "count": 2, "v": 2 } ]""");

        Assert.Equal("channel", NumberOffers.KeyOfItems(channel.RootElement));
        Assert.Null(NumberOffers.KeyOfItems(count.RootElement));
    }

    [Fact]
    public void An_array_with_no_key_is_offered_as_before()
    {
        var paths = Paths(Message("""{ "readings": [ { "seq": 0, "value": 1 }, { "seq": 1, "value": 2 } ] }"""));

        Assert.Contains("readings[*].value", paths);
        Assert.Contains("readings[0].value", paths);
        Assert.Contains("readings[1].value", paths);
    }

    [Fact]
    public void A_keyed_item_says_which_position_it_can_replace()
    {
        var offer = NumberOffers.Find(Message(Climate)).Single(o => o.Path == "readings[sensor=humidity].value");

        Assert.Equal("readings[1].value", offer.ByPosition);
    }

    [Fact]
    public void Times_and_counters_come_last_and_are_marked()
    {
        var offers = NumberOffers.Find(Message("""{ "ts": 1790000000000, "sequence": 4, "rms": 1.2, "big": 2000000000.5 }"""));

        Assert.Equal(["rms", "big", "ts", "sequence"], offers.Select(o => o.Path));
        Assert.False(offers[0].LooksLikeTime);
        Assert.True(offers[2].LooksLikeTime);
        Assert.True(offers[3].LooksLikeTime);
    }

    [Fact]
    public void Says_what_units_are_beside_a_number()
    {
        var offers = NumberOffers.Find(Message(Climate));

        Assert.Equal(["C", "%"], offers[0].Units);
        Assert.Equal(["C"], offers[1].Units);
    }

    [Fact]
    public void A_bare_number_is_the_whole_payload()
    {
        var offer = Assert.Single(NumberOffers.Find(Message("21.5")));

        Assert.Equal("", offer.Path);
        Assert.Equal(21.5, offer.Latest);
    }

    [Fact]
    public void Text_offers_nothing()
        => Assert.Empty(NumberOffers.Find(Message("not json")));

    [Fact]
    public void A_level_that_matches_a_key_is_offered_as_a_link()
    {
        var paths = Paths(Message(Climate, "building/hvac/temperature/climate"), TopicPattern.Parse("building/hvac/{sensor}/climate"));

        Assert.Contains("readings[sensor={sensor}].value", paths);
    }

    [Fact]
    public void An_unnamed_level_is_linked_by_its_index()
    {
        var paths = Paths(Message(Climate, "building/hvac/humidity/climate"), TopicPattern.Parse("building/hvac/+/climate"));

        Assert.Contains("readings[sensor={2}].value", paths);
    }

    [Fact]
    public void A_level_that_matches_a_field_is_offered_as_a_link()
    {
        var paths = Paths(Message("""{ "ch1": { "value": 7 }, "ch2": { "value": 8 } }""", "line/ch1"), TopicPattern.Parse("line/{channel}"));

        Assert.Contains("{channel}.value", paths);
    }

    [Fact]
    public void A_level_that_matches_nothing_offers_no_link()
    {
        var paths = Paths(Message(Climate, "building/hvac/zone9/climate"), TopicPattern.Parse("building/hvac/{zone}/climate"));

        Assert.DoesNotContain(paths, p => p.Contains('{'));
    }

    [Fact]
    public void Text_fields_outside_arrays_could_be_keys()
    {
        var fields = NumberOffers.Fields(Message("""{ "ts": "2026-10-31T11:33:48Z", "machine": "press-07", "axis": "x", "unit": "mm", "rms": 1, "m": { "site": "north" }, "r": [ { "s": "a" } ] }"""));

        Assert.Equal(["machine", "axis", "m.site"], fields.Select(f => f.Path));
        Assert.Equal("x", fields[1].Value);
    }

    [Fact]
    public void A_path_offered_can_be_read_back()
    {
        var message = Message(Climate);

        foreach (var offer in NumberOffers.Find(message))
            Assert.NotEmpty(PayloadPath.Parse(offer.Path).Read(message.Json!.Value, _ => null));
    }
}
