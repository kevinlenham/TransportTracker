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
    /// <summary>The train has left. DelaySeconds is its Observation, or null if none was recorded.</summary>
    Departed,
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
    LateChance Late,
    Arrival? Arrival = null);

public record StationSummary(string Id, string Name, double Lat, double Lon);

/// <summary>When a Trip gets to the Destination of a Saved Trip.</summary>
public record Arrival(string PlatformId, string? PlatformName, DateTimeOffset ScheduledAt, DateTimeOffset ExpectedAt);

/// <summary>
/// One page of a departure board, in time order. For a Saved Trip, Destination is set and only Trips that
/// later stop there are included, each with its Arrival. Earlier and Later are cursors for the next pages
/// each way, or null when there are no more.
/// </summary>
public record Board(
    StationSummary Station,
    StationSummary? Destination,
    DateTimeOffset GeneratedAt,
    DateTimeOffset? FeedTime,
    IReadOnlyList<Departure> Departures,
    string? Earlier,
    string? Later);

/// <summary>
/// Departures from a Station: the Timetable, overlaid with Live Delays from the latest poll for trains to
/// come and with Observations for trains that have left, each with its Late % for its Stats Bucket.
/// The first page is the next trains from now; cursors page later through upcoming trains, or earlier
/// through the ones that have left.
/// </summary>
public class DepartureBoard(AppDbContext db, PollerStatus poller, LateStatsQuery stats, TimeProvider time)
{
    /// <summary>How far back to look for timetabled departures that are running late but haven't left yet.</summary>
    public static readonly TimeSpan LookBack = TimeSpan.FromMinutes(90);
    /// <summary>A departure stays upcoming this long after its expected time, as trains rarely leave to the second.</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromMinutes(1);
    /// <summary>How far from now pages reach each way. Earlier than this, the Timetable may not cover it.</summary>
    public static readonly TimeSpan Horizon = TimeSpan.FromHours(24);
    /// <summary>The Timetable is searched in steps this long until a page is full.</summary>
    private static readonly TimeSpan Step = TimeSpan.FromHours(4);

    // Trains riders can't board. Empty runs also appear on passenger Lines, so the headsign is checked as well.
    private const string EmptyTrainHeadsign = "Empty Train";
    /// <summary>RTTA_REV ("Non Revenue") and RTTA_DEF ("Out Of Service").</summary>
    private const string NonPassengerRoutePrefix = "RTTA_";

    private record Row(string TripId, string StopId, int Seconds, string? Line, string? LineColor, string? Headsign,
        short? DirectionId, DateOnly ServiceDate, string? ArrivalStopId, int? ArrivalSeconds);

    private record StationPlatforms(StationSummary Station, Dictionary<string, string> Platforms);

    /// <summary>A Departure with its place in board order and whether it has already left.</summary>
    private record Item(Departure Departure, BoardPosition Position, bool Past);

    private record Context(int ImportId, StationPlatforms Origin, StationPlatforms? Destination, DateTimeOffset Now,
        LiveFeed? Live, IReadOnlyDictionary<StatsBucket, BucketStats> Stats, Dictionary<DateOnly, List<string>> Services);

    /// <summary>
    /// A page of departures from a Station, or with toStationId, of the direct Trips from it to that Station
    /// (a Saved Trip). Null when either Station isn't in the active Timetable.
    /// </summary>
    public async Task<Board?> GetAsync(string stationId, string? toStationId, int limit, BoardCursor? cursor, CancellationToken ct)
    {
        var importId = await db.TimetableImports.Where(i => i.IsActive).Select(i => (int?)i.Id).SingleOrDefaultAsync(ct);
        if (importId is null) return null;

        var origin = await StationAsync(importId.Value, stationId, ct);
        if (origin is null) return null;
        StationPlatforms? destination = null;
        if (toStationId is not null && (destination = await StationAsync(importId.Value, toStationId, ct)) is null) return null;

        var now = time.GetUtcNow();
        var context = new Context(importId.Value, origin, destination, now, poller.Live,
            await stats.ForStationAsync(stationId, ct), []);

        var (departures, earlier, later) = cursor?.Earlier == true
            ? await EarlierPageAsync(context, cursor.Position, limit, ct)
            : await LaterPageAsync(context, cursor?.Position, limit, ct);

        return new Board(origin.Station, destination?.Station, now, context.Live?.FeedTime, departures, earlier, later);
    }

