import { useQuery } from '@tanstack/react-query'
import { Link, useParams } from 'react-router'
import { getDepartures, NotFoundError } from '../api'
import { DepartureRow } from '../components/DepartureRow'
import { clockTime, stationLabel } from '../format'
import { useNow } from '../hooks'

/** Matches the API's poll interval, so each refresh can bring new Live Delays. */
const REFRESH_MS = 30_000

export function StationDepartures() {
  const { stationId = '' } = useParams()
  const now = useNow(15_000)

  const board = useQuery({
    queryKey: ['departures', stationId],
    queryFn: ({ signal }) => getDepartures(stationId, signal),
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

  const { station, feedTime, departures } = board.data
  return (
    <section>
      <Link to="/" className="back">
        ← Stations
      </Link>
      <h1 className="page-title">{stationLabel(station.name)}</h1>
      <p className="hint">
        {feedTime ? `Live data as of ${clockTime(feedTime)}` : 'Live data unavailable, showing the timetable'}
        {board.isRefetchError && ' · couldn’t refresh, retrying'}
      </p>

      {departures.length === 0 ? (
        <p className="hint">No departures in the next 3 hours.</p>
      ) : (
        <ul className="departures">
          {departures.map((d) => (
            <DepartureRow key={`${d.tripId}-${d.scheduledAt}`} departure={d} now={now} />
          ))}
        </ul>
      )}

      <p className="footnote">
        Late means more than 2 minutes behind the timetable. Late % comes from the last 3 weeks of this station’s trains
        on the same line, direction and time of week.
      </p>
    </section>
  )
}
