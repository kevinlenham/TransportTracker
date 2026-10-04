using TransportTracker.Api.Data;
using TransportTracker.Api.Poller;
using TransportTracker.Api.Stats;

namespace TransportTracker.Tests.Stats;

public class LateStatsQueryTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly TimeSpan Aest = TimeSpan.FromHours(10);
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, Aest); // Thursday

    [Fact]
    public async Task LatePercentCountsDelaysOverTwoMinutesAndLeavesCancellationsOut()
    {
        await using var db = await postgres.CreateDbContextAsync();
        var at = new DateTimeOffset(2026, 9, 29, 11, 0, 0, Aest); // Tuesday off-peak
        for (var i = 0; i < 16; i++) Add(db, $"ON{i}", at, 120); // Exactly 2 minutes isn't Late.
        for (var i = 0; i < 4; i++) Add(db, $"LATE{i}", at, 121);
        Add(db, "SKIP", at, null, ObservationStatus.Skipped);
        Add(db, "CANX", at, null, ObservationStatus.TripCancelled);
        await db.SaveChangesAsync();

        var stats = (await Query(db))[new StatsBucket("T8", 1, TimeBand.OffPeak)];

        Assert.Equal(new BucketStats(Observed: 20, Late: 4, Timetabled: 22, Cancelled: 2), stats);
        Assert.Equal(20.0, stats.LatePercent);
        Assert.Equal(9.1, stats.CancellationPercent);
    }

    [Fact]
    public async Task BelowTheMinimumSampleThereIsNoPercentage()
    {
        await using var db = await postgres.CreateDbContextAsync();
        for (var i = 0; i < LateStatsQuery.MinimumSample - 1; i++)
            Add(db, $"T{i}", new DateTimeOffset(2026, 9, 29, 11, 0, 0, Aest), 600);
        await db.SaveChangesAsync();

        var stats = (await Query(db))[new StatsBucket("T8", 1, TimeBand.OffPeak)];

        Assert.Equal(19, stats.Observed);
        Assert.Null(stats.LatePercent);
    }

    [Fact]
    public async Task ObservationsOlderThanThreeWeeksAreLeftOut()
    {
        await using var db = await postgres.CreateDbContextAsync();
        Add(db, "OLD", Now - LateStatsQuery.Window - TimeSpan.FromMinutes(1), 0);
        Add(db, "NEW", Now - LateStatsQuery.Window + TimeSpan.FromMinutes(1), 0);
        await db.SaveChangesAsync();

        var stats = Assert.Single(await Query(db)).Value;

        Assert.Equal(1, stats.Observed);
    }

    [Fact]
    public async Task SqlTimeBandsMatchTimeBandsOf()
    {
        await using var db = await postgres.CreateDbContextAsync();
        DateTimeOffset[] times =
        [
            new(2026, 9, 28, 6, 29, 59, Aest), new(2026, 9, 28, 6, 30, 0, Aest),
            new(2026, 9, 28, 9, 29, 59, Aest), new(2026, 9, 28, 9, 30, 0, Aest),
            new(2026, 9, 28, 14, 59, 59, Aest), new(2026, 9, 28, 15, 0, 0, Aest),
            new(2026, 9, 28, 18, 59, 59, Aest), new(2026, 9, 28, 19, 0, 0, Aest),
            new(2026, 9, 26, 8, 0, 0, Aest), new(2026, 9, 27, 23, 59, 0, Aest),
            new(2026, 9, 25, 22, 0, 0, TimeSpan.Zero), // 08:00 Saturday in Sydney
        ];
        // One Line per time, so each lands in its own bucket.
        for (var i = 0; i < times.Length; i++) Add(db, $"T{i}", times[i], 0, line: $"L{i}");
        await db.SaveChangesAsync();

        var stats = await Query(db);

        for (var i = 0; i < times.Length; i++)
            Assert.Contains(new StatsBucket($"L{i}", 1, TimeBands.Of(times[i])), (IDictionary<StatsBucket, BucketStats>)stats);
    }

    private static Task<IReadOnlyDictionary<StatsBucket, BucketStats>> Query(AppDbContext db) =>
        new LateStatsQuery(db, new FixedTime(Now)).ForStationAsync("200060", CancellationToken.None);

    private static void Add(AppDbContext db, string tripId, DateTimeOffset scheduledAt, int? delay,
        ObservationStatus status = ObservationStatus.Passed, string line = "T8") =>
        db.Observations.Add(new Observation
        {
            ServiceDate = DateOnly.FromDateTime(scheduledAt.DateTime),
            TripId = tripId,
            RouteId = "APS_1a",
            Line = line,
            DirectionId = 1,
            StopId = "2000323",
            StationId = "200060",
            ScheduledAt = scheduledAt.ToUniversalTime(),
            DelaySeconds = delay,
            Status = status,
            RecordedAt = scheduledAt.ToUniversalTime(),
        });
}
