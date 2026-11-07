using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Joktan.Mqtt.Tests;

public class RecordingPlayerTests
{
    private static Recording Recording(params double[] seconds) => new()
    {
        Messages = seconds.Select((s, i) => new RecordedMessage(TimeSpan.FromSeconds(s), $"t/{i}", [], 0, false)).ToList(),
        Length = TimeSpan.FromSeconds(seconds.Max()),
    };

    [Fact]
    public async Task Sends_every_message_in_order()
    {
        var sent = new ConcurrentQueue<string>();
        var player = new RecordingPlayer(Recording(0, 0.05, 0.1), m =>
        {
            sent.Enqueue(m.Topic);
            return Task.FromResult(true);
        });

        await player.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["t/0", "t/1", "t/2"], sent);
        Assert.Equal(3, player.Sent);
        Assert.Equal(3, player.Done);
    }

    [Fact]
    public async Task Keeps_the_gaps_scaled_by_the_speed()
    {
        var player = new RecordingPlayer(Recording(0, 2), _ => Task.FromResult(true)) { Speed = 10 };
        var clock = Stopwatch.StartNew();

        await player.RunAsync(TestContext.Current.CancellationToken);

        // Two seconds at ten times the speed is about a fifth of a second.
        Assert.InRange(clock.Elapsed.TotalSeconds, 0.18, 1.5);
    }

    [Fact]
    public async Task A_change_of_speed_takes_effect_while_it_plays()
    {
        var player = new RecordingPlayer(Recording(0, 60), _ => Task.FromResult(true));
        var run = player.RunAsync(TestContext.Current.CancellationToken);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.Equal(1, player.Sent);

        player.Speed = 100;
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.Equal(1, player.Sent);
        player.Speed = 1000;

        // 1000 is more than the fastest, so it plays at 100×: the minute takes about 0.6 s.
        await run.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(2, player.Sent);
        Assert.Equal(100, player.Speed);
    }

    [Fact]
    public async Task Nothing_is_sent_while_paused()
    {
        var player = new RecordingPlayer(Recording(0, 0.1), _ => Task.FromResult(true)) { IsPaused = true };
        var run = player.RunAsync(TestContext.Current.CancellationToken);

        await Task.Delay(300, TestContext.Current.CancellationToken);
        Assert.Equal(0, player.Sent);
        Assert.Equal(TimeSpan.Zero, player.Position);

        player.IsPaused = false;
        await run.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(2, player.Sent);
    }

    [Fact]
    public async Task A_loop_starts_again_until_stopped()
    {
        using var stop = new CancellationTokenSource();
        var player = new RecordingPlayer(Recording(0, 0.05), _ => Task.FromResult(true)) { Loop = true, Speed = 5 };
        var run = player.RunAsync(stop.Token);

        while (player.Sent < 6)
            await Task.Delay(10, TestContext.Current.CancellationToken);
        await stop.CancelAsync();
        await run.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.True(player.Sent >= 6);
    }

    [Fact]
    public async Task Counts_what_failed()
    {
        var player = new RecordingPlayer(Recording(0, 0, 0), m => Task.FromResult(m.Topic != "t/1"));

        await player.RunAsync(TestContext.Current.CancellationToken);

        Assert.Equal((2, 1), (player.Sent, player.Failed));
    }
}
