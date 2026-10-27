import { useQuery } from '@tanstack/react-query'
import { Link, useSearchParams } from 'react-router'
import type { TimeBand } from '../api'
import { LineBadge } from '../components/LineBadge'
import { Band, Content } from '../components/Page'
import { TimeBandTabs } from '../components/TimeBandTabs'
import { lineName, percentText, timeBandHours } from '../format'
import { useNow } from '../hooks'
import { linesQuery } from '../queries'
import { isHighLate, TIME_BANDS, timeBandAt } from '../stats'

/** Each Line's Late % and Cancellation % in one Time Band (?band=), the current one to start with. */
export function Lines() {
  const [params, setParams] = useSearchParams()
  const now = useNow(60_000)
  const asked = params.get('band') as TimeBand | null
  const band: TimeBand = asked && TIME_BANDS.includes(asked) ? asked : timeBandAt(now)

  // One request has every Line in every Time Band, so switching tabs needs no loading.
  const lines = useQuery(linesQuery)

  return (
    <>
      <Band title="Line stats">
        <p className="band-text">How often each line ran late or was cancelled over the last 3 weeks.</p>
        <TimeBandTabs value={band} onChange={(b) => setParams({ band: b }, { replace: true })} />
      </Band>

      <Content>
        <p className="hint">{timeBandHours[band]}</p>
        {lines.isPending && <p className="hint">Loading lines…</p>}
        {lines.isError && <p className="error">Couldn't load line stats.</p>}
        {lines.data && (
          <table className="data-table stack-table" id="band-panel" role="tabpanel">
            <thead>
              <tr>
                <th scope="col">Line</th>
                <th scope="col" className="num">
                  Late
                </th>
                <th scope="col" className="num">
                  Cancelled
                </th>
                <th scope="col" className="num">
                  Trains counted
                </th>
              </tr>
            </thead>
            <tbody>
              {lines.data.map((l) => {
                const stats = l.timeBands.find((b) => b.timeBand === band)?.stats ?? null
                return (
                  <tr key={l.line}>
                    <th scope="row">
                      <Link to={`/lines/${encodeURIComponent(l.line)}`} className="service line-link">
                        <LineBadge line={l.line} color={l.color} />
                        {lineName(l.line) && <span>{lineName(l.line)}</span>}
                      </Link>
                    </th>
                    <td data-label="Late" className={`num${isHighLate(stats?.latePercent ?? null) ? ' tone-warn' : ''}`}>
                      {percentText(stats?.latePercent ?? null)}
                    </td>
                    <td data-label="Cancelled" className="num">
                      {percentText(stats?.cancellationPercent ?? null)}
                    </td>
                    <td data-label="Trains counted" className="num muted">
                      {stats?.timetabled ?? ''}
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        )}

        <p className="footnote">
          Late means more than 2 minutes behind the timetable. Cancelled includes trains that skipped a station. Each train is
          counted at every station it stops at. A dash means fewer than 20 trains so far, which isn't enough to go on. Late of
          25% or more is highlighted. <Link to="/how-we-measure-late">How we measure late</Link>
        </p>
      </Content>
    </>
  )
}
