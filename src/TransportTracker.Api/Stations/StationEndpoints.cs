using Microsoft.EntityFrameworkCore;
using TransportTracker.Api.Data;

namespace TransportTracker.Api.Stations;

public static class StationEndpoints
{
    public const int MaxSearchResults = 20;
    public const int DefaultDepartures = 20, MaxDepartures = 50;

    public static RouteGroupBuilder MapStations(this RouteGroupBuilder v1)
    {
        // Station search, e.g. /v1/stations?q=central. Names starting with the query come first.
        v1.MapGet("/stations", async (string? q, AppDbContext db, CancellationToken ct) =>
        {
            q = q?.Trim();
            if (string.IsNullOrEmpty(q) || q.Length < 2)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["q"] = ["Enter at least 2 characters."] });

            var pattern = q.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");
            var stations = await db.Stops.AsNoTracking()
                .Where(s => s.LocationType == 1
                    && db.TimetableImports.Any(i => i.IsActive && i.Id == s.ImportId)
                    && EF.Functions.ILike(s.Name, $"%{pattern}%"))
                .OrderBy(s => !EF.Functions.ILike(s.Name, $"{pattern}%"))
                .ThenBy(s => s.Name)
                .Take(MaxSearchResults)
                .Select(s => new StationSummary(s.StopId, s.Name, s.Lat, s.Lon))
                .ToListAsync(ct);
            return Results.Ok(stations);
        });

        // The next departures from a Station with Live Delays and Late %, e.g. /v1/stations/200060/departures.
        // With ?to=, only direct Trips to that Station, each with its arrival: the trains for a Saved Trip.
        // Pass a response's `earlier` or `later` back as ?cursor= for the page before or after it.
        v1.MapGet("/stations/{stationId}/departures", async (string stationId, string? to, int? limit, string? cursor,
            DepartureBoard board, CancellationToken ct) =>
        {
            if (to == stationId)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["to"] = ["Choose a different Station to travel to."] });
            if (!BoardCursor.TryParse(cursor, out var parsed))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["cursor"] = ["Not a cursor from this API."] });

            var result = await board.GetAsync(stationId, string.IsNullOrEmpty(to) ? null : to,
                Math.Clamp(limit ?? DefaultDepartures, 1, MaxDepartures), parsed, ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        return v1;
    }
}
