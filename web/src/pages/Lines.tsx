import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'
import { getLines } from '../api'
import { LineBadge } from '../components/LineBadge'
import { percentText } from '../format'

export function Lines() {
  const lines = useQuery({ queryKey: ['lines'], queryFn: ({ signal }) => getLines(signal), staleTime: 5 * 60_000 })

  return (
    <section>
      <h1 className="page-title">Line stats</h1>
      <p className="hint">How often each line ran late or was cancelled over the last 3 weeks.</p>

      {lines.isPending && <p className="hint">Loading lines…</p>}
      {lines.isError && <p className="error">Couldn't load line stats.</p>}
      {lines.data && (
        <table className="stats-table">
          <thead>
            <tr>
              <th scope="col">Line</th>
              <th scope="col">Late</th>
              <th scope="col">Cancelled</th>
              <th scope="col">Trains</th>
            </tr>
          </thead>
          <tbody>
            {lines.data.map((l) => (
              <tr key={l.line}>
                <th scope="row">
                  <Link to={`/lines/${encodeURIComponent(l.line)}`} className="line-link">
                    <LineBadge line={l.line} color={l.color} />
                  </Link>
                </th>
                <td>{percentText(l.stats.latePercent)}</td>
                <td>{percentText(l.stats.cancellationPercent)}</td>
                <td className="muted">{l.stats.observed}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      <p className="footnote">
        Late means more than 2 minutes behind the timetable. Cancelled includes trains that skipped a station. A dash
        means fewer than 20 trains so far, so there's not enough data yet.
      </p>
    </section>
  )
}
