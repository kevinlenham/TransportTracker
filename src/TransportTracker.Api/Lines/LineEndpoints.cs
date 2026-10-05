namespace TransportTracker.Api.Lines;

public static class LineEndpoints
{
    public static RouteGroupBuilder MapLines(this RouteGroupBuilder v1)
    {
        // Every Line's overall Late % and Cancellation %, e.g. /v1/lines.
        v1.MapGet("/lines", async (LineReports reports, CancellationToken ct) => Results.Ok(await reports.AllAsync(ct)));

        // One Line broken down by Direction, Station and Time Band, e.g. /v1/lines/T8.
        v1.MapGet("/lines/{line}", async (string line, LineReports reports, CancellationToken ct) =>
            await reports.GetAsync(line, ct) is { } report ? Results.Ok(report) : Results.NotFound());

        return v1;
    }
}
