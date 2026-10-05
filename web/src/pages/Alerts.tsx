import { useQueries, useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'
import { getAlerts, getLines, type Departure, type ServiceAlert } from '../api'
import { LineBadge } from '../components/LineBadge'
import { clockTime, stationLabel, statusText } from '../format'
import { tripBoardQuery, tripKey, useSavedTrips } from '../savedTrips'

/** A train counts as a problem on the Alerts page once it would count as Late (over 2 minutes behind). */
const LATE_AFTER_SECONDS = 120

export function Alerts() {
  const { trips } = useSavedTrips()
  const boards = useQueries({ queries: trips.map(tripBoardQuery) })

  // Alerts for the Saved Trips' Stations and the Lines their trains run on, plus network-wide ones.
  const stations = [...new Set(trips.flatMap((t) => [t.fromId, t.toId]))]
  const lines = [...new Set(boards.flatMap((b) => b.data?.departures.map((d) => d.line) ?? []).filter((l) => l !== null))]
  const filtered = trips.length > 0
  const alerts = useQuery({
    queryKey: ['alerts', stations, lines],
    queryFn: ({ signal }) => getAlerts(filtered ? { stations, lines } : {}, signal),
    refetchInterval: 60_000,
  })

  const allLines = useQuery({ queryKey: ['lines'], queryFn: ({ signal }) => getLines(signal), staleTime: 5 * 60_000 })
  const colors = new Map(allLines.data?.map((l) => [l.line, l.color]))

  const problems = trips.flatMap((trip, i) =>
    (boards[i]?.data?.departures ?? []).filter(isProblem).map((d) => ({ trip, departure: d })),
  )

  return (
    <section>
      <h1 className="page-title">Alerts</h1>

      {filtered && (
        <>
          <h2 className="section-title">Your trips</h2>
          {problems.length === 0 ? (
            <p className="hint">No delays or cancellations on the next trains for your trips.</p>
          ) : (
            <ul className="alert-list">
              {problems.map(({ trip, departure: d }) => (
                <li key={`${tripKey(trip)}-${d.tripId}`} className="alert alert--trip">
                  <LineBadge line={d.line} color={d.lineColor} />
                  <div>
                    <strong>{statusText(d)}</strong>: {clockTime(d.scheduledAt)} {stationLabel(trip.fromName)} to{' '}
                    {stationLabel(trip.toName)}
                    {d.headsign && <span className="muted"> ({d.headsign} train)</span>}
                  </div>
                </li>
              ))}
            </ul>
          )}
        </>
      )}

      <h2 className="section-title">{filtered ? 'Service alerts for your trips' : 'All service alerts'}</h2>
      {!filtered && (
        <p className="hint">
          <Link to="/trips">Save a trip</Link> to see only the alerts that affect you.
        </p>
      )}
      {alerts.isPending && <p className="hint">Loading alerts…</p>}
      {alerts.isError && <p className="error">Couldn't load service alerts.</p>}
      {alerts.data?.alerts.length === 0 && <p className="hint">No current service alerts.</p>}
      {alerts.data && alerts.data.alerts.length > 0 && (
        <ul className="alert-list">
          {alerts.data.alerts.map((a) => (
            <AlertItem key={a.id} alert={a} colors={colors} />
          ))}
        </ul>
      )}
    </section>
  )
}

function isProblem(d: Departure) {
  return d.status === 'Cancelled' || d.status === 'Skipped' || (d.status === 'Live' && (d.delaySeconds ?? 0) > LATE_AFTER_SECONDS)
}

function AlertItem({ alert: a, colors }: { alert: ServiceAlert; colors: Map<string, string | null> }) {
  return (
    <li className="alert">
      <div className="alert-lines">
        {a.networkWide ? <span className="muted">All lines</span> : a.lines.map((l) => <LineBadge key={l} line={l} color={colors.get(l) ?? null} />)}
      </div>
      <div>
        <strong>{a.header}</strong>
        {a.description && (
          <details>
            <summary>Details</summary>
            <p className="alert-description">{a.description}</p>
          </details>
        )}
        {a.url && (
          <a href={a.url} target="_blank" rel="noreferrer" className="hint">
            More on transportnsw.info
          </a>
        )}
      </div>
    </li>
  )
}
