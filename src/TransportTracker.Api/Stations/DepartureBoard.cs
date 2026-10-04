using Microsoft.EntityFrameworkCore;
using TransportTracker.Api.Data;
using TransportTracker.Api.Poller;
using TransportTracker.Api.Stats;
using TransportTracker.Api.Timetable;

namespace TransportTracker.Api.Stations;

public enum DepartureStatus
{
    /// <summary>The Trip isn't in the realtime feed yet, so only the Timetable time is known.</summary>
    Scheduled,
    /// <summary>The feed gives a Live Delay for this Station.</summary>
    Live,
    Cancelled,
    /// <summary>The Trip runs but won't stop at this Station.</summary>
    Skipped,
}

public record LateChance(TimeBand TimeBand, double? LatePercent, int SampleSize, int MinimumSample);

public record Departure(
    string TripId,
    string? Line,
    string? LineColor,
    string? Headsign,
    short? DirectionId,
    string PlatformId,
    string? PlatformName,
    DateTimeOffset ScheduledAt,
    DateTimeOffset ExpectedAt,
    int? DelaySeconds,
    DepartureStatus Status,
    LateChance Late);

public record StationSummary(string Id, string Name, double Lat, double Lon);

public record Board(StationSummary Station, DateTimeOffset GeneratedAt, DateTimeOffset? FeedTime, IReadOnlyList<Departure> Departures);

/// <summary>
/// The next departures from a Station: the Timetable, overlaid with Live Delays from the latest poll,
/// and each one's Late % for its Stats Bucket.
/// </summary>
public class DepartureBoard(AppDbContext db, PollerStatus poller, LateStatsQuery stats, TimeProvider time)
{
    /// <summary>How far back to look for timetabled departures that are running late but haven't left yet.</summary>
    public static readonly TimeSpan LookBack = TimeSpan.FromMinutes(90);
    public static readonly TimeSpan LookAhead = TimeSpan.FromHours(3);
    /// <summary>A departure stays on the board this long after its expected time, as trains rarely leave to the second.</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromMinutes(1);

    // Trains riders can't board. Empty runs also appear on passenger Lines, so the headsign is checked as well.
    private const string EmptyTrainHeadsign = "Empty Train";
    /// <summary>RTTA_REV ("Non Revenue") and RTTA_DEF ("Out Of Service").</summary>
    private const string NonPassengerRoutePrefix = "RTTA_";

    private record Row(string TripId, string StopId, int Seconds, string? Line, string? LineColor, string? Headsign,
        short? DirectionId, DateOnly ServiceDate);

    /// <summary>Null when the Station isn't in the active Timetable.</summary>
    public async Task<Board?> GetAsync(string stationId, int limit, CancellationToken ct)
    {
        var importId = await db.TimetableImports.Where(i => i.IsActive).Select(i => (int?)i.Id).SingleOrDefaultAsync(ct);
        if (importId is null) return null;

        var station = await db.Stops.AsNoTracking()
            .Where(s => s.ImportId == importId && s.StopId == stationId && s.LocationType == 1)
            .Select(s => new StationSummary(s.StopId, s.Name, s.Lat, s.Lon))
            .SingleOrDefaultAsync(ct);
        if (station is null) return null;

        var platforms = await db.Stops.AsNoTracking()
            .Where(s => s.ImportId == importId && s.ParentStation == stationId)
            .ToDictionaryAsync(s => s.StopId, s => s.Name, ct);
        platforms.TryAdd(station.Id, station.Name); // In case anything is timetabled at the Station itself.

        var now = time.GetUtcNow();
        var rows = await TimetabledAsync(importId.Value, platforms.Keys.ToList(), now, ct);
        var live = poller.Live;
        var bucketStats = await stats.ForStationAsync(stationId, ct);

        var departures = new List<Departure>();
        foreach (var row in rows)
        {
            var scheduledAt = GtfsTime.At(row.ServiceDate, row.Seconds);
            var status = DepartureStatus.Scheduled;
            var platformId = row.StopId;
            int? delay = null;

            if (live?.CancelledTrips.Contains(row.TripId) == true)
            {
                status = DepartureStatus.Cancelled;
            }
            else if (live?.Trips.TryGetValue(row.TripId, out var tripStops) == true)
            {
                // Match on any platform of the Station, so a platform change still finds the train.
                var liveStop = tripStops.Values.FirstOrDefault(s => platforms.ContainsKey(s.StopId));
                if (liveStop is null) continue; // The train is in the feed but has already left this Station.

                platformId = liveStop.StopId;
                status = liveStop.Skipped ? DepartureStatus.Skipped : DepartureStatus.Live;
                delay = liveStop.DelaySeconds
                    ?? (liveStop.PredictedTime is { } p ? (int)(p - scheduledAt.ToUnixTimeSeconds()) : null);
            }

            var expectedAt = scheduledAt.AddSeconds(delay ?? 0);
            if (expectedAt < now - Grace) continue;

            var band = TimeBands.Of(scheduledAt);
            var bucket = bucketStats.GetValueOrDefault(new StatsBucket(row.Line, row.DirectionId, band), BucketStats.Empty);
            departures.Add(new Departure(row.TripId, row.Line, row.LineColor, row.Headsign, row.DirectionId,
                platformId, platforms.GetValueOrDefault(platformId), scheduledAt, expectedAt, delay, status,
                new LateChance(band, bucket.LatePercent, bucket.Observed, LateStatsQuery.MinimumSample)));
        }

        return new Board(station, now, live?.FeedTime,
            departures.OrderBy(d => d.ExpectedAt).ThenBy(d => d.ScheduledAt).Take(limit).ToList());
    }

