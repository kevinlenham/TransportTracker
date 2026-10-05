using Microsoft.EntityFrameworkCore;
using TransportTracker.Api.Data;
using TransportTracker.Api.Stats;

namespace TransportTracker.Api.Lines;

public record LineSummary(string Line, string? Color, BucketStats Stats);

public record TimeBandStats(TimeBand TimeBand, BucketStats Stats);

public record LineStation(string StationId, string Name, BucketStats Stats, IReadOnlyList<TimeBandStats> TimeBands);

/// <summary>One Direction of a Line, named by its most common headsigns, with its Stations in running order.</summary>
public record LineDirection(short? DirectionId, IReadOnlyList<string> Headsigns, BucketStats Stats, IReadOnlyList<LineStation> Stations);

public record LineReport(string Line, string? Color, BucketStats Stats, IReadOnlyList<LineDirection> Directions);

/// <summary>Late % and Cancellation % per Line, and per Station, Direction and Time Band within a Line.</summary>
public class LineReports(AppDbContext db, LateStatsQuery stats)
{
    /// <summary>RTTA_REV ("Non Revenue") and RTTA_DEF ("Out Of Service") aren't Lines riders use.</summary>
    private const string NonPassengerRoutePrefix = "RTTA_";
    private const int HeadsignsShown = 3;

    private static readonly TimeBand[] BandOrder = [TimeBand.AmPeak, TimeBand.PmPeak, TimeBand.OffPeak, TimeBand.Weekend];

    private record StationOrderRow(string StationId, int StopSequence);

    public async Task<IReadOnlyList<LineSummary>> AllAsync(CancellationToken ct)
    {
        var lines = await LinesAsync(ct);
        var byLine = await stats.ByLineAsync(ct);
        return lines
            .Select(l => new LineSummary(l.Key, l.Value, byLine.GetValueOrDefault(l.Key, BucketStats.Empty)))
            .OrderBy(l => SortKey(l.Line))
            .ToList();
    }

    /// <summary>Null when the Line isn't in the active Timetable.</summary>
    public async Task<LineReport?> GetAsync(string line, CancellationToken ct)
    {
        var lines = await LinesAsync(ct);
        if (!lines.TryGetValue(line, out var color)) return null;

        var importId = await db.TimetableImports.Where(i => i.IsActive).Select(i => i.Id).SingleAsync(ct);
        var buckets = await stats.ForLineAsync(line, ct);
        var headsigns = await stats.HeadsignsAsync(line, ct);

        var directionIds = await (
            from t in db.Trips
            join r in db.Routes on new { t.ImportId, t.RouteId } equals new { r.ImportId, r.RouteId }
            where t.ImportId == importId && r.ShortName == line
            select t.DirectionId).Distinct().ToListAsync(ct);

        var stationIds = buckets.Keys.Select(k => k.StationId).ToHashSet();
        var orders = new Dictionary<short, List<string>>(); // Keyed by direction, with -1 for none.
        foreach (var direction in directionIds)
        {
            orders[direction ?? -1] = await StationOrderAsync(importId, line, direction, ct);
            stationIds.UnionWith(orders[direction ?? -1]);
        }

        var names = await db.Stops.AsNoTracking()
            .Where(s => s.ImportId == importId && stationIds.Contains(s.StopId))
            .ToDictionaryAsync(s => s.StopId, s => s.Name, ct);

        var directions = directionIds.Order().Select(direction =>
        {
            // Stations in running order, then any with Observations that the longest Trip doesn't call at.
            var order = orders[direction ?? -1];
            var observed = buckets.Keys.Where(k => k.DirectionId == direction).Select(k => k.StationId).Distinct();
            var stations = order
                .Concat(observed.Except(order).OrderBy(id => names.GetValueOrDefault(id, id)))
                .Select(id =>
                {
                    var bands = BandOrder
                        .Select(b => new TimeBandStats(b, buckets.GetValueOrDefault((id, direction, b), BucketStats.Empty)))
                        .ToList();
                    return new LineStation(id, names.GetValueOrDefault(id, id), BucketStats.Sum(bands.Select(b => b.Stats)), bands);
                })
                .ToList();

            return new LineDirection(direction, headsigns[direction].Take(HeadsignsShown).ToList(),
                BucketStats.Sum(stations.Select(s => s.Stats)), stations);
        }).ToList();

        return new LineReport(line, color, BucketStats.Sum(directions.Select(d => d.Stats)), directions);
    }

    /// <summary>Each Line in the active Timetable, with its colour.</summary>
    private async Task<Dictionary<string, string?>> LinesAsync(CancellationToken ct)
    {
        var routes = await db.Routes.AsNoTracking()
            .Where(r => db.TimetableImports.Any(i => i.IsActive && i.Id == r.ImportId)
                && r.ShortName != null && r.ShortName != "" && !r.RouteId.StartsWith(NonPassengerRoutePrefix))
            .Select(r => new { r.ShortName, r.Color })
            .ToListAsync(ct);

        return routes
            .GroupBy(r => r.ShortName!)
            .ToDictionary(g => g.Key, g => g.Select(r => r.Color).FirstOrDefault(c => !string.IsNullOrEmpty(c)));
    }

    /// <summary>The Stations of the Line's longest regular Trip in this Direction, in calling order.</summary>
    private async Task<List<string>> StationOrderAsync(int importId, string line, short? direction, CancellationToken ct)
    {
        var rows = await db.Database.SqlQuery<StationOrderRow>($"""
            WITH longest AS (
                SELECT st.trip_id
                FROM stop_times st
                JOIN trips t ON t.import_id = st.import_id AND t.trip_id = st.trip_id
                JOIN routes r ON r.import_id = t.import_id AND r.route_id = t.route_id
                LEFT JOIN service_calendars c ON c.import_id = t.import_id AND c.service_id = t.service_id
                WHERE st.import_id = {importId} AND r.short_name = {line}
                    AND coalesce(t.direction_id, -1) = {direction ?? -1}
                GROUP BY st.trip_id, c.start_date, c.end_date
                -- Prefer the regular timetable over trackwork diversions, which run for a few days only.
                ORDER BY coalesce(c.end_date - c.start_date, 0) DESC, count(*) DESC, st.trip_id
                LIMIT 1
            )
            SELECT coalesce(s.parent_station, s.stop_id) AS station_id, st.stop_sequence
            FROM stop_times st
            JOIN longest l ON l.trip_id = st.trip_id
            JOIN stops s ON s.import_id = st.import_id AND s.stop_id = st.stop_id
            WHERE st.import_id = {importId}
            ORDER BY st.stop_sequence
            """).ToListAsync(ct);

        return rows.Select(r => r.StationId).Distinct().ToList();
    }

    /// <summary>T1, T2 … T9 in number order, then the intercity Lines by name.</summary>
    private static (int, int, string) SortKey(string line) =>
        line.Length > 1 && line[0] == 'T' && int.TryParse(line[1..], out var n) ? (0, n, line) : (1, 0, line);
}