    /// <summary>
    /// Upcoming trains in order, after a position, or for the first page from now. Searches forward in steps
    /// until the page is full, so overnight it reaches the first trains of the morning.
    /// </summary>
    private async Task<(List<Departure>, string?, string?)> LaterPageAsync(Context c, BoardPosition? after, int limit, CancellationToken ct)
    {
        var horizon = c.Now + Horizon;
        var from = (after?.At ?? c.Now) - LookBack;
        var found = new List<Item>();
        while (from < horizon)
        {
            var to = Min(from + Step, horizon);
            found.AddRange((await ItemsAsync(c, from, to, includePast: false, ct))
                .Where(i => !i.Past && (after is null || i.Position.CompareTo(after) > 0)));
            from = to;

            // Pages are in expected-time order but searched by timetabled time. A train timetabled after `from`
            // can only be expected before it if it runs early, so once the page's last train is a margin
            // before `from`, nothing unsearched can still belong in the page.
            var sorted = found.OrderBy(i => i.Position).ToList();
            if (sorted.Count > limit && sorted[limit].Position.At < from - Grace) break;
        }

        var page = found.OrderBy(i => i.Position).DistinctBy(i => i.Position).ToList();
        var more = page.Count > limit;
        page = page.Take(limit).ToList();

        // From the first page, Earlier goes back to the most recent trains that have left.
        var earlier = after is null ? new BoardCursor(true, null).Encode()
            : page.Count > 0 ? new BoardCursor(true, page[0].Position).Encode() : null;
        var later = more ? new BoardCursor(false, page[^1].Position).Encode() : null;
        return (page.Select(i => i.Departure).ToList(), earlier, later);
    }

    /// <summary>Trains that have left, the most recent before a position (or of all), shown in time order.</summary>
    private async Task<(List<Departure>, string?, string?)> EarlierPageAsync(Context c, BoardPosition? before, int limit, CancellationToken ct)
    {
        var horizon = c.Now - Horizon;
        var to = Min(before?.At ?? c.Now, c.Now) + Grace;
        var found = new List<Item>();
        while (to > horizon)
        {
            var from = Max(to - Step, horizon);
            found.AddRange((await ItemsAsync(c, from, to, includePast: true, ct))
                .Where(i => i.Past && (before is null || i.Position.CompareTo(before) < 0)));
            to = from;

            // A train timetabled before `to` can have left after it if it ran late, by up to about LookBack.
            // Once the page's earliest train is later than that, nothing unsearched can still belong in it.
            var sorted = found.OrderByDescending(i => i.Position).ToList();
            if (sorted.Count > limit && sorted[limit].Position.At > to + LookBack) break;
        }

        var page = found.OrderByDescending(i => i.Position).DistinctBy(i => i.Position).ToList();
        var more = page.Count > limit;
        page = page.Take(limit).Reverse().ToList();

        var earlier = more ? new BoardCursor(true, page[0].Position).Encode() : null;
        return (page.Select(i => i.Departure).ToList(), earlier, null);
    }

    /// <summary>
    /// Departures timetabled to leave in [from, to), with what's known about each. Trains that have left are
    /// only built with includePast, as that needs their Observations.
    /// </summary>
    private async Task<List<Item>> ItemsAsync(Context c, DateTimeOffset from, DateTimeOffset to, bool includePast, CancellationToken ct)
    {
        var rows = await TimetabledAsync(c, from, to, ct);
        var pastRows = new List<(Row Row, DateTimeOffset ScheduledAt)>();
        var items = new List<Item>();

        foreach (var row in rows)
        {
            var scheduledAt = GtfsTime.At(row.ServiceDate, row.Seconds);
            var status = DepartureStatus.Scheduled;
            var platformId = row.StopId;
            int? delay = null;
            LiveStop? liveArrival = null;

            if (c.Live?.CancelledTrips.Contains(row.TripId) == true)
            {
                status = DepartureStatus.Cancelled;
            }
            else if (c.Live?.Trips.TryGetValue(row.TripId, out var tripStops) == true)
            {
                // Match on any platform of the Station, so a platform change still finds the train.
                var liveStop = tripStops.Values.FirstOrDefault(s => c.Origin.Platforms.ContainsKey(s.StopId));
                if (liveStop is null)
                {
                    pastRows.Add((row, scheduledAt)); // In the feed, but it has already left this Station.
                    continue;
                }

                platformId = liveStop.StopId;
                status = liveStop.Skipped ? DepartureStatus.Skipped : DepartureStatus.Live;
                delay = DelayOf(liveStop, scheduledAt);
                if (c.Destination is not null)
                    liveArrival = tripStops.Values.FirstOrDefault(s => c.Destination.Platforms.ContainsKey(s.StopId));
            }

            var expectedAt = scheduledAt.AddSeconds(delay ?? 0);
            if (expectedAt < c.Now - Grace)
            {
                pastRows.Add((row, scheduledAt));
                continue;
            }
            // The train won't get to the Destination if it skips it.
            if (liveArrival?.Skipped == true) continue;

            var arrival = ArrivalOf(c, row, liveArrival, delay);
            items.Add(new Item(ToDeparture(c, row, platformId, scheduledAt, expectedAt, delay, status, arrival),
                new BoardPosition(expectedAt, row.TripId), Past: false));
        }

        if (includePast) items.AddRange(await PastItemsAsync(c, pastRows, ct));
        return items;
    }

