namespace TransportTracker.Api.Timetable;

public static class GtfsTime
{
    /// <summary>The time zone all Sydney Trains Timetable times are in.</summary>
    public static readonly TimeZoneInfo Sydney = TimeZoneInfo.FindSystemTimeZoneById("Australia/Sydney");

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

    /// <summary>
    /// The instant a service day's times count from. GTFS defines it as noon minus 12 hours,
    /// which is midnight except on the days daylight saving starts or ends.
    /// </summary>
    public static DateTimeOffset ServiceDayStart(DateOnly date)
    {
        var noon = TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(new TimeOnly(12, 0)), Sydney);
        return new DateTimeOffset(noon, TimeSpan.Zero).AddHours(-12);
    }

    /// <summary>When a stop time on a given service day falls, as an instant.</summary>
    public static DateTimeOffset At(DateOnly serviceDate, int seconds) =>
        ServiceDayStart(serviceDate).AddSeconds(seconds);
}
