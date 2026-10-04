using TransitRealtime;
using StopRelationship = TransitRealtime.TripUpdate.Types.StopTimeUpdate.Types.ScheduleRelationship;
using TripRelationship = TransitRealtime.TripDescriptor.Types.ScheduleRelationship;

namespace TransportTracker.Api.Poller;

/// <summary>
/// The Live Delay the feed gives for a Trip at a stop. Once the stop leaves the feed, its last
/// LiveStop is what gets recorded as the Observation.
/// </summary>
public record LiveStop(string TripId, string StopId, int? DelaySeconds, long? PredictedTime, bool Skipped);

/// <summary>What changed between the previous poll and this one.</summary>
public record FeedChanges(DateTimeOffset FeedTime, IReadOnlyList<LiveStop> Passed, IReadOnlyList<string> CancelledTrips);

/// <summary>
/// The feed as of one poll: each trusted Trip's upcoming stops by stop ID, and the cancelled Trips.
/// A Trip that's in Trips but has no entry for a stop has already passed it. Ghost Trips are left out.
/// </summary>
public record LiveFeed(
    DateTimeOffset FeedTime,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, LiveStop>> Trips,
    IReadOnlySet<string> CancelledTrips);

/// <summary>
/// Follows each Trip's stops across polls and reports the ones that have dropped out (ADR 0002).
/// It only knows the feed, so it can't tell a passed Station from one that dropped out early.
/// The recorder checks that against the Timetable.
/// State is in memory only: after a restart, stops that drop out on the first poll are missed.
/// </summary>
public class DelayTracker
{
    /// <summary>A TripUpdate older than this, relative to the feed, is a ghost and is never trusted.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(5);

    private Dictionary<string, IReadOnlyDictionary<string, LiveStop>> _trips = [];
    private HashSet<string> _cancelled = [];

    public FeedChanges Update(FeedMessage feed)
    {
        var feedTime = (long)feed.Header.Timestamp;
        var passed = new List<LiveStop>();
        var newlyCancelled = new List<string>();
        var trips = new Dictionary<string, IReadOnlyDictionary<string, LiveStop>>();
        var cancelled = new HashSet<string>();
        var present = new HashSet<string>();

        foreach (var entity in feed.Entity)
        {
            if (entity.TripUpdate is not { } update || !update.Trip.HasTripId) continue;
            var tripId = update.Trip.TripId;
            present.Add(tripId);

            // A ghost: drop what we knew about the Trip, but don't treat it as ended either.
            if (update.HasTimestamp && feedTime - (long)update.Timestamp > StaleAfter.TotalSeconds) continue;

            if (update.Trip.ScheduleRelationship == TripRelationship.Canceled)
            {
                cancelled.Add(tripId);
                if (!_cancelled.Contains(tripId)) newlyCancelled.Add(tripId);
                continue;
            }

            var stops = new Dictionary<string, LiveStop>();
            foreach (var stu in update.StopTimeUpdate)
            {
                if (!stu.HasStopId) continue;
                var evt = stu.Arrival ?? stu.Departure;
                stops[stu.StopId] = new LiveStop(
                    tripId,
                    stu.StopId,
                    DelaySeconds: evt?.HasDelay == true ? evt.Delay : null,
                    PredictedTime: evt?.HasTime == true ? evt.Time : null,
                    Skipped: stu.ScheduleRelationship == StopRelationship.Skipped);
            }

            if (_trips.TryGetValue(tripId, out var before))
                passed.AddRange(before.Values.Where(s => !stops.ContainsKey(s.StopId)));
            trips[tripId] = stops;
        }

        // The Trip has left the feed, so all its remaining stops have gone with it.
        foreach (var (tripId, stops) in _trips)
        {
            if (!present.Contains(tripId)) passed.AddRange(stops.Values);
        }

        _trips = trips;
        _cancelled = cancelled;
        // Both collections are replaced, never changed, so the snapshot is safe to share across threads.
        Live = new LiveFeed(DateTimeOffset.FromUnixTimeSeconds(feedTime), trips, cancelled);
        return new FeedChanges(Live.FeedTime, passed, newlyCancelled);
    }

    /// <summary>The feed as of the latest Update, or null before the first.</summary>
    public LiveFeed? Live { get; private set; }
}
