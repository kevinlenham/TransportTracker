namespace TransportTracker.Api.Timetable;

public static class GtfsTime
{
    /// <summary>
    /// Parses a GTFS "H:MM:SS" time into seconds after midnight of the service day.
    /// Hours can be 24 or more for Trips that run past midnight. Empty means no time given.
    /// </summary>
    public static int? ParseSeconds(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var parts = value.Trim().Split(':');
        if (parts.Length != 3
            || !int.TryParse(parts[0], out var h)
            || !int.TryParse(parts[1], out var m)
            || !int.TryParse(parts[2], out var s)
            || h < 0 || m is < 0 or > 59 || s is < 0 or > 59)
        {
            throw new FormatException($"Invalid GTFS time '{value}'.");
        }

        return h * 3600 + m * 60 + s;
    }
}
