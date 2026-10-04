using Microsoft.EntityFrameworkCore;
using TransportTracker.Api.Data;
using TransportTracker.Api.Timetable;

namespace TransportTracker.Api.Poller;

/// <summary>
/// Turns the tracker's changes into Observations by matching them against the active Timetable.
/// The feed has no start_date or stop_sequence, so the service day and the timetabled stop are
/// whichever fit the feed's times best.
/// </summary>
public class ObservationRecorder(AppDbContext db, TimeProvider time, ILogger<ObservationRecorder> logger)
{
    /// <summary>
    /// A stop that leaves the feed more than this long before its predicted time hasn't been passed.
    /// It was more likely a platform change or a reroute, so no Observation is recorded.
    /// </summary>
    public static readonly TimeSpan EarlyDropTolerance = TimeSpan.FromSeconds(60);

    private record TimetabledStop(string TripId, string StopId, string StationId, int Seconds,
        string RouteId, string? Line, short? DirectionId, string? Headsign);

    public async Task<int> RecordAsync(FeedChanges changes, CancellationToken ct)
    {
        if (changes.Passed.Count == 0 && changes.CancelledTrips.Count == 0) return 0;

        var importId = await db.TimetableImports.Where(i => i.IsActive).Select(i => (int?)i.Id).SingleOrDefaultAsync(ct);
        if (importId is null)
        {
            logger.LogWarning("No active Timetable yet, dropping {Count} changes", changes.Passed.Count + changes.CancelledTrips.Count);
            return 0;
        }

        var tripIds = changes.Passed.Select(p => p.TripId).Concat(changes.CancelledTrips).Distinct().ToList();
        var timetable = (await LoadTimetableAsync(importId.Value, tripIds, ct)).ToLookup(s => s.TripId);
        var stopIds = changes.Passed.Select(p => p.StopId).Distinct().ToList();
        var stations = await db.Stops.AsNoTracking()
            .Where(s => s.ImportId == importId && stopIds.Contains(s.StopId))
            .ToDictionaryAsync(s => s.StopId, s => s.ParentStation ?? s.StopId, ct);

        var recordedAt = time.GetUtcNow();
        var observations = new List<Observation>();
        foreach (var stop in changes.Passed)
        {
            var station = stations.GetValueOrDefault(stop.StopId, stop.StopId);
            if (ToObservation(stop, station, timetable[stop.TripId], changes.FeedTime, recordedAt) is { } o)
                observations.Add(o);
        }
        foreach (var tripId in changes.CancelledTrips)
            observations.AddRange(CancelledTrip(timetable[tripId].ToList(), changes.FeedTime, recordedAt));

        var inserted = 0;
        foreach (var o in observations)
        {
            // The unique key makes recording idempotent, e.g. a cancellation seen again after a restart.
            inserted += await db.Database.ExecuteSqlAsync($"""
                INSERT INTO observations (service_date, trip_id, route_id, line, direction_id, headsign, stop_id,
                    station_id, scheduled_at, delay_seconds, status, recorded_at)
                VALUES ({o.ServiceDate}, {o.TripId}, {o.RouteId}, {o.Line}, {o.DirectionId}, {o.Headsign}, {o.StopId},
                    {o.StationId}, {o.ScheduledAt}, {o.DelaySeconds}, {o.Status.ToString()}, {o.RecordedAt})
                ON CONFLICT DO NOTHING
                """, ct);
        }

        logger.LogDebug("Recorded {Inserted} Observations from {Passed} passed stops and {Cancelled} cancelled Trips",
            inserted, changes.Passed.Count, changes.CancelledTrips.Count);
        return inserted;
    }

