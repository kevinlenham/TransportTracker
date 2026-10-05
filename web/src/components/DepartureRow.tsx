import type { Departure } from '../api'
import { clockTime, lateText, minutesUntil, platformLabel, statusText, textColorOn, timeBandLabel } from '../format'

export function DepartureRow({ departure: d, now }: { departure: Departure; now: number }) {
  const gone = d.status === 'Cancelled' || d.status === 'Skipped'
  const delayed = d.status === 'Live' && (d.delaySeconds ?? 0) >= 60
  const tone = gone ? 'bad' : delayed ? 'warn' : d.status === 'Live' ? 'good' : 'muted'
  const badgeColor = d.lineColor ?? '888888'

  return (
    <li className={`departure${gone ? ' departure--gone' : ''}`}>
      <span className="line-badge" style={{ background: `#${badgeColor}`, color: textColorOn(badgeColor) }}>
        {d.line ?? '?'}
      </span>

      <div className="departure-main">
        <div className="departure-headsign">{d.headsign ?? 'Unknown destination'}</div>
        <div className="departure-meta">
          {[platformLabel(d.platformName), `${clockTime(d.scheduledAt)} timetabled`].filter(Boolean).join(' · ')}
        </div>
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
