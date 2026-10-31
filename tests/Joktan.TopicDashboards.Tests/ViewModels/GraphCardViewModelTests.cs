using System;
using System.Collections.Generic;
using System.Linq;
using Joktan.TopicDashboards.ViewModels;
using Xunit;

namespace Joktan.TopicDashboards.Tests.ViewModels;

public class GraphCardViewModelTests
{
    private static GraphProfile Profile(GraphKind kind = GraphKind.Line, params GraphValue[] values) => new()
    {
        Name = "g",
        Kind = kind,
        Width = 2,
        Height = 3,
        Values = values.Length > 0 ? [.. values] : [new GraphValue { Topic = "t", Path = "p" }],
    };

    private static GraphProfile WithKey(string keyName = "k", KeyShow show = KeyShow.Line) => new()
    {
        Name = "g",
        Values = [new GraphValue { Topic = "t/{k}", Path = "p" }],
        Keys = [new GraphKey { Name = keyName, Show = show }],
    };

    private static SeriesKeys Keys(params (string Key, string Value)[] pairs) => new(pairs.Select(p => new KeyValuePair<string, string>(p.Key, p.Value)));

    [Fact]
    public void Shows_what_the_profile_says()
    {
        var card = new GraphCardViewModel(Profile(), new GraphData());

        Assert.Equal("g", card.Name);
        Assert.Equal("t", card.Source);
        Assert.Equal(2, card.WidthSpan);
        Assert.Equal(3, card.HeightSpan);
        Assert.False(card.HasCell);
        Assert.Single(card.Series);
        Assert.False(card.HasReadings);
        Assert.False(card.HasStatus);
        Assert.False(card.HasLegend);
        Assert.True(card.HasHeadlineValue);
        Assert.Equal("–", card.HeadlineValue);
        Assert.False(card.HasHiddenLines);
        Assert.Equal("and 0 more", card.HiddenLinesText);
    }

    [Fact]
    public void A_graph_with_no_numbers_has_no_headline()
    {
        var profile = Profile();
        profile.Values.Clear();

        var card = new GraphCardViewModel(profile, new GraphData());

        Assert.Equal("", card.HeadlineValue);
        Assert.False(card.HasHeadlineValue);
    }

    [Theory]
    [InlineData(GraphKind.Line, true, true)]
    [InlineData(GraphKind.Area, true, true)]
    [InlineData(GraphKind.Bars, false, false)]
    [InlineData(GraphKind.Gauge, false, false)]
    public void Only_lines_and_areas_need_a_legend(GraphKind kind, bool legend, bool hasValues)
    {
        var card = new GraphCardViewModel(Profile(kind, new GraphValue { Topic = "t", Path = "a" }, new GraphValue { Topic = "t", Path = "b" }), new GraphData());

        Assert.Equal(legend, card.HasLegend);
        Assert.Equal(hasValues, card.Series.Count == 2 && legend);
    }

    [Theory]
    [InlineData(-1, "", 1.5, "1.5")]
    [InlineData(0, "", 2, "2")]
    [InlineData(-1, "%", 50, "50%")]
    [InlineData(-1, "kg", 3, "3 kg")]
    public void Values_are_formatted_with_their_unit(int decimals, string unit, double value, string expected)
    {
        var profile = Profile();
        profile.Decimals = decimals;
        profile.Unit = unit;

        Assert.Equal(expected.Replace(".", System.Globalization.CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator), new GraphCardViewModel(profile, new GraphData()).Format(value));
    }

    [Theory]
    [InlineData(2e9, "2G")]
    [InlineData(3e6, "3M")]
    [InlineData(4e3, "4K")]
    [InlineData(5, "5")]
    public void The_scale_is_shortened(double value, string expected)
    {
        Assert.Equal(expected, new GraphCardViewModel(Profile(), new GraphData()).FormatScale(value));
    }