    /// <summary>
    /// Timetabled departures from the platforms in the window around now. A service day's times can run
    /// past 24:00, so yesterday's and tomorrow's service days are checked too.
    /// </summary>
    private async Task<List<Row>> TimetabledAsync(int importId, List<string> platformIds, DateTimeOffset now, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, GtfsTime.Sydney).DateTime);
        var rows = new List<Row>();

        foreach (var date in new[] { today.AddDays(-1), today, today.AddDays(1) })
        {
            var dayStart = GtfsTime.ServiceDayStart(date);
            var fromSeconds = (int)(now - LookBack - dayStart).TotalSeconds;
            var toSeconds = (int)(now + LookAhead - dayStart).TotalSeconds;
            if (toSeconds < 0) continue;

            var serviceIds = (await db.ServiceCalendars.AsNoTracking()
                    .Where(c => c.ImportId == importId && c.StartDate <= date && c.EndDate >= date)
                    .ToListAsync(ct))
                .Where(c => RunsOn(c, date.DayOfWeek))
                .Select(c => c.ServiceId)
                .ToList();
            if (serviceIds.Count == 0) continue;

            rows.AddRange(await (
                from st in db.StopTimes
                where st.ImportId == importId && platformIds.Contains(st.StopId)
                    && st.DepartureSeconds >= fromSeconds && st.DepartureSeconds <= toSeconds
                    && st.PickupType != 1 // Set-down only, e.g. the train terminates here.
                join t in db.Trips on new { st.ImportId, st.TripId } equals new { t.ImportId, t.TripId }
                where serviceIds.Contains(t.ServiceId)
                    && t.Headsign != EmptyTrainHeadsign && !t.RouteId.StartsWith(NonPassengerRoutePrefix)
                join r in db.Routes on new { t.ImportId, t.RouteId } equals new { r.ImportId, r.RouteId } into routes
                from r in routes.DefaultIfEmpty()
                select new Row(st.TripId, st.StopId, st.DepartureSeconds!.Value, r.ShortName, r.Color, t.Headsign,
                    t.DirectionId, date)
            ).AsNoTracking().ToListAsync(ct));
        }

        return rows;
    }

    private static bool RunsOn(ServiceCalendar c, DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => c.Monday,
        DayOfWeek.Tuesday => c.Tuesday,
        DayOfWeek.Wednesday => c.Wednesday,
        DayOfWeek.Thursday => c.Thursday,
        DayOfWeek.Friday => c.Friday,
        DayOfWeek.Saturday => c.Saturday,
        _ => c.Sunday,
    };
}
