namespace TransportTracker.Api.Lines;

public static class LineEndpoints
{
    public static RouteGroupBuilder MapLines(this RouteGroupBuilder v1)
    {
        // Every Line's Late % and Cancellation %, overall and per Time Band, e.g. /v1/lines.
        v1.MapGet("/lines", async (LineReports reports, CancellationToken ct) => Results.Ok(await reports.AllAsync(ct)));

        // Each Line's stats at one Station, overall and per Time Band, e.g. /v1/stations/200060/lines.
        v1.MapGet("/stations/{stationId}/lines", async (string stationId, LineReports reports, CancellationToken ct) =>
            await reports.AtStationAsync(stationId, ct) is { } lines ? Results.Ok(lines) : Results.NotFound());

        // One Line broken down by Direction, Station and Time Band, e.g. /v1/lines/T8.
        v1.MapGet("/lines/{line}", async (string line, LineReports reports, CancellationToken ct) =>
            await reports.GetAsync(line, ct) is { } report ? Results.Ok(report) : Results.NotFound());

        return v1;
    }
}
