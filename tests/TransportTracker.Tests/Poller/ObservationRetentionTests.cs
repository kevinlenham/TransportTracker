using Microsoft.EntityFrameworkCore;
using TransportTracker.Api.Data;
using TransportTracker.Api.Poller;
using TransportTracker.Api.Stats;

namespace TransportTracker.Tests.Poller;

public class ObservationRetentionTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 9, 0, 0, TimeSpan.FromHours(11)); // AEDT

    [Fact]
    public async Task ServiceDaysOlderThanFiveWeeksAreDeleted()
    {
        await using var db = await postgres.CreateDbContextAsync();
        var today = new DateOnly(2026, 11, 10);
        Add(db, "OLD", today.AddDays(-36));
        Add(db, "OLDEST_KEPT", today.AddDays(-35));
        Add(db, "RECENT", today.AddDays(-1));
        await db.SaveChangesAsync();

        var deleted = await new ObservationRetention(db, new FixedTime(Now)).PurgeAsync(CancellationToken.None);

        Assert.Equal(1, deleted);
        Assert.Equal(["OLDEST_KEPT", "RECENT"], await db.Observations.OrderBy(o => o.ServiceDate).Select(o => o.TripId).ToListAsync());
    }

    [Fact]
    public void ObservationsOutliveTheStatsWindow() => Assert.True(ObservationRetention.KeepFor > LateStatsQuery.Window);

    private static void Add(AppDbContext db, string tripId, DateOnly serviceDate)
    {
        var at = new DateTimeOffset(serviceDate.ToDateTime(new TimeOnly(10, 0)), TimeSpan.Zero);
        db.Observations.Add(new Observation
        {
            ServiceDate = serviceDate, TripId = tripId, RouteId = "R", StopId = "2000323", StationId = "200060",
            ScheduledAt = at, DelaySeconds = 0, Status = ObservationStatus.Passed, RecordedAt = at,
        });
    }
}
