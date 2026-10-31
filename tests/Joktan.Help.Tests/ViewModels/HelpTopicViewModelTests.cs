using System.Linq;
using System.Threading.Tasks;
using Joktan.Help.ViewModels;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Help.Tests.ViewModels;

public class HelpTopicViewModelTests
{
    private static HelpTopicViewModel Topic(string id) => new(HelpTopic.All.First(t => t.Id == id));

    [Fact]
    public Task A_child_topic_is_indented_except_among_search_results() => Headless.Run(() =>
    {
        var topic = Topic("themes");
        Assert.True(topic.IsIndented);
        Assert.False(topic.HasSnippet);

        topic.Snippet = "found";

        Assert.False(topic.IsIndented);
        Assert.True(topic.HasSnippet);
        Assert.False(Topic("notes").IsIndented);
    });

    [Fact]
    public Task Every_word_must_appear_in_the_title_or_the_text() => Headless.Run(() =>
    {
        var topic = Topic("notes");

        Assert.True(topic.MatchesTitle("NOTES"));
        Assert.False(topic.MatchesTitle("mqtt broker"));
        Assert.True(topic.Matches(["notes"]));
        Assert.True(topic.Matches(["NOTES", "markdown"]));
        Assert.False(topic.Matches(["notes", "zzzz-not-there"]));
    });

    [Fact]
    public Task A_snippet_names_the_parent_of_a_child_topic() => Headless.Run(() =>
    {
        Assert.StartsWith("Settings and data · ", Topic("themes").SnippetFor(["theme"]));
        Assert.DoesNotContain(" · ", Topic("notes").SnippetFor(["markdown"])[..15]);
    });

    [Fact]
    public Task A_snippet_is_cut_around_the_word_found() => Headless.Run(() =>
    {
        // Each topic's lines are searched for one found far into a long line.
        foreach (var topic in HelpTopic.All.Select(t => new HelpTopicViewModel(t)))
        {
            var words = topic.Markdown.Split(' ', '\n').Where(w => w.Length > 5 && w.All(char.IsLetter)).ToList();
            foreach (var word in words.Take(40))
            {
                var snippet = topic.SnippetFor([word]);
                Assert.True(snippet.Length <= 90 + 2 + (topic.Topic.Parent?.Length + 3 ?? 0), snippet);
            }
        }

        // Nothing found gives the start of a line rather than failing.
        Assert.NotEmpty(Topic("notes").SnippetFor(["zzzz-not-there"]));
    });
}
