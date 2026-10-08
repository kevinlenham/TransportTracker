using TransportTracker.Api.Data;
using TransportTracker.Api.Poller;
using TransportTracker.Api.Stations;
using TransportTracker.Api.Stats;
using TransportTracker.Api.Timetable;
using Route = TransportTracker.Api.Timetable.Route;

namespace TransportTracker.Tests.Stations;

public class DepartureBoardTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly TimeSpan Aest = TimeSpan.FromHours(10);
    private static readonly DateTimeOffset Now = At(10, 0); // Thursday 1 Oct 2026

    [Fact]
    public async Task TimetableIsOverlaidWithLiveDelaysInExpectedOrder()
    {
        await using var db = await SeededAsync();
        var live = Live(
            trips: new()
            {
                ["LIVE"] = [new LiveStop("LIVE", "2000323", 300, null, false)],
                ["PLAT"] = [new LiveStop("PLAT", "2000324", 0, null, false)],
                ["GONE"] = [new LiveStop("GONE", "2135238", 60, null, false)],
            },
            cancelled: ["CANX"]);

        var board = await Board(db, live);

        Assert.Equal("Central Station", board.Station.Name);
        // After today's trains the board carries on into the night and the next morning.
        Assert.Equal(["LIVE", "CANX", "PLAT", "SCHED", "NIGHT", "OLD"], board.Departures.Take(6).Select(d => d.TripId));

        var delayed = board.Departures[0];
        Assert.Equal((DepartureStatus.Live, 300, At(9, 58), At(10, 3)),
            (delayed.Status, delayed.DelaySeconds, delayed.ScheduledAt, delayed.ExpectedAt));
        Assert.Equal(("T8", "F99D1C", "Macarthur"), (delayed.Line, delayed.LineColor, delayed.Headsign));

        Assert.Equal(DepartureStatus.Cancelled, board.Departures[1].Status);
        Assert.Equal(("2000324", "Central Station Platform 4"), (board.Departures[2].PlatformId, board.Departures[2].PlatformName));
        Assert.Equal((DepartureStatus.Scheduled, (int?)null), (board.Departures[3].Status, board.Departures[3].DelaySeconds));
    }

    [Fact]
    public async Task DelayIsWorkedOutFromThePredictedTimeWhenTheFeedLeavesItOut()
    {
        await using var db = await SeededAsync();
        var predicted = At(10, 20).AddSeconds(90).ToUnixTimeSeconds();

        var board = await Board(db, Live(new() { ["SCHED"] = [new LiveStop("SCHED", "2000323", null, predicted, false)] }));

        var d = board.Departures.Single(d => d.TripId == "SCHED");
        Assert.Equal((90, At(10, 21).AddSeconds(30)), (d.DelaySeconds, d.ExpectedAt));
    }

    [Fact]
    public async Task EachDepartureCarriesTheLatePercentForItsStatsBucket()
    {
        await using var db = await SeededAsync();
        var lastWeek = At(11, 0).AddDays(-7); // Thursday off-peak, like the departures
        for (var i = 0; i < 20; i++)
        {
            db.Observations.Add(new Observation
            {
                ServiceDate = new DateOnly(2026, 9, 24), TripId = $"OBS{i}", RouteId = "APS_1a", Line = "T8", DirectionId = 1,
                StopId = "2000323", StationId = "200060", ScheduledAt = lastWeek.AddMinutes(i).ToUniversalTime(),
                DelaySeconds = i < 5 ? 300 : 0, Status = ObservationStatus.Passed, RecordedAt = lastWeek.ToUniversalTime(),
            });
        }
        await db.SaveChangesAsync();

        var board = await Board(db, live: null);

        Assert.All(board.Departures, d =>
            Assert.Equal(new LateChance(TimeBand.OffPeak, 25.0, 20, LateStatsQuery.MinimumSample), d.Late));
    }

    [Fact]
    public async Task WithoutAPollYetTheTimetableIsShownAsScheduled()
    {
        await using var db = await SeededAsync();

        var board = await Board(db, live: null);

        Assert.Equal(["CANX", "PLAT", "SCHED", "NIGHT"], board.Departures.Take(4).Select(d => d.TripId));
        Assert.All(board.Departures, d => Assert.Equal(DepartureStatus.Scheduled, d.Status));
        Assert.Null(board.FeedTime);
    }

    [Fact]
    public async Task TripPastMidnightIsFoundOnThePreviousServiceDay()
    {
        await using var db = await SeededAsync();

        var board = await new DepartureBoard(db, new PollerStatus(), new LateStatsQuery(db, new FixedTime(At(0, 30, day: 2))),
            new FixedTime(At(0, 30, day: 2))).GetAsync("200060", null, 20, null, CancellationToken.None);

        var d = board!.Departures[0];
        Assert.Equal(("NIGHT", At(0, 45, day: 2)), (d.TripId, d.ScheduledAt));
    }

    [Fact]
    public async Task OvernightTheBoardReachesTheFirstTrainsOfTheMorning()
    {
        await using var db = await SeededAsync();

        var board = await BoardAt(db, At(1, 30, day: 2), live: null, limit: 2);

        Assert.Equal([("OLD", At(9, 30, day: 2)), ("GONE", At(9, 55, day: 2))],
            board.Departures.Select(d => (d.TripId, d.ScheduledAt)));
    }

    [Fact]
    public async Task LaterPagesContinueWithoutSkippingOrRepeatingTrains()
    {
        await using var db = await SeededAsync();

        var first = await Board(db, live: null, limit: 2);
        var second = await Board(db, live: null, limit: 2, cursor: first.Later);
        var third = await Board(db, live: null, limit: 2, cursor: second.Later);

        Assert.Equal(["CANX", "PLAT"], first.Departures.Select(d => d.TripId));
        Assert.Equal(["SCHED", "NIGHT"], second.Departures.Select(d => d.TripId));
        Assert.Equal(["OLD", "GONE"], third.Departures.Select(d => d.TripId)); // Tomorrow's.
        Assert.Equal(At(9, 30, day: 2), third.Departures[0].ScheduledAt);
    }

    [Fact]
    public async Task EarlierPagesShowTrainsThatLeftWithTheirObservations()
    {
        await using var db = await SeededAsync();
        db.Observations.Add(new Observation
        {
            ServiceDate = new DateOnly(2026, 10, 1), TripId = "GONE", RouteId = "APS_1a", Line = "T8", DirectionId = 1,
            StopId = "2000324", StationId = "200060", ScheduledAt = At(9, 55).ToUniversalTime(), DelaySeconds = 240,
            Status = ObservationStatus.Passed, RecordedAt = At(10, 0).ToUniversalTime(),
        });
        await db.SaveChangesAsync();
        var live = Live(new()
        {
            ["GONE"] = [new LiveStop("GONE", "2135238", 240, null, false)], // Already past Central.
            ["LIVE"] = [new LiveStop("LIVE", "2000323", 300, null, false)], // Still to come, running late.
        });

        var first = await Board(db, live, limit: 2);
        var earlier = await Board(db, live, limit: 1, cursor: first.Earlier);
        var earliest = await Board(db, live, limit: 1, cursor: earlier.Earlier);

        // Most recent first: GONE left at 09:59 (4 minutes late), OLD at 09:30 with nothing recorded.
        var gone = Assert.Single(earlier.Departures);
        Assert.Equal(("GONE", DepartureStatus.Departed, 240, At(9, 59), "2000324"),
            (gone.TripId, gone.Status, gone.DelaySeconds, gone.ExpectedAt, gone.PlatformId));
        var old = Assert.Single(earliest.Departures);
        Assert.Equal(("OLD", DepartureStatus.Departed, (int?)null), (old.TripId, old.Status, old.DelaySeconds));
        Assert.Null(earlier.Later);
    }

    [Fact]
    public void CursorsRoundTripAndRejectGarbage()
    {
        var cursor = new BoardCursor(false, new BoardPosition(At(10, 0), "T1"));

        Assert.True(BoardCursor.TryParse(cursor.Encode(), out var parsed));
        Assert.Equal(cursor, parsed);
        Assert.False(BoardCursor.TryParse("not-a-cursor!", out _));
        Assert.False(BoardCursor.TryParse(new BoardCursor(false, null).Encode(), out _)); // Later needs a position.
    }

    [Fact]
    public async Task SavedTripOnlyIncludesDirectTripsThatStopAtTheDestinationLater()
    {
        await using var db = await SeededAsync();
        await AddSavedTripTripsAsync(db);

        var board = await Board(db, live: null, to: "213510");

        // OTHER doesn't go to Redfern, and REVERSE calls at Redfern before Central.
        Assert.Equal(["CANX", "PLAT", "SCHED", "NIGHT"], board.Departures.Take(4).Select(d => d.TripId));
        Assert.Equal("Redfern Station", board.Destination!.Name);
        var arrival = board.Departures[2].Arrival!;
        Assert.Equal(("2135238", "Redfern Station Platform 8", At(10, 25), At(10, 25)),
            (arrival.PlatformId, arrival.PlatformName, arrival.ScheduledAt, arrival.ExpectedAt));
    }

    [Fact]
    public async Task SavedTripArrivalUsesTheDestinationsLiveDelayOrCarriesTheDepartureDelay()
    {
        await using var db = await SeededAsync();
        var live = Live(new()
        {
            ["LIVE"] = [new LiveStop("LIVE", "2000323", 300, null, false), new LiveStop("LIVE", "2135238", 240, null, false)],
            ["PLAT"] = [new LiveStop("PLAT", "2000324", 120, null, false)],
            ["SCHED"] = [new LiveStop("SCHED", "2000323", 0, null, false), new LiveStop("SCHED", "2135238", null, null, true)],
        });

        var board = await Board(db, live, to: "213510");

        Assert.Equal(["LIVE", "CANX", "PLAT", "NIGHT"], board.Departures.Take(4).Select(d => d.TripId)); // SCHED skips Redfern.
        Assert.Equal(At(10, 3).AddMinutes(4), board.Departures[0].Arrival!.ExpectedAt);
        Assert.Equal(At(10, 22), board.Departures[2].Arrival!.ExpectedAt);
    }

    [Fact]
    public async Task UnknownStationIsNotFound()
    {
        await using var db = await SeededAsync();

        Assert.Null(await new DepartureBoard(db, new PollerStatus(), new LateStatsQuery(db, new FixedTime(Now)), new FixedTime(Now))
            .GetAsync("2000323", null, 20, null, CancellationToken.None)); // A platform, not a Station.
    }

    private static DateTimeOffset At(int hour, int minute, int day = 1) => new(2026, 10, day, hour, minute, 0, Aest);

    /// <summary>OTHER: Central 10:16 → Sydenham. REVERSE: Redfern 10:12 → Central 10:17.</summary>
    private static async Task AddSavedTripTripsAsync(AppDbContext db)
    {
        var id = db.TimetableImports.Single(i => i.IsActive).Id;
        db.Stops.AddRange(
            new Stop { ImportId = id, StopId = "214510", Name = "Sydenham Station", LocationType = 1 },
            new Stop { ImportId = id, StopId = "2145111", Name = "Sydenham Station Platform 1", ParentStation = "214510" });
        db.Trips.AddRange(
            new Trip { ImportId = id, TripId = "OTHER", RouteId = "APS_1a", ServiceId = "WD", DirectionId = 1 },
            new Trip { ImportId = id, TripId = "REVERSE", RouteId = "APS_1a", ServiceId = "WD", DirectionId = 0 });
        db.StopTimes.AddRange(
            new StopTime { ImportId = id, TripId = "OTHER", StopSequence = 1, StopId = "2000323", DepartureSeconds = 36960 },
            new StopTime { ImportId = id, TripId = "OTHER", StopSequence = 2, StopId = "2145111", ArrivalSeconds = 37260 },
            new StopTime { ImportId = id, TripId = "REVERSE", StopSequence = 1, StopId = "2135238", DepartureSeconds = 36720 },
            new StopTime { ImportId = id, TripId = "REVERSE", StopSequence = 2, StopId = "2000323", DepartureSeconds = 37020 });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    private static LiveFeed Live(Dictionary<string, LiveStop[]> trips, string[]? cancelled = null) => new(
        Now,
        trips.ToDictionary(t => t.Key, t => (IReadOnlyDictionary<string, LiveStop>)t.Value.ToDictionary(s => s.StopId)),
        (cancelled ?? []).ToHashSet());

    private static Task<Board> Board(AppDbContext db, LiveFeed? live, string? to = null, int limit = 20, string? cursor = null) =>
        BoardAt(db, Now, live, to, limit, cursor);

    private static async Task<Board> BoardAt(AppDbContext db, DateTimeOffset now, LiveFeed? live, string? to = null,
        int limit = 20, string? cursor = null)
    {
        Assert.True(BoardCursor.TryParse(cursor, out var parsed));
        var poller = new PollerStatus { Live = live };
        var board = await new DepartureBoard(db, poller, new LateStatsQuery(db, new FixedTime(now)), new FixedTime(now))
            .GetAsync("200060", to, limit, parsed, CancellationToken.None);
        return board!;
    }

    /// <summary>
    /// Departures from Central (platforms 3 and 4) on weekday service WD, with Now at 10:00:
    /// OLD 09:30, GONE 09:55, LIVE 09:58, CANX 10:10, PLAT 10:15, SCHED 10:20, TERM arrives 10:30 (set-down only).
    /// WKND 10:25 runs on weekends only. NIGHT leaves at 24:45, i.e. 00:45 the next morning.
    /// EMPTY 10:12 and NONREV 10:14 are non-passenger runs.
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
        db.Routes.Add(new Route { ImportId = id, RouteId = "APS_1a", ShortName = "T8", Color = "F99D1C" });
        db.ServiceCalendars.AddRange(
            new ServiceCalendar { ImportId = id, ServiceId = "WD", Monday = true, Tuesday = true, Wednesday = true, Thursday = true,
                Friday = true, StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 12, 31) },
            new ServiceCalendar { ImportId = id, ServiceId = "WE", Saturday = true, Sunday = true,
                StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 12, 31) });

        void Departs(string trip, int hour, int minute, string service = "WD", short pickupType = 0,
            string headsign = "Macarthur", string route = "APS_1a")
        {
            db.Trips.Add(new Trip { ImportId = id, TripId = trip, RouteId = route, ServiceId = service, Headsign = headsign, DirectionId = 1 });
            var seconds = hour * 3600 + minute * 60;
            db.StopTimes.Add(new StopTime { ImportId = id, TripId = trip, StopSequence = 1, StopId = "2000323",
                ArrivalSeconds = seconds, DepartureSeconds = seconds, PickupType = pickupType });
            db.StopTimes.Add(new StopTime { ImportId = id, TripId = trip, StopSequence = 2, StopId = "2135238",
                ArrivalSeconds = seconds + 300, DepartureSeconds = seconds + 300 });
        }

        Departs("OLD", 9, 30);
        Departs("GONE", 9, 55);
        Departs("LIVE", 9, 58);
        Departs("CANX", 10, 10);
        Departs("PLAT", 10, 15);
        Departs("SCHED", 10, 20);
        Departs("TERM", 10, 30, pickupType: 1);
        Departs("WKND", 10, 25, service: "WE");
        Departs("NIGHT", 24, 45);
        Departs("EMPTY", 10, 12, headsign: "Empty Train");
        Departs("NONREV", 10, 14, route: "RTTA_REV");
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return db;
    }
}
