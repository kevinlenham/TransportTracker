# An Observation is the last Live Delay seen before a Station leaves the feed

GTFS-realtime only publishes predicted delays, and a Station disappears from a Trip's updates once the train has passed. We record an Observation from the last Live Delay seen for that Trip and Station before it disappears. The alternatives were working out arrival times from vehicle positions (noisy and complex) or storing every prediction on every poll (lots of redundant data). With 30s polling, the recorded Delay can be up to about one poll interval out of date. That's acceptable when "Late" is set at 2 minutes.
