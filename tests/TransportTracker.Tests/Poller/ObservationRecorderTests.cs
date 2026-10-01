using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TransportTracker.Api.Data;
using TransportTracker.Api.Poller;
using TransportTracker.Api.Timetable;
using Route = TransportTracker.Api.Timetable.Route;

namespace TransportTracker.Tests.Poller;

public class ObservationRecorderTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly TimeSpan Aest = TimeSpan.FromHours(10);

    [Fact]
    public async Task PassedStopIsRecordedAtItsStationWithTheTimetableDetails()
    {
        await using var db = await SeededAsync();

        var inserted = await Record(db, At(10, 6), new PassedStop("T1", "2135238", 60, null, false));

        Assert.Equal(1, inserted);
        var o = await db.Observations.SingleAsync();
        Assert.Equal(ObservationStatus.Passed, o.Status);
        Assert.Equal(60, o.DelaySeconds);
        Assert.Equal("213510", o.StationId);
        Assert.Equal("2135238", o.StopId);
        Assert.Equal(At(10, 5), o.ScheduledAt);
        Assert.Equal(new DateOnly(2026, 10, 1), o.ServiceDate);
        Assert.Equal(("APS_1a", "T8", (short?)1, "Macarthur"), (o.RouteId, o.Line, o.DirectionId, o.Headsign));
    }

    [Fact]
    public async Task DelayIsWorkedOutFromThePredictedTimeWhenTheFeedLeavesItOut()
    {
        await using var db = await SeededAsync();

        await Record(db, At(10, 8), new PassedStop("T1", "2135238", null, At(10, 5).AddSeconds(150).ToUnixTimeSeconds(), false));

        Assert.Equal(150, (await db.Observations.SingleAsync()).DelaySeconds);
    }

    [Fact]
    public async Task StopThatDropsOutBeforeItsTimeIsNotRecorded()
    {
        await using var db = await SeededAsync();

        var inserted = await Record(db, At(9, 50), new PassedStop("T1", "2135238", 0, null, false));

        Assert.Equal(0, inserted);
    }

    [Fact]
    public async Task PlatformChangeStillMatchesTheTimetabledStation()
    {
        await using var db = await SeededAsync();

        // T1 is timetabled at Central platform 3, but called at platform 4.
        await Record(db, At(10, 1), new PassedStop("T1", "2000324", 45, null, false));

        var o = await db.Observations.SingleAsync();
        Assert.Equal(("200060", "2000324", At(10, 0)), (o.StationId, o.StopId, o.ScheduledAt));
    }

    [Fact]
    public async Task TripPastMidnightBelongsToThePreviousServiceDay()
    {
        await using var db = await SeededAsync();

        await Record(db, At(1, 0, day: 2), new PassedStop("LATE", "2000323", 0, null, false));

        var o = await db.Observations.SingleAsync();
        Assert.Equal(new DateOnly(2026, 10, 1), o.ServiceDate);
        Assert.Equal(At(0, 58, day: 2), o.ScheduledAt);
    }

    [Fact]
    public async Task SecondCallAtTheSameStationMatchesTheCloserTime()
    {
        await using var db = await SeededAsync();

        await Record(db, At(11, 1), new PassedStop("CC", "2000324", 30, null, false));

        Assert.Equal(At(11, 0), (await db.Observations.SingleAsync()).ScheduledAt);
    }

    [Fact]
    public async Task SkippedStationIsRecordedWithoutADelay()
    {
        await using var db = await SeededAsync();

        await Record(db, At(10, 6), new PassedStop("T1", "2135238", null, null, true));

        var o = await db.Observations.SingleAsync();
        Assert.Equal(ObservationStatus.Skipped, o.Status);
        Assert.Null(o.DelaySeconds);
    }

    [Fact]
    public async Task CancelledTripIsRecordedOnceForEveryTimetabledStation()
    {
        await using var db = await SeededAsync();
        var changes = new FeedChanges(At(8, 0), [], ["T1"]);

        var first = await Recorder(db).RecordAsync(changes, CancellationToken.None);
        var again = await Recorder(db).RecordAsync(changes, CancellationToken.None);

        Assert.Equal(2, first);
        Assert.Equal(0, again);
        var rows = await db.Observations.OrderBy(o => o.ScheduledAt).ToListAsync();
        Assert.All(rows, o => Assert.Equal(ObservationStatus.TripCancelled, o.Status));
        Assert.Equal(["200060", "213510"], rows.Select(o => o.StationId));
        Assert.Equal(At(10, 0), rows[0].ScheduledAt);
    }

    [Fact]
    public async Task NothingIsRecordedBeforeATimetableIsLoaded()
    {
        await using var db = await postgres.CreateDbContextAsync();

        var inserted = await Record(db, At(10, 6), new PassedStop("T1", "2135238", 60, null, false));

        Assert.Equal(0, inserted);
    }

    private static DateTimeOffset At(int hour, int minute, int day = 1) => new(2026, 10, day, hour, minute, 0, Aest);

    private static Task<int> Record(AppDbContext db, DateTimeOffset feedTime, PassedStop stop) =>
        Recorder(db).RecordAsync(new FeedChanges(feedTime, [stop], []), CancellationToken.None);

    private static ObservationRecorder Recorder(AppDbContext db) =>
        new(db, TimeProvider.System, NullLogger<ObservationRecorder>.Instance);

    /// <summary>
    /// T1: Central P3 10:00 → Redfern 10:05. LATE: Central P3 at 24:58.
    /// CC: a loop calling at Central P3 at 10:00 and Central P4 at 11:00.
    /// </summary>
    private async Task<AppDbContext> SeededAsync()
    {
        var db = await postgres.CreateDbContextAsync();
        var import = new TimetableImport { ContentHash = "test", ImportedAt = DateTimeOffset.UtcNow, IsActive = true };
        db.TimetableImports.Add(import);
        await db.SaveChangesAsync();
        var id = import.Id;

        db.Stops.AddRange(
            new Stop { ImportId = id, StopId = "200060", Name = "Central Station", LocationType = 1 },
            new Stop { ImportId = id, StopId = "2000323", Name = "Central Station Platform 3", ParentStation = "200060" },
            new Stop { ImportId = id, StopId = "2000324", Name = "Central Station Platform 4", ParentStation = "200060" },
            new Stop { ImportId = id, StopId = "213510", Name = "Redfern Station", LocationType = 1 },
            new Stop { ImportId = id, StopId = "2135238", Name = "Redfern Station Platform 8", ParentStation = "213510" });
        db.Routes.Add(new Route { ImportId = id, RouteId = "APS_1a", ShortName = "T8" });
        foreach (var trip in new[] { "T1", "LATE", "CC" })
            db.Trips.Add(new Trip { ImportId = id, TripId = trip, RouteId = "APS_1a", ServiceId = "S", Headsign = "Macarthur", DirectionId = 1 });
        db.StopTimes.AddRange(
            new StopTime { ImportId = id, TripId = "T1", StopSequence = 1, StopId = "2000323", ArrivalSeconds = 10 * 3600 },
            new StopTime { ImportId = id, TripId = "T1", StopSequence = 2, StopId = "2135238", ArrivalSeconds = 10 * 3600 + 300 },
            new StopTime { ImportId = id, TripId = "LATE", StopSequence = 1, StopId = "2000323", ArrivalSeconds = 24 * 3600 + 58 * 60 },
            new StopTime { ImportId = id, TripId = "CC", StopSequence = 1, StopId = "2000323", ArrivalSeconds = 10 * 3600 },
            new StopTime { ImportId = id, TripId = "CC", StopSequence = 5, StopId = "2000324", ArrivalSeconds = 11 * 3600 });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return db;
    }
}
