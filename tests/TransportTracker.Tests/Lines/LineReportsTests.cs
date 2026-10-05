using TransportTracker.Api.Data;
using TransportTracker.Api.Lines;
using TransportTracker.Api.Poller;
using TransportTracker.Api.Stats;
using TransportTracker.Api.Timetable;
using Route = TransportTracker.Api.Timetable.Route;

namespace TransportTracker.Tests.Lines;

public class LineReportsTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly TimeSpan Aest = TimeSpan.FromHours(10);
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, Aest);
    private static readonly DateTimeOffset AmPeak = new(2026, 9, 29, 8, 0, 0, Aest); // Tuesday
    private static readonly DateTimeOffset OffPeak = new(2026, 9, 29, 11, 0, 0, Aest);

    [Fact]
    public async Task LinesAreListedInNumberOrderWithTheirOverallStats()
    {
        await using var db = await SeededAsync();
        Observe(db, "T8", "213510", 1, OffPeak, count: 20, late: 5);
        Observe(db, "T8", "200060", 0, AmPeak, count: 10, late: 0);
        await db.SaveChangesAsync();

        var lines = await Reports(db).AllAsync(CancellationToken.None);

        Assert.Equal(["T2", "T8", "BMT"], lines.Select(l => l.Line)); // Non-passenger routes are left out.
        var t8 = lines[1];
        Assert.Equal(("00954C", 30, 5), (t8.Color, t8.Stats.Observed, t8.Stats.Late));
        Assert.Equal(16.7, t8.Stats.LatePercent);
        Assert.Null(lines[0].Stats.LatePercent);
    }

    [Fact]
    public async Task LineIsBrokenDownByDirectionStationAndTimeBand()
    {
        await using var db = await SeededAsync();
        Observe(db, "T8", "213510", 1, OffPeak, count: 20, late: 5, headsign: "Macarthur");
        Observe(db, "T8", "213510", 1, AmPeak, count: 3, late: 1, headsign: "Campbelltown");
        Observe(db, "T8", "999999", 1, OffPeak, count: 1, late: 0); // Not on the longest Trip.
        await db.SaveChangesAsync();

        var report = (await Reports(db).GetAsync("T8", CancellationToken.None))!;

        Assert.Equal([(short?)0, 1], report.Directions.Select(d => d.DirectionId));
        var outbound = report.Directions[1];
        Assert.Equal(["Macarthur", "Campbelltown"], outbound.Headsigns);
        // Running order from the longest Trip, then other observed Stations.
        Assert.Equal(["200060", "213510", "214510", "999999"], outbound.Stations.Select(s => s.StationId));
        Assert.Equal("Redfern Station", outbound.Stations[1].Name);

        var redfern = outbound.Stations[1];
        Assert.Equal([TimeBand.AmPeak, TimeBand.PmPeak, TimeBand.OffPeak, TimeBand.Weekend], redfern.TimeBands.Select(b => b.TimeBand));
        Assert.Equal((3, 1), (redfern.TimeBands[0].Stats.Observed, redfern.TimeBands[0].Stats.Late));
        Assert.Equal(25.0, redfern.TimeBands[2].Stats.LatePercent);
        Assert.Equal((23, 6), (redfern.Stats.Observed, redfern.Stats.Late));
        Assert.Equal(24, outbound.Stats.Observed);
        Assert.Equal(24, report.Stats.Observed);
    }

    [Fact]
    public async Task UnknownLineIsNotFound()
    {
        await using var db = await SeededAsync();

        Assert.Null(await Reports(db).GetAsync("T99", CancellationToken.None));
        Assert.Null(await Reports(db).GetAsync("", CancellationToken.None));
    }

    private static LineReports Reports(AppDbContext db) => new(db, new LateStatsQuery(db, new FixedTime(Now)));

    private static void Observe(AppDbContext db, string line, string stationId, short direction, DateTimeOffset at, int count,
        int late, string headsign = "Macarthur")
    {
        for (var i = 0; i < count; i++)
        {
            var scheduledAt = at.AddMinutes(i).ToUniversalTime();
            db.Observations.Add(new Observation
            {
                ServiceDate = DateOnly.FromDateTime(at.DateTime), TripId = $"{line}-{stationId}-{at:HHmm}-{i}", RouteId = "R",
                Line = line, DirectionId = direction, Headsign = headsign, StopId = stationId, StationId = stationId,
                ScheduledAt = scheduledAt, DelaySeconds = i < late ? 300 : 0, Status = ObservationStatus.Passed,
                RecordedAt = scheduledAt,
            });
        }
    }

    /// <summary>
    /// T8 runs both ways. Its longest outbound Trip calls at Central, Redfern, Sydenham; a shorter one
    /// only at Redfern. T2 and BMT have no Observations. RTTA_REV is a non-passenger route.
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
            new Stop { ImportId = id, StopId = "213510", Name = "Redfern Station", LocationType = 1 },
            new Stop { ImportId = id, StopId = "2135238", Name = "Redfern Station Platform 8", ParentStation = "213510" },
            new Stop { ImportId = id, StopId = "214510", Name = "Sydenham Station", LocationType = 1 },
            new Stop { ImportId = id, StopId = "2145111", Name = "Sydenham Station Platform 1", ParentStation = "214510" });
        db.Routes.AddRange(
            new Route { ImportId = id, RouteId = "APS_1a", ShortName = "T8", Color = "00954C" },
            new Route { ImportId = id, RouteId = "APS_2a", ShortName = "T8" },
            new Route { ImportId = id, RouteId = "IWL_1a", ShortName = "T2", Color = "0098CD" },
            new Route { ImportId = id, RouteId = "BMT_1", ShortName = "BMT", Color = "F99D1C" },
            new Route { ImportId = id, RouteId = "RTTA_REV", ShortName = "NR" });

        void Trip(string trip, string route, short direction, params string[] platforms)
        {
            db.Trips.Add(new Trip { ImportId = id, TripId = trip, RouteId = route, ServiceId = "S", DirectionId = direction });
            for (var i = 0; i < platforms.Length; i++)
                db.StopTimes.Add(new StopTime { ImportId = id, TripId = trip, StopSequence = i + 1, StopId = platforms[i],
                    DepartureSeconds = 36000 + i * 300 });
        }

        Trip("LONG", "APS_1a", 1, "2000323", "2135238", "2145111");
        Trip("SHORT", "APS_1a", 1, "2135238");
        Trip("BACK", "APS_2a", 0, "2145111", "2135238", "2000323");
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return db;
    }
}
