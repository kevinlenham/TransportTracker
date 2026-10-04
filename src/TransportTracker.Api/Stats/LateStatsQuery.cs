using Microsoft.EntityFrameworkCore;
using TransportTracker.Api.Data;

namespace TransportTracker.Api.Stats;

/// <summary>The Stats Bucket a Late % is calculated over. Line + Direction is how a Direction is identified.</summary>
public record StatsBucket(string? Line, short? DirectionId, TimeBand TimeBand);

/// <summary>Observation counts for one Stats Bucket over the stats window.</summary>
public record BucketStats(int Observed, int Late, int Timetabled, int Cancelled)
{
    public static readonly BucketStats Empty = new(0, 0, 0, 0);

    /// <summary>Null below the Minimum Sample, when the UI shows "collecting data".</summary>
    public double? LatePercent => Observed >= LateStatsQuery.MinimumSample ? Percent(Late, Observed) : null;

    public double? CancellationPercent => Timetabled >= LateStatsQuery.MinimumSample ? Percent(Cancelled, Timetabled) : null;

    private static double Percent(int part, int whole) => Math.Round(100.0 * part / whole, 1);
}

/// <summary>Late % and Cancellation % for each Stats Bucket at a Station, over the last 3 weeks of Observations.</summary>
public class LateStatsQuery(AppDbContext db, TimeProvider time)
{
    public static readonly TimeSpan Window = TimeSpan.FromDays(21);
    public const int MinimumSample = 20;
    /// <summary>Late means more than 2 minutes behind the Timetable.</summary>
    public const int LateAfterSeconds = 120;

    private record Row(string? Line, short? DirectionId, string TimeBand, int Observed, int Late, int Timetabled, int Cancelled);

    public async Task<IReadOnlyDictionary<StatsBucket, BucketStats>> ForStationAsync(string stationId, CancellationToken ct)
    {
        var until = time.GetUtcNow();
        var since = until - Window;

        // The Time Band CASE must match TimeBands.Of. Skipped and TripCancelled both count as Cancelled.
        // Columns are snake_case because the naming convention maps Row's properties too.
        var rows = await db.Database.SqlQuery<Row>($"""
            SELECT o.line AS line, o.direction_id AS direction_id,
                CASE
                    WHEN extract(isodow FROM o.local) >= 6 THEN 'Weekend'
                    WHEN o.local::time >= '06:30' AND o.local::time < '09:30' THEN 'AmPeak'
                    WHEN o.local::time >= '15:00' AND o.local::time < '19:00' THEN 'PmPeak'
                    ELSE 'OffPeak'
                END AS time_band,
                (count(*) FILTER (WHERE o.status = 'Passed'))::int AS observed,
                (count(*) FILTER (WHERE o.status = 'Passed' AND o.delay_seconds > {LateAfterSeconds}))::int AS late,
                count(*)::int AS timetabled,
                (count(*) FILTER (WHERE o.status <> 'Passed'))::int AS cancelled
            FROM (
                SELECT line, direction_id, status, delay_seconds, scheduled_at AT TIME ZONE 'Australia/Sydney' AS local
                FROM observations
                WHERE station_id = {stationId} AND scheduled_at >= {since} AND scheduled_at < {until}
            ) o
            GROUP BY 1, 2, 3
            """).ToListAsync(ct);

        return rows.ToDictionary(
            r => new StatsBucket(r.Line, r.DirectionId, Enum.Parse<TimeBand>(r.TimeBand)),
            r => new BucketStats(r.Observed, r.Late, r.Timetabled, r.Cancelled));
    }
}