    [Fact]
    public void New_readings_show_when_refreshed_and_time_moves_it_on()
    {
        var data = new GraphData();
        var card = new GraphCardViewModel(Profile(), data);
        var revision = card.Revision;

        data.Series("t", "p").Add(DateTime.Now, [7]);
        card.Refresh();

        Assert.True(card.HasReadings);
        Assert.Equal("7", card.HeadlineValue);
        Assert.Equal(7, card.Series[0].Latest);
        Assert.Equal("7", card.Series[0].LatestText);
        Assert.True(card.Revision > revision);

        var now = card.Revision;
        card.Tick(DateTime.Now, visible: false);
        Assert.Equal(now, card.Revision);
        card.Tick(DateTime.Now, visible: true);
        Assert.Equal(now + 1, card.Revision);
    }

    [Fact]
    public void Details_say_how_it_is_doing_and_keep_up()
    {
        var card = new GraphCardViewModel(Profile(), new GraphData());
        card.ToggleDetailsCommand.Execute(null);
        Assert.Equal("\nSeries: 1", card.DetailsText);

        card.DescribeHealth = () => "Fine";
        card.Tick(DateTime.Now, visible: false);
        Assert.Equal("Fine\nSeries: 1", card.DetailsText);

        card.ToggleDetailsCommand.Execute(null);
        Assert.False(card.IsShowingDetails);
        card.DescribeHealth = () => "Not read";
        card.Tick(DateTime.Now, visible: false);
        Assert.Equal("Fine\nSeries: 1", card.DetailsText);
    }

    [Fact]
    public void Every_command_calls_what_was_set_if_anything()
    {
        var card = new GraphCardViewModel(Profile(), new GraphData());
        card.EditCommand.Execute(null);
        card.RemoveCommand.Execute(null);
        card.ForgetCommand.Execute(null);
        card.MoveEarlierCommand.Execute(null);
        card.MoveLaterCommand.Execute(null);
        card.AskRemoveCommand.Execute(null);
        Assert.True(card.IsConfirmingRemove);
        card.KeepGraphCommand.Execute(null);
        Assert.False(card.IsConfirmingRemove);

        var called = new List<string>();
        card.EditRequested = _ => called.Add("edit");
        card.RemoveRequested = _ => called.Add("remove");
        card.ForgetRequested = _ => called.Add("forget");
        card.MoveRequested = (_, by) => called.Add($"move {by}");
        card.RemoveAsked = _ => called.Add("asked");
        card.SpanChanged = _ => called.Add("span");

        card.EditCommand.Execute(null);
        card.RemoveCommand.Execute(null);
        card.ForgetCommand.Execute(null);
        card.MoveEarlierCommand.Execute(null);
        card.MoveLaterCommand.Execute(null);
        card.AskRemoveCommand.Execute(null);
        card.WidthSpan = 4;
        card.HeightSpan = 5;

        Assert.Equal(["edit", "remove", "forget", "move -1", "move 1", "asked", "span", "span"], called);
        Assert.Equal(4, card.Profile.Width);
        Assert.Equal(5, card.Profile.Height);
    }

    [Fact]
    public void A_line_for_each_value_of_a_key_with_steady_colours()
    {
        var data = new GraphData();
        var profile = WithKey();
        var card = new GraphCardViewModel(profile, data);
        Assert.Empty(card.Series);

        data.Series("t/{k}", "p", Keys(("k", "a"))).Add(DateTime.Now, [1]);
        data.Series("t/{k}", "p", Keys(("k", "b"))).Add(DateTime.Now, [2]);
        card.Refresh();

        Assert.Equal(["a", "b"], card.Series.Select(s => s.DisplayLabel));
        Assert.True(card.HasLegend);
        Assert.Equal(2, profile.SeriesColors.Count);
        Assert.Equal(2, profile.SeriesColors.Values.Distinct().Count());
        var slots = card.Series.Select(s => s.Slot).ToList();

        card.Refresh();
        Assert.Equal(slots, card.Series.Select(s => s.Slot));
    }

