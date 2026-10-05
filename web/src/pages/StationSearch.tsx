import { useQuery } from '@tanstack/react-query'
import { Link, useSearchParams } from 'react-router'
import { searchStations } from '../api'
import { stationLabel } from '../format'
import { useDebounced } from '../hooks'

const MIN_QUERY_LENGTH = 2

export function StationSearch() {
  // The query lives in the URL, so Back from a Station returns to the same results.
  const [params, setParams] = useSearchParams()
  const q = params.get('q') ?? ''
  const debounced = useDebounced(q.trim(), 250)
  const ready = debounced.length >= MIN_QUERY_LENGTH

  const results = useQuery({
    queryKey: ['stations', debounced],
    queryFn: ({ signal }) => searchStations(debounced, signal),
    enabled: ready,
    staleTime: 60 * 60_000, // Station names only change with the Timetable.
  })

  return (
    <section>
      <h1 className="page-title">Find a station</h1>
      <input
        className="search"
        type="search"
        placeholder="e.g. Central, Parramatta"
        aria-label="Station name"
        autoFocus
        value={q}
        onChange={(e) => setParams(e.target.value ? { q: e.target.value } : {}, { replace: true })}
      />

      {!ready && <p className="hint">Type at least {MIN_QUERY_LENGTH} letters of a Sydney Trains station.</p>}
      {ready && results.isPending && <p className="hint">Searching…</p>}
      {results.isError && <p className="error">Couldn't search stations. Check your connection and try again.</p>}
      {results.data?.length === 0 && <p className="hint">No stations match “{debounced}”.</p>}

      {results.data && results.data.length > 0 && (
        <ul className="station-list">
          {results.data.map((s) => (
            <li key={s.id}>
              <Link to={`/stations/${s.id}`}>{stationLabel(s.name)}</Link>
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}
