# TransportTracker

A Sydney (NSW) public transport hub showing live departures and how likely a service is to be late, based on recent history. V1 covers Sydney Trains only.

## Network

**Line**:
A named train route such as T2 or T8, as shown on the network map.
_Avoid_: Route (reserve for the GTFS record), service line

**Station**:
A place where passengers board trains, made up of one or more platforms.
_Avoid_: Stop (reserve for the GTFS record), platform

**Direction**:
Which way a Line runs through a Station, named by the terminus it's heading towards.
_Avoid_: Bound, heading

**Trip**:
One scheduled run of a train along a Line on a given day.
_Avoid_: Service, run, journey

**Timetable**:
The scheduled Trips and Station times, as TfNSW publishes them.
_Avoid_: Schedule, static data

## Punctuality

**Delay**:
How far behind (or ahead of) the Timetable a Trip is at a Station, in whole seconds.

**Live Delay**:
The current predicted Delay for a Trip that hasn't reached a Station yet. It's shown as "Delayed X min".
_Avoid_: ETA, prediction

**Observation**:
The final recorded Delay for one Trip at one Station, captured once the train has passed.
_Avoid_: Data point, sample, record

**Late**:
An Observation with a Delay of more than 2 minutes.
_Avoid_: Delayed (that's Live Delay), not on time

**Cancelled**:
A Trip that didn't run, or skipped a Station it was timetabled to stop at. It's never counted as Late.

**Late %**:
The share of Observations in a Stats Bucket that were Late over the last 3 weeks. Cancelled Trips are excluded from it.
_Avoid_: Delay chance, reliability score

**Cancellation %**:
The share of timetabled Trips in a Stats Bucket that were Cancelled over the last 3 weeks.

**Stats Bucket**:
The grouping a Late % is calculated over: Station + Direction + Time Band.

**Time Band**:
A weekday AM peak (6:30–9:30), weekday PM peak (15:00–19:00), weekday off-peak, or weekend period.
_Avoid_: Time slot, period

**Minimum Sample**:
The 20 Observations a Stats Bucket needs before it shows a Late %. Below that, it shows "collecting data".

## Rider

**Saved Trip**:
A rider's favourite Origin → Destination pair of Stations, served by any direct Trip. It's stored on the rider's device only.
_Avoid_: Favourite line, bookmark, Trip (a Trip is one train run)

**Alert**:
A disruption or Live Delay that affects one of the rider's Saved Trips, shown in the app while it's open.
_Avoid_: Notification (reserved for V2 push)
