import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { Link, useParams } from 'react-router'
import { NotFoundError, type Stats } from '../api'
import { LineBadge } from '../components/LineBadge'
import { Band, Content } from '../components/Page'
import { lineName, percentText, shortTimeBandLabel, stationLabel, timeBandLabel } from '../format'
import { lineReportQuery } from '../queries'
import { isHighLate } from '../stats'

/** One Line's Late % at each Station, in running order, by Time Band, one Direction at a time. */
export function LineDetail() {
  const { line = '' } = useParams()
  const [directionIndex, setDirectionIndex] = useState(0)
  const report = useQuery(lineReportQuery(line))
  const title = (
    <span className="service">
      <LineBadge line={line} color={report.data?.color ?? null} /> {lineName(line) ?? line}
    </span>
  )

  if (report.isPending || report.isError) {
    return (
      <>
        <Band title={title} />
        <Content>
          {report.isPending ? (
            <p className="hint">Loading {line}…</p>
          ) : (
            <p className="error">
              {report.error instanceof NotFoundError ? `There's no line called ${line}.` : "Couldn't load this line."}{' '}
              <Link to="/lines">All lines</Link>
            </p>
          )}
        </Content>
      </>
    )
  }

  const { stats, directions } = report.data
  const direction = directions[Math.min(directionIndex, directions.length - 1)]

  return (
    <>
      <Band title={title}>
        <p className="band-text">
          {summary(stats)} over the last 3 weeks. <Link to="/lines">All lines</Link>
        </p>
        {directions.length > 1 && (
          <div className="tabs" role="tablist" aria-label="Direction">
            {directions.map((d, i) => (
              <button
                key={d.directionId ?? i}
                type="button"
                role="tab"
                aria-selected={d === direction}
                aria-controls="direction-panel"
                className="tab"
                onClick={() => setDirectionIndex(i)}
              >
                {d.headsigns.length ? `Towards ${d.headsigns.slice(0, 2).join(' / ')}` : `Direction ${i + 1}`}
              </button>
            ))}
          </div>
        )}
      </Band>

      <Content>
        {direction && (
          <table className="data-table stack-table" id="direction-panel" role="tabpanel">
            <thead>
              <tr>
                <th scope="col">Station</th>
                {direction.stations[0]?.timeBands.map((b) => (
                  <th scope="col" className="num" key={b.timeBand}>
                    {shortTimeBandLabel(b.timeBand)}
                  </th>
                ))}
                <th scope="col" className="num">
                  All times
                </th>
              </tr>
            </thead>
            <tbody>
              {direction.stations.map((s) => (
                <tr key={s.stationId}>
                  <th scope="row">
                    <Link to={`/stations/${encodeURIComponent(s.stationId)}?${new URLSearchParams({ line })}`}>{stationLabel(s.name)}</Link>
                  </th>
                  {s.timeBands.map((b) => (
                    <td
                      key={b.timeBand}
                      data-label={shortTimeBandLabel(b.timeBand)}
                      className={`num${isHighLate(b.stats.latePercent) ? ' tone-warn' : ''}`}
                      title={`${timeBandLabel(b.timeBand)}: ${b.stats.observed} trains`}
                    >
                      {percentText(b.stats.latePercent)}
                    </td>
                  ))}
                  <td data-label="All times" className={`num${isHighLate(s.stats.latePercent) ? ' tone-warn' : ''}`} title={`${s.stats.observed} trains`}>
                    <strong>{percentText(s.stats.latePercent)}</strong>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}

        <p className="footnote">
          Late % per station over the last 3 weeks: weekday AM peak (6:30–9:30 am), PM peak (3–7 pm), off-peak, and weekends.
          A dash means fewer than 20 trains so far. Choose a station to see its live {line} departures.
        </p>
      </Content>
    </>
  )
}

function summary(stats: Stats): string {
  if (stats.latePercent === null) return `Collecting data (${stats.observed} trains so far)`
  return `Late ${percentText(stats.latePercent)}, cancelled ${percentText(stats.cancellationPercent)}`
}
