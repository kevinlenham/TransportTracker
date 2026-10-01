namespace TransportTracker.Api.Timetable;

// Rows mirror the static GTFS records. Every row belongs to one TimetableImport, and only the
// active import is ever read, so a new Timetable can be loaded alongside the old one and swapped in.

public class TimetableImport
{
    public int Id { get; set; }
    public required string ContentHash { get; set; }
    public DateTimeOffset? SourceLastModified { get; set; }
    public DateTimeOffset ImportedAt { get; set; }
    public bool IsActive { get; set; }
    public long StopTimeCount { get; set; }
}

public class Stop
{
    public int ImportId { get; set; }
    public required string StopId { get; set; }
    public required string Name { get; set; }
    public double Lat { get; set; }
    public double Lon { get; set; }
    /// <summary>GTFS location_type: 0 = platform, 1 = Station.</summary>
    public short LocationType { get; set; }
    /// <summary>The Station a platform belongs to. Realtime stop IDs are platforms.</summary>
    public string? ParentStation { get; set; }
}

public class Route
{
    public int ImportId { get; set; }
    public required string RouteId { get; set; }
    public string? AgencyId { get; set; }
    /// <summary>The Line name, e.g. "T8".</summary>
    public string? ShortName { get; set; }
    public string? LongName { get; set; }
    public string? Description { get; set; }
    public string? Color { get; set; }
    public string? TextColor { get; set; }
}

public class ServiceCalendar
{
    public int ImportId { get; set; }
    public required string ServiceId { get; set; }
    public bool Monday { get; set; }
    public bool Tuesday { get; set; }
    public bool Wednesday { get; set; }
    public bool Thursday { get; set; }
    public bool Friday { get; set; }
    public bool Saturday { get; set; }
    public bool Sunday { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
}

public class Trip
{
    public int ImportId { get; set; }
    public required string TripId { get; set; }
    public required string RouteId { get; set; }
    public required string ServiceId { get; set; }
    public string? Headsign { get; set; }
    public short? DirectionId { get; set; }
}

public class StopTime
{
    public int ImportId { get; set; }
    public required string TripId { get; set; }
    public int StopSequence { get; set; }
    public required string StopId { get; set; }
    /// <summary>Seconds after midnight of the service day. Can exceed 24h for trips running past midnight.</summary>
    public int? ArrivalSeconds { get; set; }
    public int? DepartureSeconds { get; set; }
    public short PickupType { get; set; }
    public short DropOffType { get; set; }
}