    [Fact]
    public void Lines_can_be_taken_off_and_brought_back()
    {
        var data = new GraphData();
        var card = new GraphCardViewModel(WithKey(), data);
        data.Series("t/{k}", "p", Keys(("k", "a"))).Add(DateTime.Now, [1]);
        data.Series("t/{k}", "p", Keys(("k", "b"))).Add(DateTime.Now, [2]);
        card.Refresh();
        var lines = card.LinesFromKeys();
        Assert.Equal(2, lines.Count);
        Assert.All(lines, l => Assert.False(l.IsRemoved));

        card.SetRemoved(lines[0].Line.Id, true);

        Assert.Equal(["b"], card.Series.Select(s => s.DisplayLabel));
        Assert.True(card.LinesFromKeys()[0].IsRemoved);

        card.SetRemoved(lines[0].Line.Id, false);
        Assert.Equal(2, card.Series.Count);
    }

    [Fact]
    public void More_lines_than_there_is_room_for_leave_the_oldest_out()
    {
        var data = new GraphData();
        var card = new GraphCardViewModel(WithKey(), data);
        var now = DateTime.Now;
        foreach (var i in Enumerable.Range(0, GraphProfile.MaxValues + 2))
            data.Series("t/{k}", "p", Keys(("k", $"s{i:00}"))).Add(now.AddSeconds(i), [i]);
        data.Series("t/{k}", "p", Keys(("k", "empty")));

        card.Refresh();

        Assert.Equal(GraphProfile.MaxValues, card.Series.Count);
        Assert.Equal(3, card.HiddenLines);
        Assert.True(card.HasHiddenLines);
        Assert.Equal("and 3 more", card.HiddenLinesText);
        Assert.DoesNotContain(card.Series, s => s.DisplayLabel == "s00");
        card.ToggleDetailsCommand.Execute(null);
        Assert.Contains($"Series: {GraphProfile.MaxValues} of {GraphProfile.MaxValues + 3} shown", card.DetailsText);
    }

    [Fact]
    public void Colours_go_round_when_they_run_out()
    {
        var data = new GraphData();
        var profile = WithKey();
        foreach (var i in Enumerable.Range(0, GraphColors.Count))
            profile.SeriesColors[$"taken{i}"] = i;
        var card = new GraphCardViewModel(profile, data);
        data.Series("t/{k}", "p", Keys(("k", "new"))).Add(DateTime.Now, [1]);

        card.Refresh();

        Assert.Equal(GraphColors.Count % GraphColors.Count, card.Series[0].Slot);
    }

    [Fact]
    public void A_graph_of_one_cell_shows_only_its_own_and_names_it()
    {
        var data = new GraphData();
        var profile = new GraphProfile
        {
            Name = "g",
            Values = [new GraphValue { Topic = "t/{k}/{l}", Path = "p" }],
            Keys = [new GraphKey { Name = "k", Show = KeyShow.Graph }, new GraphKey { Name = "l" }],
        };
        data.Series("t/{k}/{l}", "p", Keys(("k", "a"), ("l", "x"))).Add(DateTime.Now, [1]);
        data.Series("t/{k}/{l}", "p", Keys(("k", "b"), ("l", "y"))).Add(DateTime.Now, [2]);

        var card = new GraphCardViewModel(profile, data, Keys(("k", "a")));

        Assert.Equal("g: a", card.Name);
        Assert.Equal(["x"], card.Series.Select(s => s.DisplayLabel));

        profile.Name = "{k} room";
        Assert.Equal("a room", new GraphCardViewModel(profile, data, Keys(("k", "a"))).Name);
    }

