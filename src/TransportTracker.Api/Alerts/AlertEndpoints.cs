namespace TransportTracker.Api.Alerts;

public static class AlertEndpoints
{
    public static RouteGroupBuilder MapAlerts(this RouteGroupBuilder v1)
    {
        // Active alerts, optionally only those affecting some Stations or Lines (plus network-wide ones),
        // e.g. /v1/alerts?stations=200060,213510&lines=T8.
        v1.MapGet("/alerts", (string? stations, string? lines, AlertStore store, TimeProvider time) =>
        {
            var now = time.GetUtcNow();
            var stationIds = Split(stations);
            var lineNames = Split(lines);
            var everything = stationIds.Count == 0 && lineNames.Count == 0;

            var alerts = store.Alerts
                .Where(a => a.IsActive(now) && (everything || a.Affects(stationIds, lineNames)))
                .ToList();
            return Results.Ok(new { fetchedAt = store.FetchedAt, alerts });
        });

        return v1;
    }

    private static HashSet<string> Split(string? csv) =>
        (csv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet();
}