    /// <summary>Trains that have left, with their Observations at this Station where the poller recorded one.</summary>
    private async Task<List<Item>> PastItemsAsync(Context c, List<(Row Row, DateTimeOffset ScheduledAt)> past, CancellationToken ct)
    {
        if (past.Count == 0) return [];

        var tripIds = past.Select(p => p.Row.TripId).Distinct().ToList();
        var dates = past.Select(p => p.Row.ServiceDate).Distinct().ToList();
        var stationId = c.Origin.Station.Id;
        var observations = (await db.Observations.AsNoTracking()
                .Where(o => o.StationId == stationId && tripIds.Contains(o.TripId) && dates.Contains(o.ServiceDate))
                .Select(o => new { o.TripId, o.ServiceDate, o.ScheduledAt, o.Status, o.DelaySeconds, o.StopId })
                .ToListAsync(ct))
            .ToLookup(o => (o.TripId, o.ServiceDate));

        var items = new List<Item>();
        foreach (var (row, scheduledAt) in past)
        {
            // A Trip can call twice (e.g. the City Circle), so take the Observation for this call.
            var o = observations[(row.TripId, row.ServiceDate)].MinBy(o => (o.ScheduledAt - scheduledAt).Duration());
            var status = o?.Status switch
            {
                ObservationStatus.TripCancelled => DepartureStatus.Cancelled,
                ObservationStatus.Skipped => DepartureStatus.Skipped,
                ObservationStatus.Passed => DepartureStatus.Departed,
                // Not recorded: trust the feed if it still lists the Trip as cancelled.
                _ => c.Live?.CancelledTrips.Contains(row.TripId) == true ? DepartureStatus.Cancelled : DepartureStatus.Departed,
            };
            var delay = o?.DelaySeconds;
            var departedAt = scheduledAt.AddSeconds(delay ?? 0);
            var arrival = ArrivalOf(c, row, liveArrival: null, delay);
            items.Add(new Item(ToDeparture(c, row, o?.StopId ?? row.StopId, scheduledAt, departedAt, delay, status, arrival),
                new BoardPosition(departedAt, row.TripId), Past: true));
        }
        return items;
    }

    /// <summary>
    /// For a Saved Trip, when the train gets to the Destination: from the Destination's own Live Delay, or
    /// carrying the departure delay forward if the feed doesn't give one.
    /// </summary>
    private static Arrival? ArrivalOf(Context c, Row row, LiveStop? liveArrival, int? departureDelay)
    {
        if (c.Destination is null) return null;
        var scheduled = GtfsTime.At(row.ServiceDate, row.ArrivalSeconds!.Value);
        var platform = liveArrival?.StopId ?? row.ArrivalStopId!;
        var delay = (liveArrival is null ? null : DelayOf(liveArrival, scheduled)) ?? departureDelay ?? 0;
        return new Arrival(platform, c.Destination.Platforms.GetValueOrDefault(platform), scheduled, scheduled.AddSeconds(delay));
    }

    private static Departure ToDeparture(Context c, Row row, string platformId, DateTimeOffset scheduledAt,
        DateTimeOffset expectedAt, int? delay, DepartureStatus status, Arrival? arrival)
    {
        var band = TimeBands.Of(scheduledAt);
        var bucket = c.Stats.GetValueOrDefault(new StatsBucket(row.Line, row.DirectionId, band), BucketStats.Empty);
        return new Departure(row.TripId, row.Line, row.LineColor, row.Headsign, row.DirectionId, platformId,
            c.Origin.Platforms.GetValueOrDefault(platformId), scheduledAt, expectedAt, delay, status,
            new LateChance(band, bucket.LatePercent, bucket.Observed, LateStatsQuery.MinimumSample), arrival);
    }

    private static int? DelayOf(LiveStop stop, DateTimeOffset scheduledAt) =>
        stop.DelaySeconds ?? (stop.PredictedTime is { } p ? (int)(p - scheduledAt.ToUnixTimeSeconds()) : null);

    /// <summary>A Station and its platforms by stop ID, with their names. Null if it isn't a Station.</summary>
    private async Task<StationPlatforms?> StationAsync(int importId, string stationId, CancellationToken ct)
    {
        var station = await db.Stops.AsNoTracking()
            .Where(s => s.ImportId == importId && s.StopId == stationId && s.LocationType == 1)
            .Select(s => new StationSummary(s.StopId, s.Name, s.Lat, s.Lon))
            .SingleOrDefaultAsync(ct);
        if (station is null) return null;

        var platforms = await db.Stops.AsNoTracking()
            .Where(s => s.ImportId == importId && s.ParentStation == stationId)
            .ToDictionaryAsync(s => s.StopId, s => s.Name, ct);
        platforms.TryAdd(station.Id, station.Name); // In case anything is timetabled at the Station itself.
        return new StationPlatforms(station, platforms);
    }