    [Fact]
    public void Several_topics_are_named_short_and_labels_win()
    {
        var card = new GraphCardViewModel(Profile(GraphKind.Line,
            new GraphValue { Topic = "home/a", Path = "p" },
            new GraphValue { Topic = "home/b", Path = "" },
            new GraphValue { Topic = "home/c", Path = "q", Label = "  Chosen  " }), new GraphData());

        Assert.Equal(3, card.Series.Count);
        Assert.Contains("p", card.Series[0].DisplayLabel);
        Assert.Equal("Chosen", card.Series[2].DisplayLabel);
        Assert.Equal("home/a p", card.Series[0].Source);
        Assert.Equal("home/b", card.Series[1].Source);
        Assert.Equal("p", card.Series[0].Path);
        Assert.Equal("Value", new GraphCardViewModel(Profile(GraphKind.Line, new GraphValue { Topic = "t" }), new GraphData()).Series[0].DisplayLabel);
    }

    [Fact]
    public void A_line_says_its_path_when_other_numbers_have_keys_too()
    {
        var data = new GraphData();
        var profile = new GraphProfile
        {
            Name = "g",
            Values = [new GraphValue { Topic = "t/{k}", Path = "p" }, new GraphValue { Topic = "t/{k}", Path = "q", Label = "L {k}" }],
            Keys = [new GraphKey { Name = "k" }],
        };
        data.Series("t/{k}", "p", Keys(("k", "a"))).Add(DateTime.Now, [1]);
        data.Series("t/{k}", "q", Keys(("k", "a"))).Add(DateTime.Now, [1]);

        var card = new GraphCardViewModel(profile, data);

        Assert.Equal(["p a", "L a"], card.Series.Select(s => s.DisplayLabel));
    }

    [Fact]
    public void Applying_changes_what_it_draws()
    {
        var card = new GraphCardViewModel(Profile(), new GraphData());
        var profile = Profile(GraphKind.Gauge);
        profile.Name = "other";

        card.Apply(profile, new GraphData(), Keys(("k", "v")));

        Assert.Equal("other", card.Name);
        Assert.True(card.HasCell);
        Assert.False(card.HasHeadlineValue);
    }
}

public class GraphEditorRowsTests
{
    private static readonly Action Nothing = () => { };

    [Theory]
    [InlineData("", "The whole payload", "value")]
    [InlineData("a", "a", "value")]
    [InlineData("a[0]", "a[0]", "item")]
    [InlineData("a[*]", "a[*]", "array")]
    [InlineData("a[k=*].v", "a[k=*].v", "each")]
    [InlineData("a[{k}].v", "a[{k}].v", "linked")]
    public void A_number_says_what_kind_it_is(string path, string display, string kind)
    {
        var row = new GraphValueChoiceViewModel("t", path, 0, _ => { }, Nothing);

        Assert.Equal(display, row.DisplayPath);
        Assert.Equal(kind, row.Kind);
        Assert.Equal("t", row.Topic);
        Assert.Equal(path, row.Path);
    }

    [Fact]
    public void Ticking_tells_the_editor_but_showing_does_not()
    {
        var toggled = 0;
        var changed = 0;
        var row = new GraphValueChoiceViewModel("t", "p", 0, _ => toggled++, () => changed++);

        row.Show(true);
        Assert.Equal(0, toggled);
        Assert.True(row.IsChosen);

        row.IsChosen = false;
        Assert.Equal(1, toggled);

        row.Label = "l";
        row.Color = 3;
        Assert.Equal(2, changed);
        row.LatestText = "1";
        row.LooksLikeTime = true;
        row.Units = ["kg"];
        Assert.True(row.LooksLikeTime);
        Assert.Equal(["kg"], row.Units);
    }

