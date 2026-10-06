using Microsoft.EntityFrameworkCore;
using TransportTracker.Api.Data;
using TransportTracker.Api.Timetable;

namespace TransportTracker.Api.Poller;

/// <summary>
/// Deletes Observations older than about 5 weeks. The stats only use the last 3, and the extra 2 leave
/// room to recompute them if a definition changes.
/// </summary>
public class ObservationRetention(AppDbContext db, TimeProvider time)
{
    /// <summary>Must stay longer than LateStatsQuery.Window, or the stats would lose data.</summary>
    public static readonly TimeSpan KeepFor = TimeSpan.FromDays(35);

    /// <summary>Deletes whole service days older than KeepFor, and returns how many Observations went.</summary>
    public Task<int> PurgeAsync(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), GtfsTime.Sydney).DateTime);
        var cutoff = today.AddDays(-(int)KeepFor.TotalDays);
        // By service_date, which leads the unique index, so this doesn't scan the whole table.
        return db.Observations.Where(o => o.ServiceDate < cutoff).ExecuteDeleteAsync(ct);
    }
}

/// <summary>Runs the retention purge on startup and then daily.</summary>
public class ObservationRetentionService(IServiceScopeFactory scopes, TimeProvider time, ILogger<ObservationRetentionService> logger)
    : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, time);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var deleted = await scope.ServiceProvider.GetRequiredService<ObservationRetention>().PurgeAsync(stoppingToken);
                logger.LogInformation("Deleted {Count} Observations older than {Days} days", deleted, ObservationRetention.KeepFor.TotalDays);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Observation retention purge failed, retrying tomorrow");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
