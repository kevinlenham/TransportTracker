import { useQueries, useQuery } from '@tanstack/react-query'
import { useId, useState } from 'react'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router'
import type { Station } from '../api'
import { DepartureTable } from '../components/DepartureTable'
import { LineBadge } from '../components/LineBadge'
import { Band, Content } from '../components/Page'
import { StationPicker } from '../components/StationPicker'
import { lineName, percentText, stationLabel } from '../format'
import { useNow } from '../hooks'
import { inLineOrder, lineReportQuery, linesQuery } from '../queries'
import { isHighLate, stationStats } from '../stats'
import { useBoard } from '../useBoard'

/** Every train leaving one Station, optionally only one Line's. The Station is in the path, the Line in ?line=. */
export function StationDepartures() {
  const { stationId = null } = useParams()
  const [params] = useSearchParams()
  const line = params.get('line') ?? ''
  const navigate = useNavigate()
  const now = useNow(15_000)
  const lineSelectId = useId()

  const board = useBoard(stationId)
  const allLines = useQuery(linesQuery)
  const lineOrder = allLines.data?.map((l) => l.line)
  const departures = board.data?.pages.flatMap((p) => p.departures) ?? []
  const boardLines = inLineOrder([...new Set(departures.map((d) => d.line).filter((l) => l !== null))], lineOrder)
  const colors = new Map(departures.map((d) => [d.line, d.lineColor]))

  // The form starts from what's shown, until the rider changes it.
  const [picked, setPicked] = useState<Station | null | undefined>(undefined)
  const [pickedLine, setPickedLine] = useState<string | undefined>(undefined)
  const [error, setError] = useState<string | null>(null)
  const boardStation = board.data?.pages[0].station ?? null
  const station = picked !== undefined ? picked : boardStation
  const selectedLine = pickedLine ?? line

  // A Station's own Lines once its trains have loaded, every Line before then.
  const lineOptions = [...(station && station.id === stationId && board.data ? boardLines : (lineOrder ?? []))]
  if (selectedLine && !lineOptions.includes(selectedLine)) lineOptions.push(selectedLine)

  return (
    <>
      <Band title="Station departures">
        <form
          className="station-form"
          onSubmit={(e) => {
            e.preventDefault()
            if (!station) return setError('Choose a station from the list.')
            setError(null)
            navigate(`/stations/${encodeURIComponent(station.id)}${selectedLine ? `?${new URLSearchParams({ line: selectedLine })}` : ''}`)
          }}
        >
          <StationPicker label="Station" value={station} onPick={setPicked} placeholder="e.g. Central, Parramatta" autoFocus={!stationId} />
          <div className="field">
            <label className="field-label" htmlFor={lineSelectId}>
              Line
            </label>
            <select id={lineSelectId} className="select" value={selectedLine} onChange={(e) => setPickedLine(e.target.value)}>
              <option value="">All lines</option>
              {lineOptions.map((l) => (
                <option key={l} value={l}>
                  {lineName(l) ? `${l} ${lineName(l)}` : l}
                </option>
              ))}
            </select>
          </div>
          <button type="submit" className="button">
            Show departures
          </button>
          {error && <p className="form-error">{error}</p>}
        </form>
        <p className="band-links">
          Going somewhere specific? <Link to="/">Plan a trip</Link> to see only the trains that get you there.
        </p>
      </Band>

      <Content>
        {!stationId ? (
          <p className="hint">Choose a station to see every train leaving it, with how often each one runs late.</p>
        ) : (
          <div className="with-sidebar">
            <section aria-labelledby="board-heading">
              <h2 id="board-heading" className="section-title results-heading">
                {boardStation ? stationLabel(boardStation.name) : 'Departures'}
                {line && <span className="muted"> · {line} trains only</span>}
              </h2>
              <DepartureTable board={board} now={now} line={line || undefined} />
              <p className="footnote">
                Late means more than 2 minutes behind the timetable. “Runs late” comes from the last 3 weeks of this
                station’s trains on the same line, in the same direction, at the same time of the week.
              </p>
            </section>

            {boardStation && boardLines.length > 0 && (
              <aside className="sidebar">
                <LinesAtStation stationId={boardStation.id} name={stationLabel(boardStation.name)} lines={boardLines} colors={colors} />
              </aside>
            )}
          </div>
        )}
      </Content>
    </>
  )
}

/** Each Line's Late % at this Station over 3 weeks, both directions and all times together. */
function LinesAtStation({ stationId, name, lines, colors }: {
  stationId: string
  name: string
  lines: string[]
  colors: Map<string | null, string | null>
}) {
  const reports = useQueries({ queries: lines.map(lineReportQuery) })

  return (
    <section>
      <h2 className="section-title">Late % by line at {name}</h2>
      <table className="data-table data-table--compact">
        <thead>
          <tr>
            <th scope="col">Line</th>
            <th scope="col" className="num">
              Late
            </th>
            <th scope="col" className="num">
              Trains
            </th>
          </tr>
        </thead>
        <tbody>
          {lines.map((line, i) => {
            const report = reports[i]
            const stats = report.data ? stationStats(report.data, stationId) : null
            return (
              <tr key={line}>
                <th scope="row">
                  <LineBadge line={line} color={colors.get(line) ?? null} />
                </th>
                <td className={`num${isHighLate(stats?.latePercent ?? null) ? ' tone-warn' : ''}`}>
                  {report.isPending ? '…' : report.isError ? '?' : percentText(stats?.latePercent ?? null)}
                </td>
                <td className="num muted">{stats?.observed ?? ''}</td>
              </tr>
            )
          })}
        </tbody>
      </table>
      <p className="footnote">Last 3 weeks, all times of day. A dash means fewer than 20 trains so far.</p>
    </section>
  )
}