    [Fact]
    public void A_better_path_is_offered_only_while_ticked()
    {
        var used = 0;
        var removed = 0;
        var row = new GraphValueChoiceViewModel("t", "a[0]", 0, _ => { }, Nothing, _ => used++, _ => removed++) { BetterPath = "a[k=x]" };
        Assert.False(row.HasBetterPath);

        row.Show(true);
        Assert.True(row.HasBetterPath);
        Assert.Contains("a[k=x]", row.BetterPathText);

        row.UseBetterPathCommand.Execute(null);
        row.RemoveCommand.Execute(null);
        Assert.Equal(1, used);
        Assert.Equal(1, removed);

        var plain = new GraphValueChoiceViewModel("t", "a", 0, _ => { }, Nothing);
        plain.UseBetterPathCommand.Execute(null);
        plain.RemoveCommand.Execute(null);
    }

    [Fact]
    public void A_key_says_what_was_seen_and_tells_of_changes()
    {
        var changed = 0;
        GraphKeyViewModel? removed = null;
        var key = new GraphKeyViewModel(new GraphKey { Name = "k", Field = "f", Show = KeyShow.Only, Only = "x" }, () => changed++, k => removed = k);
        Assert.Equal(0, changed);
        Assert.True(key.IsOnly);
        Assert.True(key.CanRemove);
        Assert.Equal("x", key.OnlyValue);
        Assert.Equal("Nothing yet", key.SeenText);

        key.ShowValues(["a", "b"]);
        Assert.Equal("a, b", key.SeenText);
        key.ShowValues(["a", "b"]);
        key.ShowValues(["1", "2", "3", "4", "5"]);
        Assert.Equal("1, 2, 3 and 2 more", key.SeenText);
        Assert.Equal(5, key.Values.Count);
        key.ShowValues([]);
        Assert.Equal("Nothing yet", key.SeenText);

        key.SelectedShow = GraphKeyViewModel.Choices[0];
        key.OnlyValue = "y";
        Assert.Equal(2, changed);
        Assert.False(key.IsOnly);
        var saved = key.ToKey();
        Assert.Equal(KeyShow.Line, saved.Show);
        Assert.Equal("y", saved.Only);
        Assert.Equal("f", saved.Field);

        key.RemoveCommand.Execute(null);
        Assert.Same(key, removed);
        Assert.False(new GraphKeyViewModel(new GraphKey { Name = "k" }, Nothing, _ => { }).CanRemove);
    }

    [Fact]
    public void A_topics_levels_can_be_made_keys_unless_they_are()
    {
        var made = new List<int>();
        var entry = new TopicEntryViewModel("home/+/{room}/#/x", (_, i) => made.Add(i));

        Assert.Equal("home/+/{room}/#/x", entry.Text);
        Assert.Equal([false, true, true, true, false], entry.Levels.Select(l => l.IsKey));

        foreach (var level in entry.Levels)
            entry.MakeKeyCommand.Execute(level);

        Assert.Equal([0, 4], made);
    }

    [Fact]
    public void A_suggestion_adds_its_field()
    {
        string? added = null;

        var suggestion = new KeySuggestion("text", "field", f => added = f);
        suggestion.AddCommand.Execute(null);

        Assert.Equal("text", suggestion.Text);
        Assert.Equal("field", added);
    }

    [Fact]
    public void A_line_can_be_taken_off_or_brought_back()
    {
        var calls = new List<(string, bool)>();
        var line = new GraphLineViewModel("id", "label", 2, false, true, (id, removed) => calls.Add((id, removed)));

        Assert.Equal("id", line.Id);
        Assert.Equal("label", line.Label);
        Assert.Equal(2, line.Slot);
        Assert.False(line.IsRemoved);
        Assert.True(line.CanRemove);
        Assert.False(new GraphLineViewModel("id", "l", 0, true, true, (_, _) => { }).CanRemove);
        Assert.False(new GraphLineViewModel("id", "l", 0, false, false, (_, _) => { }).CanRemove);

        line.RemoveCommand.Execute(null);
        line.BringBackCommand.Execute(null);

        Assert.Equal([("id", true), ("id", false)], calls);
    }
}
