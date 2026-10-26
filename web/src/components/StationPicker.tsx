import { useQuery } from '@tanstack/react-query'
import { useId, useState } from 'react'
import { searchStations, type Station } from '../api'
import { stationLabel } from '../format'
import { useDebounced } from '../hooks'

const MIN_QUERY_LENGTH = 2
const OPTIONS_SHOWN = 8

/**
 * A text box for choosing a Station: matching Stations are listed under it as you type, and can be
 * picked with the mouse or the arrow keys and Enter. Typing clears the chosen Station until a new one is
 * picked. Shows the chosen Station's name otherwise.
 */
export function StationPicker({ label, value, onPick, placeholder = 'Station name', className, autoFocus }: {
  label: string
  value: Station | null
  onPick: (station: Station | null) => void
  placeholder?: string
  className?: string
  autoFocus?: boolean
}) {
  // What the rider has typed, or null to show the chosen Station.
  const [text, setText] = useState<string | null>(null)
  const [open, setOpen] = useState(false)
  const [active, setActive] = useState(0)
  const id = useId()

  // Show a Station chosen from outside, e.g. by the swap button, instead of what was typed.
  const [shownValue, setShownValue] = useState(value)
  if (shownValue !== value) {
    setShownValue(value)
    if (value) setText(null)
  }

  const query = useDebounced((text ?? '').trim(), 200)
  const results = useQuery({
    queryKey: ['stations', query],
    queryFn: ({ signal }) => searchStations(query, signal),
    enabled: text !== null && query.length >= MIN_QUERY_LENGTH,
    staleTime: 60 * 60_000, // Station names only change with the Timetable.
  })
  const options = text !== null && query.length >= MIN_QUERY_LENGTH ? (results.data ?? []).slice(0, OPTIONS_SHOWN) : []
  const listOpen = open && text !== null && query.length >= MIN_QUERY_LENGTH && results.data !== undefined

  function pick(station: Station) {
    onPick(station)
    setText(null)
    setOpen(false)
  }

  return (
    <div className={`field combobox ${className ?? ''}`}>
      <label className="field-label" htmlFor={id}>
        {label}
      </label>
      <input
        id={id}
        className="input"
        type="text"
        role="combobox"
        aria-autocomplete="list"
        aria-expanded={listOpen}
        aria-controls={`${id}-list`}
        aria-activedescendant={listOpen && options[active] ? `${id}-${active}` : undefined}
        autoComplete="off"
        autoFocus={autoFocus}
        placeholder={placeholder}
        value={text ?? (value ? stationLabel(value.name) : '')}
        onChange={(e) => {
          setText(e.target.value)
          setOpen(true)
          setActive(0)
          if (value) onPick(null)
        }}
        onFocus={(e) => e.target.select()}
        onBlur={() => setOpen(false)}
        onKeyDown={(e) => {
          if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
            e.preventDefault()
            setOpen(true)
            if (options.length) setActive((a) => (a + (e.key === 'ArrowDown' ? 1 : options.length - 1)) % options.length)
          } else if (e.key === 'Enter' && listOpen && options[active]) {
            e.preventDefault() // Picks the Station rather than submitting the form.
            pick(options[active])
          } else if (e.key === 'Escape') {
            setOpen(false)
          }
        }}
      />
      {listOpen && (
        <ul className="combobox-list" id={`${id}-list`} role="listbox" aria-label={`${label} stations`}>
          {options.length === 0 && <li className="combobox-empty">No stations match “{query}”</li>}
          {options.map((s, i) => (
            <li
              key={s.id}
              id={`${id}-${i}`}
              role="option"
              aria-selected={i === active}
              className="combobox-option"
              // On mouse down, so it happens before the box loses focus and the list closes.
              onMouseDown={(e) => {
                e.preventDefault()
                pick(s)
              }}
              onMouseEnter={() => setActive(i)}
            >
              {stationLabel(s.name)}
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
