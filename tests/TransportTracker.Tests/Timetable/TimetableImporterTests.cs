using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TransportTracker.Api.Timetable;

namespace TransportTracker.Tests.Timetable;

public class TimetableImporterTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    // A cut-down copy of the real Sydney Trains feed: quoted fields, a Station with a platform,
    // a Trip past midnight, and an empty optional field.
    private static Dictionary<string, string> Feed(string headsign = "Macarthur") => new()
    {
        ["stops.txt"] = """
            "stop_id","stop_code","stop_name","stop_desc","stop_lat","stop_lon","zone_id","stop_url","location_type","parent_station","stop_timezone","wheelchair_boarding"
            "200060","200060","Central Station","","-33.883881632","151.205829312","","","1","","","1"
            "2000323","2000323","Central Station Platform 3","","-33.882771","151.206062","","","0","200060","","0"
            "2135238","2135238","Redfern Station Platform 8","","-33.8916","151.1988","","","0","213510","","0"
            """,
        ["routes.txt"] = """
            "route_id","agency_id","route_short_name","route_long_name","route_desc","route_type","route_url","route_color","route_text_color"
            "APS_1a","SydneyTrains","T8","City Circle to Macarthur via Airport","T8 Airport & South Line","2","","00954C","FFFFFF"
            """,
        ["calendar.txt"] = """
            "service_id","monday","tuesday","wednesday","thursday","friday","saturday","sunday","start_date","end_date"
            "1309.166.100","1","0","0","1","1","0","0","20261001","20261002"
            """,
        ["trips.txt"] = $"""
            "route_id","service_id","trip_id","trip_headsign","trip_short_name","direction_id","block_id","shape_id","wheelchair_accessible","vehicle_category_id"
            "APS_1a","1309.166.100","T1.1309.166.100","{headsign}","","1","","APS_1a","0","B8"
            """,
        ["stop_times.txt"] = """
            "trip_id","arrival_time","departure_time","stop_id","stop_sequence","stop_headsign","pickup_type","drop_off_type","shape_dist_traveled"
            "T1.1309.166.100","24:58:00","24:59:00","2000323","1","","0","0",""
            "T1.1309.166.100","25:02:30","","2135238","2","","","1",""
            """,
    };

    [Fact]
    public async Task FirstImportLoadsAndActivatesTheTimetable()
    {
        await using var db = await postgres.CreateDbContextAsync();

        var result = await Importer(db, Feed()).ImportAsync(CancellationToken.None);

        Assert.Equal(ImportOutcome.Imported, result.Outcome);
        Assert.Equal(2, result.StopTimeCount);

        var import = await db.TimetableImports.SingleAsync();
        Assert.True(import.IsActive);
        Assert.Equal(2, import.StopTimeCount);

        var platform = await db.Stops.SingleAsync(s => s.StopId == "2000323");
        Assert.Equal("200060", platform.ParentStation);
        Assert.Null((await db.Stops.SingleAsync(s => s.StopId == "200060")).ParentStation);

        Assert.Equal("T8", (await db.Routes.SingleAsync()).ShortName);
        var calendar = await db.ServiceCalendars.SingleAsync();
        Assert.True(calendar.Monday);
        Assert.False(calendar.Tuesday);
        Assert.Equal(new DateOnly(2026, 10, 1), calendar.StartDate);
        Assert.Equal((short)1, (await db.Trips.SingleAsync()).DirectionId);

        var stopTimes = await db.StopTimes.OrderBy(s => s.StopSequence).ToListAsync();
        Assert.Equal(24 * 3600 + 58 * 60, stopTimes[0].ArrivalSeconds);
        Assert.Equal(25 * 3600 + 2 * 60 + 30, stopTimes[1].ArrivalSeconds);
        Assert.Null(stopTimes[1].DepartureSeconds);
        Assert.Equal((short)0, stopTimes[1].PickupType);
        Assert.Equal((short)1, stopTimes[1].DropOffType);
    }

    [Fact]
    public async Task UnchangedFeedIsNotReimported()
    {
        await using var db = await postgres.CreateDbContextAsync();
        var first = await Importer(db, Feed()).ImportAsync(CancellationToken.None);

        var second = await Importer(db, Feed()).ImportAsync(CancellationToken.None);

        Assert.Equal(ImportOutcome.Unchanged, second.Outcome);
        Assert.Equal(first.ImportId, second.ImportId);
        Assert.Equal(1, await db.TimetableImports.CountAsync());
        Assert.Equal(2, await db.StopTimes.CountAsync());
    }

    [Fact]
    public async Task ChangedFeedReplacesThePreviousTimetable()
    {
        await using var db = await postgres.CreateDbContextAsync();
        var first = await Importer(db, Feed()).ImportAsync(CancellationToken.None);

        var second = await Importer(db, Feed(headsign: "Leppington")).ImportAsync(CancellationToken.None);

        Assert.Equal(ImportOutcome.Imported, second.Outcome);
        Assert.NotEqual(first.ImportId, second.ImportId);
        var import = await db.TimetableImports.AsNoTracking().SingleAsync();
        Assert.Equal(second.ImportId, import.Id);
        Assert.True(import.IsActive);
        var trip = await db.Trips.AsNoTracking().SingleAsync();
        Assert.Equal("Leppington", trip.Headsign);
        Assert.Equal(2, await db.StopTimes.CountAsync(s => s.ImportId == second.ImportId));
        Assert.Equal(0, await db.StopTimes.CountAsync(s => s.ImportId != second.ImportId));
    }

    [Fact]
    public async Task FailedImportLeavesThePreviousTimetableActive()
    {
        await using var db = await postgres.CreateDbContextAsync();
        var first = await Importer(db, Feed()).ImportAsync(CancellationToken.None);

        var broken = Feed(headsign: "Leppington");
        broken["stop_times.txt"] = """
            "trip_id","arrival_time","departure_time","stop_id","stop_sequence"
            "T1.1309.166.100","not-a-time","","2000323","1"
            """;
        await Assert.ThrowsAnyAsync<Exception>(() => Importer(db, broken).ImportAsync(CancellationToken.None));

        var active = await db.TimetableImports.AsNoTracking().SingleAsync(i => i.IsActive);
        Assert.Equal(first.ImportId, active.Id);
        Assert.Equal("Macarthur", (await db.Trips.AsNoTracking().SingleAsync(t => t.ImportId == active.Id)).Headsign);
    }

    private static TimetableImporter Importer(Api.Data.AppDbContext db, Dictionary<string, string> files) =>
        new(db, new FakeGtfsSource(files), TimeProvider.System, NullLogger<TimetableImporter>.Instance);

    private class FakeGtfsSource(Dictionary<string, string> files) : IGtfsSource
    {
        public Task<GtfsDownload> DownloadAsync(CancellationToken ct)
        {
            var stream = new MemoryStream();
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var (name, content) in files)
                {
                    using var writer = new StreamWriter(zip.CreateEntry(name).Open(), Encoding.UTF8);
                    writer.Write(content);
                }
            }
            stream.Position = 0;
            return Task.FromResult(new GtfsDownload(stream, null));
        }
    }
}
