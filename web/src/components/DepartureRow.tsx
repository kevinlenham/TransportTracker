import type { Departure } from '../api'
import { clockTime, lateText, minutesUntil, platformLabel, statusText, timeBandLabel } from '../format'
import { LineBadge } from './LineBadge'

/**
 * One train on a departure board. For a Saved Trip it also shows when it arrives. A train that has
 * already gone (`past`) shows when it left instead of a countdown.
 */
export function DepartureRow({ departure: d, now, past = false, dataKey }: {
  departure: Departure
  now: number
  past?: boolean
  /** Identifies the row on the page, so the board can keep it in place when trains are added above. */
  dataKey?: string
}) {
  const gone = d.status === 'Cancelled' || d.status === 'Skipped'
  const late = (d.status === 'Live' || d.status === 'Departed') && (d.delaySeconds ?? 0) >= 60
  const tone = gone ? 'bad' : late ? 'warn' : d.status === 'Live' || (d.status === 'Departed' && d.delaySeconds !== null) ? 'good' : 'muted'

  return (
    <li className={`departure${gone ? ' departure--gone' : ''}${past ? ' departure--past' : ''}`} data-key={dataKey}>
      <LineBadge line={d.line} color={d.lineColor} />

      <div className="departure-main">
        <div className="departure-headsign">{d.headsign ?? 'Unknown destination'}</div>
        <div className="departure-meta">
          {[platformLabel(d.platformName), `${clockTime(d.scheduledAt)} timetabled`].filter(Boolean).join(' · ')}
        </div>
        {d.arrival && !gone && (
          <div className="departure-meta">
            {past ? 'Arrived about' : 'Arrives'} {clockTime(d.arrival.expectedAt)}
            {platformLabel(d.arrival.platformName) && `, ${platformLabel(d.arrival.platformName)}`}
          </div>
        )}
        <div className={`departure-status tone-${tone}`}>
          {statusText(d)}
          {d.status === 'Departed' && d.delaySeconds === null && <span className="muted"> · no live data recorded</span>}
        </div>
        {!past && (
          <div className="departure-late" title={`Based on the last 3 weeks, ${timeBandLabel(d.late.timeBand)}`}>
            {lateText(d.late)}
            {d.late.latePercent !== null && (
              <span className="muted"> · {timeBandLabel(d.late.timeBand)}, {d.late.sampleSize} trains</span>
            )}
          </div>
        )}
      </div>

      <div className="departure-when">
        {past ? (
          <span className="muted">{d.status === 'Departed' ? 'Left' : ''}</span>
        ) : gone ? (
          <span className="muted">—</span>
        ) : (
          <strong>{minutesUntil(d.expectedAt, now)}</strong>
        )}
        <span className="muted">{clockTime(d.expectedAt)}</span>
      </div>
    </li>
  )
}
