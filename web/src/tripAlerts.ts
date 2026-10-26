import { useQueries, useQuery } from '@tanstack/react-query'
import { getAlerts, type Departure } from './api'
import { tripBoardQuery, useSavedTrips } from './savedTrips'

/** A train counts as a problem once it would count as Late (over 2 minutes behind), or doesn't run. */
const LATE_AFTER_SECONDS = 120

export const isProblem = (d: Departure) =>
  d.status === 'Cancelled' || d.status === 'Skipped' || (d.status === 'Live' && (d.delaySeconds ?? 0) > LATE_AFTER_SECONDS)

/** Active TfNSW alerts, only those affecting the Stations or Lines given (plus network-wide ones) if any are. */
export const alertsQuery = (filter: { stations?: string[]; lines?: string[] }) => ({
  queryKey: ['alerts', filter.stations ?? [], filter.lines ?? []],
  queryFn: ({ signal }: { signal: AbortSignal }) => getAlerts(filter, signal),
  refetchInterval: 60_000,
})

/**
 * What the Alerts page shows: problems with the next trains for each Saved Trip, and the service
 * alerts for their Stations and Lines (all alerts when there are no Saved Trips). `count` is both
 * together, for the nav. The header and the Alerts page share these queries' cache.
 */
export function useTripAlerts() {
  const { trips } = useSavedTrips()
  const boards = useQueries({ queries: trips.map(tripBoardQuery) })

  const filtered = trips.length > 0
  const stations = [...new Set(trips.flatMap((t) => [t.fromId, t.toId]))]
  const lines = [...new Set(boards.flatMap((b) => b.data?.departures.map((d) => d.line) ?? []).filter((l) => l !== null))]
  const alerts = useQuery(alertsQuery(filtered ? { stations, lines } : {}))

  const problems = trips.flatMap((trip, i) =>
    (boards[i]?.data?.departures ?? []).filter(isProblem).map((departure) => ({ trip, departure })),
  )

  return { filtered, alerts, problems, count: problems.length + (alerts.data?.alerts.length ?? 0) }
}