    private static Observation? ToObservation(LiveStop stop, string station, IEnumerable<TimetabledStop> tripStops,
        DateTimeOffset feedTime, DateTimeOffset recordedAt)
    {
        // A platform change keeps the Station, so match on that rather than the platform.
        var candidates = tripStops.Where(s => s.StationId == station).ToList();
        if (candidates.Count == 0) return null;

        // Roughly when the train was at the stop. A Trip can call at a Station twice (e.g. the City Circle),
        // and on any of three service days, so pick the timetabled time closest to it.
        var around = stop.PredictedTime is { } t
            ? DateTimeOffset.FromUnixTimeSeconds(t)
            : feedTime.AddSeconds(-(stop.DelaySeconds ?? 0));
        var (match, serviceDate, scheduledAt) = ClosestScheduled(candidates, around);

        var delay = stop.DelaySeconds
            ?? (stop.PredictedTime is { } p ? (int)(p - scheduledAt.ToUnixTimeSeconds()) : null);
        if (delay is null && !stop.Skipped) return null;

        if (scheduledAt.AddSeconds(delay ?? 0) > feedTime + EarlyDropTolerance) return null;

        return new Observation
        {
            ServiceDate = serviceDate,
            TripId = stop.TripId,
            RouteId = match.RouteId,
            Line = match.Line,
            DirectionId = match.DirectionId,
            Headsign = match.Headsign,
            StopId = stop.StopId,
            StationId = station,
            ScheduledAt = scheduledAt,
            DelaySeconds = stop.Skipped ? null : delay,
            Status = stop.Skipped ? ObservationStatus.Skipped : ObservationStatus.Passed,
            RecordedAt = recordedAt,
        };
    }

    private static IEnumerable<Observation> CancelledTrip(List<TimetabledStop> tripStops, DateTimeOffset feedTime,
        DateTimeOffset recordedAt)
    {
        if (tripStops.Count == 0) yield break;

        // Cancellations are announced ahead of time, so the service day is the one whose run of
        // this Trip starts closest to now.
        var first = tripStops.MinBy(s => s.Seconds)!;
        var (_, serviceDate, _) = ClosestScheduled([first], feedTime);

        foreach (var s in tripStops)
        {
            yield return new Observation
            {
                ServiceDate = serviceDate,
                TripId = s.TripId,
                RouteId = s.RouteId,
                Line = s.Line,
                DirectionId = s.DirectionId,
                Headsign = s.Headsign,
                StopId = s.StopId,
                StationId = s.StationId,
                ScheduledAt = GtfsTime.At(serviceDate, s.Seconds),
                Status = ObservationStatus.TripCancelled,
                RecordedAt = recordedAt,
            };
        }
    }

    private static (TimetabledStop Stop, DateOnly ServiceDate, DateTimeOffset ScheduledAt) ClosestScheduled(
        IEnumerable<TimetabledStop> candidates, DateTimeOffset around)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(around, GtfsTime.Sydney).DateTime);
        return candidates
            .SelectMany(_ => new[] { today.AddDays(-1), today, today.AddDays(1) }, (s, d) => (s, d, GtfsTime.At(d, s.Seconds)))
            .MinBy(x => (x.Item3 - around).Duration());
    }

    private async Task<List<TimetabledStop>> LoadTimetableAsync(int importId, List<string> tripIds, CancellationToken ct)
    {
        var rows = await (
            from st in db.StopTimes
            where st.ImportId == importId && tripIds.Contains(st.TripId)
                && (st.ArrivalSeconds != null || st.DepartureSeconds != null)
            join s in db.Stops on new { st.ImportId, st.StopId } equals new { s.ImportId, s.StopId }
            join t in db.Trips on new { st.ImportId, st.TripId } equals new { t.ImportId, t.TripId }
            join r in db.Routes on new { t.ImportId, t.RouteId } equals new { r.ImportId, r.RouteId } into routes
            from r in routes.DefaultIfEmpty()
            select new
            {
                st.TripId,
                st.StopId,
                StationId = s.ParentStation ?? s.StopId,
                Seconds = st.ArrivalSeconds ?? st.DepartureSeconds,
                t.RouteId,
                Line = r.ShortName,
                t.DirectionId,
                t.Headsign,
            }).AsNoTracking().ToListAsync(ct);

        return rows.Select(r => new TimetabledStop(r.TripId, r.StopId, r.StationId, r.Seconds!.Value,
            r.RouteId, r.Line, r.DirectionId, r.Headsign)).ToList();
    }
}