    /// <summary>
    /// Timetabled departures from the origin's platforms in [from, to). For a Saved Trip, only Trips that set
    /// down at the Destination later are kept, with their first such call. A service day's times can run
    /// past 24:00, so the day before each date in range is checked too.
    /// </summary>
    private async Task<List<Row>> TimetabledAsync(Context c, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var platformIds = c.Origin.Platforms.Keys.ToList();
        var destinationPlatforms = c.Destination?.Platforms.Keys.ToList();
        var firstDate = LocalDate(from).AddDays(-1);
        var lastDate = LocalDate(to);
        var rows = new List<Row>();

        for (var date = firstDate; date <= lastDate; date = date.AddDays(1))
        {
            var dayStart = GtfsTime.ServiceDayStart(date);
            var fromSeconds = (int)Math.Ceiling((from - dayStart).TotalSeconds);
            var toSeconds = (int)Math.Ceiling((to - dayStart).TotalSeconds); // Exclusive.
            if (toSeconds <= 0) continue;

            var serviceIds = await ServicesAsync(c, date, ct);
            if (serviceIds.Count == 0) continue;

            var departures =
                from st in db.StopTimes
                where st.ImportId == c.ImportId && platformIds.Contains(st.StopId)
                    && st.DepartureSeconds >= fromSeconds && st.DepartureSeconds < toSeconds
                    && st.PickupType != 1 // Set-down only, e.g. the train terminates here.
                join t in db.Trips on new { st.ImportId, st.TripId } equals new { t.ImportId, t.TripId }
                where serviceIds.Contains(t.ServiceId)
                    && t.Headsign != EmptyTrainHeadsign && !t.RouteId.StartsWith(NonPassengerRoutePrefix)
                join r in db.Routes on new { t.ImportId, t.RouteId } equals new { r.ImportId, r.RouteId } into routes
                from r in routes.DefaultIfEmpty()
                select new
                {
                    st.TripId, st.StopId, st.StopSequence, Seconds = st.DepartureSeconds!.Value,
                    Line = r.ShortName, LineColor = r.Color, t.Headsign, t.DirectionId,
                };

            if (destinationPlatforms is null)
            {
                rows.AddRange((await departures.AsNoTracking().ToListAsync(ct)).Select(d =>
                    new Row(d.TripId, d.StopId, d.Seconds, d.Line, d.LineColor, d.Headsign, d.DirectionId, date, null, null)));
                continue;
            }

            var calls = await (
                from d in departures
                join a in db.StopTimes on new { ImportId = c.ImportId, d.TripId } equals new { a.ImportId, a.TripId }
                where destinationPlatforms.Contains(a.StopId) && a.StopSequence > d.StopSequence
                    && a.DropOffType != 1 // Pick-up only, so riders can't get off there.
                    && (a.ArrivalSeconds != null || a.DepartureSeconds != null)
                select new { Departure = d, ArrivalSequence = a.StopSequence, ArrivalStopId = a.StopId,
                    ArrivalSeconds = a.ArrivalSeconds ?? a.DepartureSeconds }
            ).AsNoTracking().ToListAsync(ct);

            // A Trip can call at the Destination more than once (e.g. a loop): keep the first call after departing.
            rows.AddRange(calls
                .GroupBy(x => (x.Departure.TripId, x.Departure.StopSequence))
                .Select(g => g.MinBy(x => x.ArrivalSequence)!)
                .Select(x => new Row(x.Departure.TripId, x.Departure.StopId, x.Departure.Seconds, x.Departure.Line,
                    x.Departure.LineColor, x.Departure.Headsign, x.Departure.DirectionId, date, x.ArrivalStopId, x.ArrivalSeconds)));
        }

        return rows;
    }

    /// <summary>The services running on a date, cached for the request since paging asks for each date repeatedly.</summary>
    private async Task<List<string>> ServicesAsync(Context c, DateOnly date, CancellationToken ct)
    {
        if (c.Services.TryGetValue(date, out var cached)) return cached;
        var services = (await db.ServiceCalendars.AsNoTracking()
                .Where(s => s.ImportId == c.ImportId && s.StartDate <= date && s.EndDate >= date)
                .ToListAsync(ct))
            .Where(s => RunsOn(s, date.DayOfWeek))
            .Select(s => s.ServiceId)
            .ToList();
        return c.Services[date] = services;
    }

    private static DateOnly LocalDate(DateTimeOffset at) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, GtfsTime.Sydney).DateTime);

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;
    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;

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
