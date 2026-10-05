import type { Departure } from '../api'
import { clockTime, lateText, minutesUntil, platformLabel, statusText, timeBandLabel } from '../format'
import { LineBadge } from './LineBadge'

/** One train on a departure board. For a Saved Trip it also shows when it arrives. */
export function DepartureRow({ departure: d, now }: { departure: Departure; now: number }) {
  const gone = d.status === 'Cancelled' || d.status === 'Skipped'
  const delayed = d.status === 'Live' && (d.delaySeconds ?? 0) >= 60
  const tone = gone ? 'bad' : delayed ? 'warn' : d.status === 'Live' ? 'good' : 'muted'

  return (
    <li className={`departure${gone ? ' departure--gone' : ''}`}>
      <LineBadge line={d.line} color={d.lineColor} />

      <div className="departure-main">
        <div className="departure-headsign">{d.headsign ?? 'Unknown destination'}</div>
        <div className="departure-meta">
          {[platformLabel(d.platformName), `${clockTime(d.scheduledAt)} timetabled`].filter(Boolean).join(' · ')}
        </div>
        {d.arrival && !gone && (
          <div className="departure-meta">
            Arrives {clockTime(d.arrival.expectedAt)}
            {platformLabel(d.arrival.platformName) && `, ${platformLabel(d.arrival.platformName)}`}
          </div>
        )}
        <div className={`departure-status tone-${tone}`}>{statusText(d)}</div>
        <div className="departure-late" title={`Based on the last 3 weeks, ${timeBandLabel(d.late.timeBand)}`}>
          {lateText(d.late)}
          {d.late.latePercent !== null && (
            <span className="muted"> · {timeBandLabel(d.late.timeBand)}, {d.late.sampleSize} trains</span>
          )}
        </div>
      </div>

      <div className="departure-when">
        {gone ? <span className="muted">—</span> : <strong>{minutesUntil(d.expectedAt, now)}</strong>}
        <span className="muted">{clockTime(d.expectedAt)}</span>
      </div>
    </li>
  )
}
