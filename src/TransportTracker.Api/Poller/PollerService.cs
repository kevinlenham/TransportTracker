namespace TransportTracker.Api.Poller;

/// <summary>What the poller last saw, for the health check and the departure board.</summary>
public class PollerStatus
{
    /// <summary>Six missed polls. Any longer and the gap starts to cost Observations.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(3);

    private long _lastPollTicks;

    public bool IsHealthy(DateTimeOffset now) => LastPollAt is { } last && now - last <= StaleAfter;

    private LiveFeed? _live;

    /// <summary>The feed as of the latest poll, for the departure board. Null until the first poll.</summary>
    public LiveFeed? Live
    {
        get => Volatile.Read(ref _live);
        set => Volatile.Write(ref _live, value);
    }

    public DateTimeOffset? LastPollAt
    {
        get => Interlocked.Read(ref _lastPollTicks) is var t and > 0 ? new DateTimeOffset(t, TimeSpan.Zero) : null;
        set => Interlocked.Exchange(ref _lastPollTicks, value?.UtcTicks ?? 0);
    }
}

/// <summary>
/// Polls the Sydney Trains realtime feed every 30s and records Observations. A failed poll is
/// logged and skipped: the tracker keeps its state, so the next poll picks up where it left off.
/// </summary>
public class PollerService(IServiceScopeFactory scopes, IRealtimeFeed feed, PollerStatus status, TimeProvider time,
    ILogger<PollerService> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private readonly DelayTracker _tracker = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, time);
        do
        {
            try
            {
                var changes = _tracker.Update(await feed.FetchAsync(stoppingToken));
                status.Live = _tracker.Live;
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<ObservationRecorder>().RecordAsync(changes, stoppingToken);
                status.LastPollAt = time.GetUtcNow();
            }
            // An HttpClient timeout is also an OperationCanceledException, so check the token, not the type.
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Realtime poll failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
