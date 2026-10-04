using TransitRealtime;
using TransportTracker.Api.Poller;
using StopRelationship = TransitRealtime.TripUpdate.Types.StopTimeUpdate.Types.ScheduleRelationship;
using TripRelationship = TransitRealtime.TripDescriptor.Types.ScheduleRelationship;

namespace TransportTracker.Tests.Poller;

public class DelayTrackerTests
{
    private const long T0 = 1_790_812_800; // 10:00 AEST, 1 Oct 2026

    [Fact]
    public void FirstPollReportsNothing()
    {
        var changes = new DelayTracker().Update(Feed(T0, Trip("T1", T0, Stop("A", delay: 30))));

        Assert.Empty(changes.Passed);
        Assert.Empty(changes.CancelledTrips);
    }

    [Fact]
    public void StationThatDropsOutIsReportedWithItsLastDelay()
    {
        var tracker = new DelayTracker();
        tracker.Update(Feed(T0, Trip("T1", T0, Stop("A", delay: 10), Stop("B", delay: 40))));
        tracker.Update(Feed(T0 + 30, Trip("T1", T0 + 30, Stop("A", delay: 30), Stop("B", delay: 45))));

        var changes = tracker.Update(Feed(T0 + 60, Trip("T1", T0 + 60, Stop("B", delay: 50))));

        var passed = Assert.Single(changes.Passed);
        Assert.Equal(new LiveStop("T1", "A", 30, null, false), passed);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(T0 + 60), changes.FeedTime);
    }

    [Fact]
    public void TripLeavingTheFeedReportsAllItsRemainingStations()
    {
        var tracker = new DelayTracker();
        tracker.Update(Feed(T0, Trip("T1", T0, Stop("A", delay: 10), Stop("B", delay: 20)), Trip("T2", T0, Stop("C"))));

        var changes = tracker.Update(Feed(T0 + 30, Trip("T2", T0 + 30, Stop("C"))));

        Assert.Equal(["A", "B"], changes.Passed.Select(p => p.StopId).Order());
        Assert.All(changes.Passed, p => Assert.Equal("T1", p.TripId));
    }

    [Fact]
    public void StaleTripUpdateIsForgottenRatherThanRecorded()
    {
        var tracker = new DelayTracker();
        tracker.Update(Feed(T0, Trip("T1", T0, Stop("A", delay: 10), Stop("B", delay: 20))));

        // A ghost keeps listing Stations long after they were due. Its last fresh state isn't trusted either.
        var stale = T0 + 30 - (long)DelayTracker.StaleAfter.TotalSeconds - 1;
        var ghost = tracker.Update(Feed(T0 + 30, Trip("T1", stale, Stop("B", delay: 20))));
        var gone = tracker.Update(Feed(T0 + 60));

        Assert.Empty(ghost.Passed);
        Assert.Empty(gone.Passed);
    }

    [Fact]
    public void CancelledTripIsReportedOnceWhileItStaysInTheFeed()
    {
        var tracker = new DelayTracker();
        var cancelled = Trip("T1", T0, TripRelationship.Canceled);

        var first = tracker.Update(Feed(T0, cancelled));
        var second = tracker.Update(Feed(T0 + 30, cancelled));

        Assert.Equal(["T1"], first.CancelledTrips);
        Assert.Empty(second.CancelledTrips);
        Assert.Empty(second.Passed);
    }

    [Fact]
    public void SkippedStationAndMissingDelayAreCarriedThrough()
    {
        var tracker = new DelayTracker();
        tracker.Update(Feed(T0, Trip("T1", T0, Stop("A", skipped: true), Stop("B", time: T0 + 20), Stop("C"))));

        var changes = tracker.Update(Feed(T0 + 30, Trip("T1", T0 + 30, Stop("C"))));

        Assert.Contains(new LiveStop("T1", "A", null, null, true), changes.Passed);
        Assert.Contains(new LiveStop("T1", "B", null, T0 + 20, false), changes.Passed);
    }

    private static FeedMessage Feed(long timestamp, params TripUpdate[] trips)
    {
        var feed = new FeedMessage { Header = new FeedHeader { GtfsRealtimeVersion = "2.0", Timestamp = (ulong)timestamp } };
        feed.Entity.AddRange(trips.Select(t => new FeedEntity { Id = t.Trip.TripId, TripUpdate = t }));
        return feed;
    }

    private static TripUpdate Trip(string tripId, long timestamp, params TripUpdate.Types.StopTimeUpdate[] stops) =>
        Trip(tripId, timestamp, TripRelationship.Scheduled, stops);

    private static TripUpdate Trip(string tripId, long timestamp, TripRelationship relationship,
        params TripUpdate.Types.StopTimeUpdate[] stops)
    {
        var update = new TripUpdate
        {
            Trip = new TripDescriptor { TripId = tripId, ScheduleRelationship = relationship },
            Timestamp = (ulong)timestamp,
        };
        update.StopTimeUpdate.AddRange(stops);
        return update;
    }

    private static TripUpdate.Types.StopTimeUpdate Stop(string stopId, int? delay = null, long? time = null, bool skipped = false)
    {
        var stu = new TripUpdate.Types.StopTimeUpdate
        {
            StopId = stopId,
            ScheduleRelationship = skipped ? StopRelationship.Skipped : StopRelationship.Scheduled,
        };
        if (delay is not null || time is not null)
        {
            stu.Arrival = new TripUpdate.Types.StopTimeEvent();
            if (delay is { } d) stu.Arrival.Delay = d;
            if (time is { } t) stu.Arrival.Time = t;
        }
        return stu;
    }
}
