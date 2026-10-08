import { useInfiniteQuery } from '@tanstack/react-query'
import type { ReactNode } from 'react'
import { Link } from 'react-router'
import { getDepartures, NotFoundError, type Board } from '../api'
import { boardRows } from '../boardRows'
import { clockTime } from '../format'
import { useNow } from '../hooks'
import { DepartureRow } from './DepartureRow'

const PAGE_SIZE = 20
/** Matches the API's poll interval, so each refresh can bring new Live Delays. */
const REFRESH_MS = 30_000

/**
 * A departure board that starts at the next train and loads more each way: "Show earlier trains"
 * goes back through trains that have left (with how late they actually were), and "Show later
 * trains" carries on, overnight to the first trains of the morning.
 */
export function DepartureList({ stationId, to, header }: { stationId: string; to?: string; header: (board: Board) => ReactNode }) {
  const now = useNow(15_000)
  const board = useInfiniteQuery({
    queryKey: ['board', stationId, to ?? null],
    queryFn: ({ pageParam, signal }) => getDepartures(stationId, { to, cursor: pageParam, limit: PAGE_SIZE }, signal),
    initialPageParam: null as string | null,
    getNextPageParam: (last) => last.later,
    getPreviousPageParam: (first) => first.earlier,
    refetchInterval: REFRESH_MS,
    retry: (count, error) => !(error instanceof NotFoundError) && count < 3,
  })

  if (board.isPending) return <p className="hint">Loading departures…</p>
  if (board.isError) {
    return board.error instanceof NotFoundError ? (
      <p className="error">
        Station not found. <Link to="/">Search again</Link>
      </p>
    ) : (
      <p className="error">
        Couldn't load departures. <button onClick={() => board.refetch()}>Try again</button>
      </p>
    )
  }

  const pages = board.data.pages
  const first = pages[0] // Every page carries the Station and feed time, and all refresh together.
  const rows = boardRows(pages, now)

  return (
    <>
      {header(first)}
      <p className="hint">
        {first.feedTime ? `Live data as of ${clockTime(first.feedTime)}` : 'Live data unavailable, showing the timetable'}
        {board.isRefetchError && ' · couldn’t refresh, retrying'}
      </p>

      {board.hasPreviousPage && (
        <button className="load-more" onClick={() => board.fetchPreviousPage()} disabled={board.isFetchingPreviousPage}>
          {board.isFetchingPreviousPage ? 'Loading…' : '↑ Show earlier trains'}
        </button>
      )}

      {rows.length === 0 ? (
        <p className="hint">No trains in the next 24 hours.</p>
      ) : (
        <ul className="departures">
          {rows.map((row) =>
            row.kind === 'train' ? (
              <DepartureRow key={row.key} departure={row.departure} now={now} past={row.past} />
            ) : (
              <li key={row.key} className={`divider divider--${row.kind}`}>
                {row.kind === 'now' ? 'Now' : row.label}
              </li>
            ),
          )}
        </ul>
      )}

      {board.hasNextPage && (
        <button className="load-more" onClick={() => board.fetchNextPage()} disabled={board.isFetchingNextPage}>
          {board.isFetchingNextPage ? 'Loading…' : '↓ Show later trains'}
        </button>
      )}
    </>
  )
}
