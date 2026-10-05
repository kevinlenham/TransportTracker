using TransportTracker.Api.Timetable;

namespace TransportTracker.Api.Stats;

public enum TimeBand { AmPeak, PmPeak, OffPeak, Weekend }

public static class TimeBands
{
    // Kept in step with the observation_stats view, which buckets Observations the same way in SQL.
    public static readonly TimeOnly AmPeakStart = new(6, 30), AmPeakEnd = new(9, 30);
    public static readonly TimeOnly PmPeakStart = new(15, 0), PmPeakEnd = new(19, 0);

    /// <summary>The Time Band a timetabled time falls in, by Sydney local time. Peaks include their start, not their end.</summary>
    public static TimeBand Of(DateTimeOffset scheduledAt)
    {
        var local = TimeZoneInfo.ConvertTime(scheduledAt, GtfsTime.Sydney);
        if (local.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return TimeBand.Weekend;

        var time = TimeOnly.FromDateTime(local.DateTime);
        if (time >= AmPeakStart && time < AmPeakEnd) return TimeBand.AmPeak;
        if (time >= PmPeakStart && time < PmPeakEnd) return TimeBand.PmPeak;
        return TimeBand.OffPeak;
    }
}
