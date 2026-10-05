using Microsoft.EntityFrameworkCore;
using TransportTracker.Api.Data;

namespace TransportTracker.Api.Stats;

/// <summary>The Stats Bucket a Late % is calculated over. Line + Direction is how a Direction is identified.</summary>
public record StatsBucket(string? Line, short? DirectionId, TimeBand TimeBand);

/// <summary>Observation counts over the stats window, for a Stats Bucket or a wider grouping.</summary>
public record BucketStats(int Observed, int Late, int Timetabled, int Cancelled)
{
    public static readonly BucketStats Empty = new(0, 0, 0, 0);

    /// <summary>Null below the Minimum Sample, when the UI shows "collecting data".</summary>
    public double? LatePercent => Observed >= LateStatsQuery.MinimumSample ? Percent(Late, Observed) : null;

    public double? CancellationPercent => Timetabled >= LateStatsQuery.MinimumSample ? Percent(Cancelled, Timetabled) : null;

    public static BucketStats operator +(BucketStats a, BucketStats b) =>
        new(a.Observed + b.Observed, a.Late + b.Late, a.Timetabled + b.Timetabled, a.Cancelled + b.Cancelled);

    public static BucketStats Sum(IEnumerable<BucketStats> all) => all.Aggregate(Empty, (a, b) => a + b);

    private static double Percent(int part, int whole) => Math.Round(100.0 * part / whole, 1);
}

/// <summary>
/// Late % and Cancellation % over the last 3 weeks of Observations. The rules for Time Band, Late and
/// Cancelled live in the observation_stats view (see the AddObservationStatsView migration).
/// </summary>
public class LateStatsQuery(AppDbContext db, TimeProvider time)
{
    public static readonly TimeSpan Window = TimeSpan.FromDays(21);
    public const int MinimumSample = 20;
    /// <summary>Late means more than 2 minutes behind the Timetable. Must match observation_stats.</summary>
    public const int LateAfterSeconds = 120;

    // Columns are snake_case because the naming convention maps these records' properties too.
    private record StationRow(string? Line, short? DirectionId, string TimeBand, int Observed, int Late, int Timetabled, int Cancelled);
    private record LineRow(string Line, int Observed, int Late, int Timetabled, int Cancelled);
    private record LineStationRow(string StationId, short? DirectionId, string TimeBand, int Observed, int Late, int Timetabled, int Cancelled);
    private record HeadsignRow(short? DirectionId, string? Headsign, int Count);

    /// <summary>Each Stats Bucket at a Station.</summary>
    public async Task<IReadOnlyDictionary<StatsBucket, BucketStats>> ForStationAsync(string stationId, CancellationToken ct)
    {
        var (since, until) = CurrentWindow();
        var rows = await db.Database.SqlQuery<StationRow>($"""
            SELECT line, direction_id, time_band,
                (count(*) FILTER (WHERE observed))::int AS observed, (count(*) FILTER (WHERE late))::int AS late,
                count(*)::int AS timetabled, (count(*) FILTER (WHERE cancelled))::int AS cancelled
            FROM observation_stats
            WHERE station_id = {stationId} AND scheduled_at >= {since} AND scheduled_at < {until}
            GROUP BY 1, 2, 3
            """).ToListAsync(ct);

        return rows.ToDictionary(
            r => new StatsBucket(r.Line, r.DirectionId, Enum.Parse<TimeBand>(r.TimeBand)),
            r => new BucketStats(r.Observed, r.Late, r.Timetabled, r.Cancelled));
    }

    /// <summary>Each Line across the whole network.</summary>
    public async Task<IReadOnlyDictionary<string, BucketStats>> ByLineAsync(CancellationToken ct)
    {
        var (since, until) = CurrentWindow();
        var rows = await db.Database.SqlQuery<LineRow>($"""
            SELECT line,
                (count(*) FILTER (WHERE observed))::int AS observed, (count(*) FILTER (WHERE late))::int AS late,
                count(*)::int AS timetabled, (count(*) FILTER (WHERE cancelled))::int AS cancelled
            FROM observation_stats
            WHERE line IS NOT NULL AND scheduled_at >= {since} AND scheduled_at < {until}
            GROUP BY 1
            """).ToListAsync(ct);

        return rows.ToDictionary(r => r.Line, r => new BucketStats(r.Observed, r.Late, r.Timetabled, r.Cancelled));
    }

    /// <summary>A Line's Stats Buckets, keyed by Station, Direction and Time Band.</summary>
    public async Task<IReadOnlyDictionary<(string StationId, short? DirectionId, TimeBand TimeBand), BucketStats>> ForLineAsync(
        string line, CancellationToken ct)
    {
        var (since, until) = CurrentWindow();
        var rows = await db.Database.SqlQuery<LineStationRow>($"""
            SELECT station_id, direction_id, time_band,
                (count(*) FILTER (WHERE observed))::int AS observed, (count(*) FILTER (WHERE late))::int AS late,
                count(*)::int AS timetabled, (count(*) FILTER (WHERE cancelled))::int AS cancelled
            FROM observation_stats
            WHERE line = {line} AND scheduled_at >= {since} AND scheduled_at < {until}
            GROUP BY 1, 2, 3
            """).ToListAsync(ct);

        return rows.ToDictionary(
            r => (r.StationId, r.DirectionId, Enum.Parse<TimeBand>(r.TimeBand)),
            r => new BucketStats(r.Observed, r.Late, r.Timetabled, r.Cancelled));
    }

    /// <summary>The most common headsigns per Direction of a Line, most common first, to name the Directions.</summary>
    public async Task<ILookup<short?, string>> HeadsignsAsync(string line, CancellationToken ct)
    {
        var (since, until) = CurrentWindow();
        var rows = await db.Database.SqlQuery<HeadsignRow>($"""
            SELECT direction_id, headsign, count(*)::int AS count
            FROM observation_stats
            WHERE line = {line} AND headsign IS NOT NULL AND scheduled_at >= {since} AND scheduled_at < {until}
            GROUP BY 1, 2
            ORDER BY count DESC
            """).ToListAsync(ct);

        return rows.ToLookup(r => r.DirectionId, r => r.Headsign!);
    }

    private (DateTimeOffset Since, DateTimeOffset Until) CurrentWindow()
    {
        var until = time.GetUtcNow();
        return (until - Window, until);
    }
}
