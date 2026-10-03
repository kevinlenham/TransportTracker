using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TransitRealtime;
using TransportTracker.Api.Poller;

namespace TransportTracker.Tests.Poller;

public class PollerServiceTests
{
    [Fact]
    public async Task HttpTimeoutDoesNotStopThePoller()
    {
        // HttpClient reports a timeout as a TaskCanceledException. In Azure this escaped and shut the host down.
        var feed = new TimingOutFeed();
        var poller = new PollerService(new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            feed, new PollerStatus(), TimeProvider.System, NullLogger<PollerService>.Instance);

        await poller.StartAsync(CancellationToken.None);
        await feed.Called.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Task.Delay(100);

        Assert.False(poller.ExecuteTask!.IsCompleted);
        await poller.StopAsync(CancellationToken.None);
    }

    private class TimingOutFeed : IRealtimeFeed
    {
        public TaskCompletionSource Called { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<FeedMessage> FetchAsync(CancellationToken ct)
        {
            Called.TrySetResult();
            throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 20 seconds elapsing.",
                new TimeoutException());
        }
    }
}
