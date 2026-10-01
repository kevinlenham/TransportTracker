namespace TransportTracker.Api.Timetable;

/// <summary>
/// Runs the Timetable import on startup and then nightly at 03:00 Sydney time. TfNSW publishes the
/// new static GTFS at about 01:00. A failed import is retried after 30 minutes, and the previous
/// Timetable stays active in the meantime.
/// </summary>
public class TimetableImportService(IServiceScopeFactory scopes, TimeProvider time, ILogger<TimetableImportService> logger)
    : BackgroundService
{
    private static readonly TimeZoneInfo Sydney = TimeZoneInfo.FindSystemTimeZoneById("Australia/Sydney");
    private static readonly TimeOnly RunAt = new(3, 0);
    private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan wait;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<TimetableImporter>().ImportAsync(stoppingToken);
                wait = DelayUntilNextRun(time.GetUtcNow());
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Timetable import failed, retrying in {RetryAfter}", RetryAfter);
                wait = RetryAfter;
            }

            await Task.Delay(wait, time, stoppingToken);
        }
    }

    public static TimeSpan DelayUntilNextRun(DateTimeOffset nowUtc)
    {
        var local = TimeZoneInfo.ConvertTime(nowUtc, Sydney);
        var next = local.Date + RunAt.ToTimeSpan();
        if (next <= local.DateTime) next = next.AddDays(1);
        var nextUtc = TimeZoneInfo.ConvertTimeToUtc(next, Sydney);
        return nextUtc - nowUtc.UtcDateTime;
    }
}
