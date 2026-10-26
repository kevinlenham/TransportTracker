import { useRef } from 'react'
import { Link } from 'react-router'
import { NotFoundError, type Departure } from '../api'
import { boardRows } from '../boardRows'
import {
  clockTime,
  durationText,
  isRunningLate,
  minutesUntil,
  platformLabel,
  runsLateText,
  statusText,
  statusTone,
  timeBandLabel,
} from '../format'
import { useKeepInPlace } from '../hooks'
import type { BoardQuery } from '../useBoard'
import { LineBadge } from './LineBadge'

/**
 * A departure board as a table. For a trip (`trip`) the first column is departure to arrival with the
 * journey time; otherwise it's the departure time. "Show earlier trains" keeps the trains on screen
 * where they are while earlier ones are added above. With `line`, only that Line's trains.
 */
export function DepartureTable({ board, now, trip = false, line }: { board: BoardQuery; now: number; trip?: boolean; line?: string }) {
  const bodyRef = useRef<HTMLTableSectionElement>(null)
  const rememberPosition = useKeepInPlace(bodyRef, board.data?.pages.length)

  if (board.isPending) return <p className="hint">Loading trains…</p>
  if (board.isError) {
    return board.error instanceof NotFoundError ? (
      <p className="error">
        Station not found. <Link to={trip ? '/' : '/stations'}>Search again</Link>
      </p>
    ) : (
      <p className="error">
        Couldn't load trains.{' '}
        <button className="link-button" onClick={() => board.refetch()}>
          Try again
        </button>
      </p>
    )
  }

  const first = board.data.pages[0]
  const rows = boardRows(board.data.pages, now, line ? (d) => d.line === line : undefined)
  const empty = !rows.some((r) => r.kind === 'train')

  return (
    <>
      <p className="board-meta">
        <span>
          {first.feedTime ? `Live data as of ${clockTime(first.feedTime)}` : 'Live data unavailable, showing the timetable'}
          {board.isRefetchError && ' · couldn’t refresh, retrying'}
        </span>
      </p>

      {board.hasPreviousPage && (
        <p className="board-paging">
          <button
            className="link-button"
            disabled={board.isFetchingPreviousPage}
            onClick={() => {
              rememberPosition()
              void board.fetchPreviousPage()
            }}
          >
            {board.isFetchingPreviousPage ? 'Loading…' : 'Show earlier trains'}
          </button>
        </p>
      )}

      {empty ? (
        <p className="hint">
          {line
            ? `No ${line} trains in these departures.${board.hasNextPage ? ' Try showing later trains.' : ''}`
            : trip
              ? 'No direct trains in the next 24 hours. This trip may need a change of trains.'
              : 'No trains in the next 24 hours.'}
        </p>
      ) : (
        <table className="data-table departures-table stack-table">
          <thead>
            <tr>
              <th scope="col">{trip ? 'Departs–arrives' : 'Departs'}</th>
              <th scope="col">Service</th>
              <th scope="col">Runs late</th>
              <th scope="col" className="num">
                Leaves
              </th>
            </tr>
          </thead>
          <tbody ref={bodyRef}>
            {rows.map((row) =>
              row.kind === 'train' ? (
                <TrainRow key={row.key} dataKey={row.key} departure={row.departure} past={row.past} now={now} trip={trip} />
              ) : (
                <tr key={row.key} className={`divider-row divider-row--${row.kind}`}>
                  <td colSpan={4}>{row.kind === 'now' ? 'Now' : row.label}</td>
                </tr>
              ),
            )}
          </tbody>
        </table>
      )}

      {board.hasNextPage && (
        <p className="board-paging">
          <button className="link-button" onClick={() => board.fetchNextPage()} disabled={board.isFetchingNextPage}>
            {board.isFetchingNextPage ? 'Loading…' : 'Show later trains'}
          </button>
        </p>
      )}
    </>
  )
}

function TrainRow({ departure: d, past, now, trip, dataKey }: {
  departure: Departure
  past: boolean
  now: number
  trip: boolean
  dataKey: string
}) {
  const gone = d.status === 'Cancelled' || d.status === 'Skipped'
  const platform = platformLabel(d.platformName)

  return (
    <tr className={`train-row${gone ? ' row-gone' : ''}${past ? ' row-past' : ''}`} data-key={dataKey}>
      <td className="col-time">
        <DepartureTime departure={d} />
        {trip && d.arrival && !gone && (
          <>
            <span className="cell-main"> – {clockTime(d.arrival.expectedAt)}</span>
            <div className="cell-sub">{durationText(d.expectedAt, d.arrival.expectedAt)}</div>
          </>
        )}
      </td>
      <td className="col-service">
        <div className="service">
          <LineBadge line={d.line} color={d.lineColor} />
          <span className="service-name">{d.headsign ? `to ${d.headsign}` : 'Destination unknown'}</span>
        </div>
        {platform && <div className="cell-sub">{platform}</div>}
        <div className={`cell-status tone-${statusTone(d)}`}>
          {statusText(d)}
          {d.status === 'Departed' && d.delaySeconds === null && <span className="tone-muted"> · no live data recorded</span>}
        </div>
      </td>
      <td className="col-late" title={past ? undefined : `Over the last 3 weeks, ${timeBandLabel(d.late.timeBand)}`}>
        {!past && <span className={d.late.latePercent === null ? 'muted' : undefined}>{runsLateText(d.late)}</span>}
      </td>
      <td className="col-leaves num">
        {past ? (
          <span className="muted">{d.status === 'Departed' ? 'Left' : 'Gone'}</span>
        ) : gone ? (
          <span className="muted">—</span>
        ) : (
          <span className="cell-main">{minutesUntil(d.expectedAt, now)}</span>
        )}
      </td>
    </tr>
  )
}

/** The departure time. A late train shows its timetabled time struck through and when it's now expected. */
function DepartureTime({ departure: d }: { departure: Departure }) {
  if (!isRunningLate(d)) return <span className="cell-main">{clockTime(d.scheduledAt)}</span>
  return (
    <span className="cell-main">
      <s className="struck">{clockTime(d.scheduledAt)}</s>{' '}
      <span className="tone-warn">
        {d.status === 'Departed' ? 'left' : 'now'} {clockTime(d.expectedAt)}
      </span>
    </span>
  )
}
