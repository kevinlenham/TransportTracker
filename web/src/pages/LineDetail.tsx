import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { Link, useParams } from 'react-router'
import { getLine, NotFoundError, type Stats } from '../api'
import { LineBadge } from '../components/LineBadge'
import { percentText, stationLabel, timeBandLabel } from '../format'

export function LineDetail() {
  const { line = '' } = useParams()
  const [directionIndex, setDirectionIndex] = useState(0)
  const report = useQuery({
    queryKey: ['line', line],
    queryFn: ({ signal }) => getLine(line, signal),
    staleTime: 5 * 60_000,
    retry: (count, error) => !(error instanceof NotFoundError) && count < 3,
  })

  if (report.isPending) return <p className="hint">Loading {line}…</p>
  if (report.isError) {
    return (
      <p className="error">
        {report.error instanceof NotFoundError ? `There's no line called ${line}.` : "Couldn't load this line."}{' '}
        <Link to="/lines">All lines</Link>
      </p>
    )
  }

  const { color, stats, directions } = report.data
  const direction = directions[Math.min(directionIndex, directions.length - 1)]

  return (
    <section>
      <Link to="/lines" className="back">
        ← All lines
      </Link>
      <h1 className="page-title">
        <LineBadge line={line} color={color} /> {summary(stats)}
      </h1>

      {directions.length > 1 && (
        <div className="tabs" role="tablist" aria-label="Direction">
          {directions.map((d, i) => (
            <button
              key={d.directionId ?? i}
              role="tab"
              aria-selected={d === direction}
              className={d === direction ? 'tab tab--active' : 'tab'}
              onClick={() => setDirectionIndex(i)}
            >
              {d.headsigns.length ? `Towards ${d.headsigns.slice(0, 2).join(' / ')}` : `Direction ${i + 1}`}
            </button>
          ))}
        </div>
      )}

      {direction && (
        <div className="table-scroll">
          <table className="stats-table">
            <thead>
              <tr>
                <th scope="col">Station</th>
                {direction.stations[0]?.timeBands.map((b) => (
                  <th scope="col" key={b.timeBand}>
                    {shortBand[b.timeBand]}
                  </th>
                ))}
                <th scope="col">All</th>
              </tr>
            </thead>
            <tbody>
              {direction.stations.map((s) => (
                <tr key={s.stationId}>
                  <th scope="row">
                    <Link to={`/stations/${s.stationId}`}>{stationLabel(s.name)}</Link>
                  </th>
                  {s.timeBands.map((b) => (
                    <td key={b.timeBand} title={`${timeBandLabel(b.timeBand)}: ${b.stats.observed} trains`}>
                      {percentText(b.stats.latePercent)}
                    </td>
                  ))}
                  <td title={`${s.stats.observed} trains`}>
                    <strong>{percentText(s.stats.latePercent)}</strong>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <p className="footnote">
        Late % per station over the last 3 weeks: weekday AM peak (6:30–9:30), PM peak (3–7pm), off-peak, and weekends.
        A dash means fewer than 20 trains so far. Tap a station to see its live departures.
      </p>
    </section>
  )
}

const shortBand = { AmPeak: 'AM peak', PmPeak: 'PM peak', OffPeak: 'Off-peak', Weekend: 'Weekend' } as const

function summary(stats: Stats): string {
  if (stats.latePercent === null) return `Collecting data (${stats.observed} trains so far)`
  return `Late ${percentText(stats.latePercent)}, cancelled ${percentText(stats.cancellationPercent)}`
}
