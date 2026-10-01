# TransportTracker

A Sydney transport hub with live departures and the chance each service is **Late**, based on the last 3 weeks of real data. Domain terms are defined in [CONTEXT.md](./CONTEXT.md), and key decisions are recorded in [docs/adr](./docs/adr).

## Roadmap

- **V1:** Sydney Trains only, delivered as a PWA website. No accounts.
- **V2:** Push notifications for Saved Trips.
- **Later:** Buses and light rail, plus a native SwiftUI iOS app on the same API.

## V1 scope

| Screen | What it shows |
|---|---|
| Station search + departures | Live departures with "Delayed X min" and the Late % for each service |
| Saved Trips | The rider's Origin → Destination favourites, the next direct trains, and the Late % for the current Time Band |
| Line stats | Overall Late % and Cancellation %, broken down by Station, Direction and Time Band |
| Alerts | Disruptions and delays affecting Saved Trips (in-app only) |

Saved Trips are stored in `localStorage`, so they stay on the rider's device and need no login.

## Stats rules

- **Late** means more than 2 minutes behind the Timetable at the rider's Station. The UI labels it clearly.
- Cancelled Trips and skipped Stations get their own Cancellation %. They're never counted as Late.
- **Stats Buckets** are Station + Direction + Time Band:
  - weekday AM peak (6:30–9:30)
  - weekday PM peak (15:00–19:00)
  - weekday off-peak
  - weekend
- A bucket needs at least 20 Observations before it shows a percentage. Below that, the UI shows "collecting data". The sample size is always displayed.
- Stats cover a rolling 3-week window. Raw Observations are kept for about 5 weeks, so the stats can be recomputed if a definition changes.
- Saved Trips match **direct** trains only (from the static Timetable). V1 doesn't handle transfers.

## Architecture

```
TfNSW GTFS-realtime ──(every 30s)──┐
TfNSW static GTFS ──(nightly)──────┤
                                   ▼
                  ASP.NET Core app (modular monolith)
                  ├── Poller: tracks Live Delays and finalises Observations
                  ├── Timetable importer: loads static GTFS, swaps it only if changed
                  ├── Stats: Late % / Cancellation % per Stats Bucket
                  └── API ──► React + TS PWA
                                   │
                                Postgres
```

- **Recording Observations:** each Trip is tracked at each Station. When the Station drops out of the realtime feed, the last Live Delay seen is saved as the Observation.
- **Polling:** every 30s, which comes to about 2.9k requests a day. TfNSW allows about 60k a day.
- **API key:** stays server-side only, in App Service config or Key Vault. The browser never calls TfNSW directly.
- **No Redis or Service Bus:** they can be added once load justifies them ([ADR 0001](./docs/adr/0001-modular-monolith.md)).

## Hosting

- **App Service B1 (Azure):** runs the API and poller. Always On is required for the poller.
- **Azure Database for PostgreSQL Flexible Server B1ms**
- **Vercel:** hosts the React PWA as static files. The API allows the Vercel domain through CORS ([ADR 0004](./docs/adr/0004-pwa-hosted-on-vercel.md)).

## Engineering

- **GitHub Actions:** build and test on every PR, and deploy to Azure on merge to `main`.
- **Tests:**
  - unit tests for Observation finalisation and the Late % calculation, run against recorded feed fixtures
  - integration tests against a real Postgres (Testcontainers)
- **Deferred:** monitoring. Before the stats are trusted, add at least an alert for when the poller stops writing Observations. A silent gap would skew the 3-week window.
- **Start the poller early:** TfNSW doesn't publish realtime history, so the stats can only build up from the day the poller starts.

## Local development

Prerequisites: .NET 10 SDK, and Docker Desktop (which needs WSL 2 on Windows).

```sh
docker compose up -d                      # local Postgres
dotnet user-secrets set "Tfnsw:ApiKey" "<key>" --project src/TransportTracker.Api
dotnet run --project src/TransportTracker.Api
```

On startup the API applies migrations and imports the static Timetable. After that it re-imports nightly at 03:00 Sydney time. It also polls the realtime feed every 30s and records Observations to the `observations` table. `GET /v1/health` shows the active Timetable and when the last poll succeeded.

```sh
dotnet test                               # integration tests start their own Postgres via Testcontainers
dotnet run --project tools/FeedProbe -- 60  # record 60 min of realtime feed into data/feed-samples/
dotnet tool restore && dotnet ef migrations add <Name> --project src/TransportTracker.Api -o Data/Migrations
```
