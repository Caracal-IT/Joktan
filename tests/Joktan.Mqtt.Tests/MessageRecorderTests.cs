using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Joktan.Mqtt.Tests;

public class MessageRecorderTests : IAsyncLifetime
{
    private TestBroker _broker = null!;
    private readonly MqttConnection _connection = new();

    public async ValueTask InitializeAsync()
    {
        _broker = await TestBroker.StartAsync();
        await _connection.ConnectAsync(new BrokerEndpoint("127.0.0.1", _broker.Port, "rec-" + Guid.NewGuid().ToString("N")[..8], null, null));
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisconnectAsync();
        await _broker.DisposeAsync();
    }

    private static async Task Until(Func<bool> condition)
    {
        var start = Environment.TickCount64;
        while (!condition())
        {
            if (Environment.TickCount64 - start > 15000)
                throw new TimeoutException("The condition did not hold in time.");
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task Records_what_matches_its_filters_with_the_time_between()
    {
        var recorder = new MessageRecorder(_connection);
        Assert.Null(await recorder.StartAsync(["plant/#"]));

        await _broker.SendAsync("plant/a", "{\"v\":1}");
        await _broker.SendAsync("other", "x");
        await Task.Delay(150, TestContext.Current.CancellationToken);
        await _broker.SendAsync("plant/b", "{\"v\":2}");
        await Until(() => recorder.Count == 2);

        var recording = await recorder.StopAsync("id", "name", "client");

        Assert.Equal(["plant/a", "plant/b"], recording.Messages.Select(m => m.Topic));
        Assert.Equal("{\"v\":2}", Encoding.UTF8.GetString(recording.Messages[1].Payload));
        Assert.True(recording.Messages[1].At - recording.Messages[0].At >= TimeSpan.FromMilliseconds(100));
        Assert.True(recording.Length >= recording.Messages[^1].At);
        Assert.Equal("plant/#", recording.Filters);
        Assert.False(recorder.IsRecording);
    }

    [Fact]
    public async Task Nothing_is_recorded_after_it_stops()
    {
        var recorder = new MessageRecorder(_connection);
        await recorder.StartAsync(["#"]);
        var recording = await recorder.StopAsync("id", "name", "client");

        await _broker.SendAsync("late", "x");
        await Task.Delay(150, TestContext.Current.CancellationToken);

        Assert.Empty(recording.Messages);
        Assert.Equal(0, recorder.Count);
    }

    [Fact]
    public async Task An_invalid_filter_is_refused()
    {
        var recorder = new MessageRecorder(_connection);

        Assert.NotNull(await recorder.StartAsync(["a/#/b"]));
        Assert.False(recorder.IsRecording);
    }
}
