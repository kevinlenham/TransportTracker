using TransitRealtime;
using StopRelationship = TransitRealtime.TripUpdate.Types.StopTimeUpdate.Types.ScheduleRelationship;
using TripRelationship = TransitRealtime.TripDescriptor.Types.ScheduleRelationship;

namespace TransportTracker.Api.Poller;

/// <summary>The last Live Delay seen for a Trip at a stop before the stop left the feed.</summary>
public record PassedStop(string TripId, string StopId, int? DelaySeconds, long? PredictedTime, bool Skipped);

/// <summary>What changed between the previous poll and this one.</summary>
public record FeedChanges(DateTimeOffset FeedTime, IReadOnlyList<PassedStop> Passed, IReadOnlyList<string> CancelledTrips);

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

    private Dictionary<string, Dictionary<string, PassedStop>> _trips = [];
    private HashSet<string> _cancelled = [];

    public FeedChanges Update(FeedMessage feed)
    {
        var feedTime = (long)feed.Header.Timestamp;
        var passed = new List<PassedStop>();
        var newlyCancelled = new List<string>();
        var trips = new Dictionary<string, Dictionary<string, PassedStop>>();
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

            var stops = new Dictionary<string, PassedStop>();
            foreach (var stu in update.StopTimeUpdate)
            {
                if (!stu.HasStopId) continue;
                var evt = stu.Arrival ?? stu.Departure;
                stops[stu.StopId] = new PassedStop(
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
        return new FeedChanges(DateTimeOffset.FromUnixTimeSeconds(feedTime), passed, newlyCancelled);
    }
}
