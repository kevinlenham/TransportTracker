import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { Link } from 'react-router'
import type { ServiceAlert } from '../api'
import { LineBadge } from '../components/LineBadge'
import { Band, Content } from '../components/Page'
import { clockTime, stationLabel, statusText, statusTone } from '../format'
import { linesQuery } from '../queries'
import { tripKey } from '../savedTrips'
import { useTripAlerts } from '../tripAlerts'

export function Alerts() {
  const { filtered, alerts, problems } = useTripAlerts()
  const allLines = useQuery(linesQuery)
  const colors = new Map(allLines.data?.map((l) => [l.line, l.color]))

  return (
    <>
      <Band title="Alerts">
        <p className="band-text">
          {filtered
            ? 'Delays and cancellations on your saved trips, and service alerts for their stations and lines.'
            : 'Service alerts from Transport for NSW.'}
          {alerts.data?.fetchedAt && <span className="muted"> Updated {clockTime(alerts.data.fetchedAt)}.</span>}
        </p>
      </Band>

      <Content>
        {filtered && (
          <section aria-labelledby="your-trips">
            <h2 id="your-trips" className="section-title">
              Your trips
            </h2>
            {problems.length === 0 ? (
              <p className="hint">No delays or cancellations on the next trains for your trips.</p>
            ) : (
              <ul className="plain-list">
                {problems.map(({ trip, departure: d }) => (
                  <li key={`${tripKey(trip)}-${d.tripId}`}>
                    <LineBadge line={d.line} color={d.lineColor} />
                    <div>
                      <strong className={`tone-${statusTone(d)}`}>{statusText(d)}</strong>
                      <div>
                        {clockTime(d.scheduledAt)} {stationLabel(trip.fromName)} to {stationLabel(trip.toName)}
                        {d.headsign && <span className="muted"> ({d.headsign} train)</span>}
                      </div>
                    </div>
                  </li>
                ))}
              </ul>
            )}
          </section>
        )}

        <section aria-labelledby="service-alerts">
          <h2 id="service-alerts" className="section-title">
            Service alerts
          </h2>
          {!filtered && (
            <p className="hint">
              <Link to="/trips">Save a trip</Link> to see only the alerts that affect you.
            </p>
          )}
          {alerts.isPending && <p className="hint">Loading alerts…</p>}
          {alerts.isError && <p className="error">Couldn't load service alerts.</p>}
          {alerts.data?.alerts.length === 0 && <p className="hint">No current service alerts.</p>}
          {alerts.data && alerts.data.alerts.length > 0 && (
            <ul className="plain-list">
              {alerts.data.alerts.map((a) => (
                <AlertItem key={a.id} alert={a} colors={colors} />
              ))}
            </ul>
          )}
        </section>
      </Content>
    </>
  )
}

function AlertItem({ alert: a, colors }: { alert: ServiceAlert; colors: Map<string, string | null> }) {
  const [open, setOpen] = useState(false)
  const detailsId = `alert-${a.id}`

  return (
    <li>
      <div className="alert-lines">
        {a.networkWide ? <span className="muted">All lines</span> : a.lines.map((l) => <LineBadge key={l} line={l} color={colors.get(l) ?? null} />)}
      </div>
      <div className="alert-body">
        <div className="alert-header">{a.header}</div>
        {open && a.description && (
          <p className="alert-description" id={detailsId}>
            {a.description}
          </p>
        )}
        <div className="alert-actions">
          {a.description && (
            <button type="button" className="link-button" aria-expanded={open} aria-controls={detailsId} onClick={() => setOpen(!open)}>
              {open ? 'Hide details' : 'Details'}
            </button>
          )}
          {a.url && (
            <a href={a.url} target="_blank" rel="noreferrer">
              More on transportnsw.info
            </a>
          )}
        </div>
      </div>
    </li>
  )
}
