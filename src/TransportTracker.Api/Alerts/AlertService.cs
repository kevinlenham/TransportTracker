using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TransitRealtime;
using TransportTracker.Api.Data;
using TransportTracker.Api.Tfnsw;

namespace TransportTracker.Api.Alerts;

public interface IAlertsFeed
{
    Task<FeedMessage> FetchAsync(CancellationToken ct);
}

public class TfnswAlertsFeed(IHttpClientFactory httpFactory, IOptions<TfnswOptions> options) : IAlertsFeed
{
    public async Task<FeedMessage> FetchAsync(CancellationToken ct)
    {
        var http = httpFactory.CreateClient(TfnswOptions.RealtimeHttpClientName);
        var bytes = await http.GetByteArrayAsync(options.Value.RealtimeAlertsPath, ct);
        return FeedMessage.Parser.ParseFrom(bytes);
    }
}

/// <summary>The alerts as of the last successful fetch. Empty until the first.</summary>
public class AlertStore
{
    private IReadOnlyList<ServiceAlert> _alerts = [];

    public IReadOnlyList<ServiceAlert> Alerts
    {
        get => Volatile.Read(ref _alerts);
        set => Volatile.Write(ref _alerts, value);
    }

    public DateTimeOffset? FetchedAt { get; set; }
}

/// <summary>
/// Fetches the Sydney Trains alerts feed every 2 minutes. Alerts change far less often than delays.
/// A failed fetch keeps the previous alerts.
/// </summary>
public class AlertService(IServiceScopeFactory scopes, IAlertsFeed feed, AlertStore store, TimeProvider time,
    ILogger<AlertService> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, time);
        do
        {
            try
            {
                var message = await feed.FetchAsync(stoppingToken);
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var (lineByRoute, stationByStop) = await ReferencedAsync(db, message, stoppingToken);
                store.Alerts = ServiceAlerts.From(message, lineByRoute, stationByStop);
                store.FetchedAt = time.GetUtcNow();
            }
            // An HttpClient timeout is also an OperationCanceledException, so check the token, not the type.
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Alerts fetch failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Lines and Stations for the route and stop IDs the alerts mention, from the active Timetable.</summary>
    private static async Task<(Dictionary<string, string>, Dictionary<string, string>)> ReferencedAsync(
        AppDbContext db, FeedMessage message, CancellationToken ct)
    {
        var entities = message.Entity.Where(e => e.Alert is not null).SelectMany(e => e.Alert.InformedEntity).ToList();
        var routeIds = entities.Select(e => e.HasRouteId ? e.RouteId : e.Trip?.RouteId).OfType<string>().Distinct().ToList();
        var stopIds = entities.Where(e => e.HasStopId).Select(e => e.StopId).Distinct().ToList();

        var importId = await db.TimetableImports.Where(i => i.IsActive).Select(i => (int?)i.Id).SingleOrDefaultAsync(ct);
        if (importId is null) return ([], []);

        var lineByRoute = await db.Routes.AsNoTracking()
            .Where(r => r.ImportId == importId && routeIds.Contains(r.RouteId) && r.ShortName != null)
            .ToDictionaryAsync(r => r.RouteId, r => r.ShortName!, ct);
        var stationByStop = await db.Stops.AsNoTracking()
            .Where(s => s.ImportId == importId && stopIds.Contains(s.StopId))
            .ToDictionaryAsync(s => s.StopId, s => s.ParentStation ?? s.StopId, ct);
        return (lineByRoute, stationByStop);
    }
}
