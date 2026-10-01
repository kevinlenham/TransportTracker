# An Observation is the last Live Delay seen before a Station leaves the feed

GTFS-realtime only publishes predicted delays, and a Station disappears from a Trip's updates once the train has passed. We record an Observation from the last Live Delay seen for that Trip and Station before it disappears. The alternatives were working out arrival times from vehicle positions (noisy and complex) or storing every prediction on every poll (lots of redundant data). With 30s polling, the recorded Delay can be up to about one poll interval out of date. That's acceptable when "Late" is set at 2 minutes.

## Feed check (2026-10-01)

We ran `tools/FeedProbe` against the live Sydney Trains feed (`/v2/gtfs/realtime/sydneytrains`) for a short period. Passed Stations do drop out while the Trip stays in the feed, and none dropped out before their predicted time, so the approach holds. The poller still has to handle these cases:

- **Ghost Trips:** about 10% of Trips have Stations listed long after their predicted time. Some of these TripUpdates have a `timestamp` hours old. The poller ignores TripUpdates whose own `timestamp` is stale, and never records an Observation from them.
- **Missing `delay`:** some stop updates (mostly on `REPLACEMENT` Trips) give an absolute `time` and no `delay`. The Delay is then worked out as predicted time minus the Timetable time, which needs the static GTFS.
- **Trip ends:** when a Trip leaves the feed, its remaining Stations go with it. The final Station is finalised from its last Live Delay, the same as a Station dropping out.
- **Stop IDs are platforms** (for example `2000323` at Central), so Observations are mapped to their parent Station using `parent_station` in the static GTFS.
- The feed contains TripUpdates only, with no vehicle positions or alerts. Alerts come from a separate feed.
