namespace TransportTracker.Api.Poller;

public enum ObservationStatus
{
    /// <summary>The train passed the Station. DelaySeconds holds its final Delay.</summary>
    Passed,
    /// <summary>The Trip ran but skipped this Station. Counts as Cancelled.</summary>
    Skipped,
    /// <summary>The whole Trip was cancelled. One row per timetabled Station.</summary>
    TripCancelled,
}

/// <summary>
/// The final recorded Delay for one Trip at one Station (ADR 0002). Rows outlive the Timetable
/// they were matched against, so everything the stats need is copied onto the row.
/// </summary>
public class Observation
{
    public long Id { get; set; }
    /// <summary>The Timetable day the Trip belongs to. Trips past midnight belong to the day before.</summary>
    public DateOnly ServiceDate { get; set; }
    public required string TripId { get; set; }
    public required string RouteId { get; set; }
    /// <summary>The Line, e.g. "T8".</summary>
    public string? Line { get; set; }
    public short? DirectionId { get; set; }
    /// <summary>The terminus the Trip is heading to, which names its Direction.</summary>
    public string? Headsign { get; set; }
    /// <summary>The platform, as the feed (or for a cancelled Trip, the Timetable) gave it.</summary>
    public required string StopId { get; set; }
    public required string StationId { get; set; }
    public DateTimeOffset ScheduledAt { get; set; }
    /// <summary>Null unless the status is Passed.</summary>
    public int? DelaySeconds { get; set; }
    public ObservationStatus Status { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
}
