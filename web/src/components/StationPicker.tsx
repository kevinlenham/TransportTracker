import { useQuery } from '@tanstack/react-query'
import { useId, useState } from 'react'
import { searchStations, type Station } from '../api'
import { stationLabel } from '../format'
import { useDebounced } from '../hooks'

/** A station search box that shows matches as you type. Picking one calls onPick. */
export function StationPicker({ label, value, onPick }: { label: string; value: Station | null; onPick: (s: Station | null) => void }) {
  const [text, setText] = useState('')
  const debounced = useDebounced(text.trim(), 250)
  const id = useId()

  const results = useQuery({
    queryKey: ['stations', debounced],
    queryFn: ({ signal }) => searchStations(debounced, signal),
    enabled: value === null && debounced.length >= 2,
    staleTime: 60 * 60_000,
  })

  if (value) {
    return (
      <div className="picker">
        <span className="picker-label">{label}</span>
        <span className="picker-chosen">
          {stationLabel(value.name)}
          <button type="button" onClick={() => onPick(null)} aria-label={`Change ${label.toLowerCase()} station`}>
            Change
          </button>
        </span>
      </div>
    )
  }

  return (
    <div className="picker">
      <label className="picker-label" htmlFor={id}>
        {label}
      </label>
      <input id={id} className="search" type="search" placeholder="Station name" value={text} onChange={(e) => setText(e.target.value)} />
      {results.data && results.data.length > 0 && (
        <ul className="station-list">
          {results.data.slice(0, 6).map((s) => (
            <li key={s.id}>
              <button type="button" className="station-option" onClick={() => { onPick(s); setText('') }}>
                {stationLabel(s.name)}
              </button>
            </li>
          ))}
        </ul>
      )}
      {results.data?.length === 0 && <p className="hint">No stations match “{debounced}”.</p>}
    </div>
  )
}
