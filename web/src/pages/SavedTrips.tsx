import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { Link } from 'react-router'
import type { Departure, Station } from '../api'
import { LineBadge } from '../components/LineBadge'
import { Band, Content } from '../components/Page'
import { StationPicker } from '../components/StationPicker'
import { clockTime, minutesUntil, runsLateText, stationLabel, statusText, statusTone, timeBandLabel } from '../format'
import { useNow } from '../hooks'
import { tripBoardQuery, tripKey, useSavedTrips, type SavedTrip } from '../savedTrips'

export function SavedTrips() {
  const { trips, add, remove } = useSavedTrips()
  const [adding, setAdding] = useState(trips.length === 0)

  return (
    <>
      <Band title="My trips">
        <p className="band-text">Saved on this device only, so there's no account to sign in to. Shows direct trains only.</p>
        {adding ? (
          <AddTrip
            onAdd={(trip) => {
              add(trip)
              setAdding(false)
            }}
            onCancel={trips.length > 0 ? () => setAdding(false) : undefined}
          />
        ) : (
          <button type="button" className="button" onClick={() => setAdding(true)}>
            Add a trip
          </button>
        )}
      </Band>

      <Content>
        {trips.length === 0 ? (
          <p className="hint">You haven't saved any trips yet. Add the trips you take often to see their next trains here.</p>
        ) : (
          <div className="trip-list">
            {trips.map((trip) => (
              <TripBlock key={tripKey(trip)} trip={trip} onRemove={() => remove(trip)} />
            ))}
          </div>
        )}
      </Content>
    </>
  )
}

/** One Saved Trip: the next train, how often it runs late, and the two trains after it. */
function TripBlock({ trip, onRemove }: { trip: SavedTrip; onRemove: () => void }) {
  const now = useNow(15_000)
  const board = useQuery(tripBoardQuery(trip))
  const [next, ...after] = board.data?.departures ?? []
  const title = `${stationLabel(trip.fromName)} to ${stationLabel(trip.toName)}`

  return (
    <article className="trip-block" aria-label={title}>
      <header className="trip-block-header">
        <h2>{title}</h2>
        <button type="button" className="link-button" onClick={onRemove} aria-label={`Remove ${title}`}>
          Remove
        </button>
      </header>

      <div className="trip-block-body">
        {board.isPending && <p className="hint">Loading trains…</p>}
        {board.isError && <p className="error">Couldn't load trains for this trip.</p>}
        {board.isSuccess && !next && <p className="hint">No direct trains in the next 24 hours. This trip may need a change of trains.</p>}
        {next && (
          <>
            <section>
              <h3 className="column-label">Next train</h3>
              <div className="cell-main">
                <TimeRange departure={next} />
              </div>
              <div className="service">
                <LineBadge line={next.line} color={next.lineColor} />
                <span>{next.headsign ? `to ${next.headsign}` : ''}</span>
              </div>
              <div className={`cell-status tone-${statusTone(next)}`}>
                {statusText(next)}
                {next.status !== 'Cancelled' && next.status !== 'Skipped' && (
                  <span className="tone-muted"> · leaves {leavesText(next, now)}</span>
                )}
              </div>
            </section>

            <section>
              <h3 className="column-label">Runs late</h3>
              {next.late.latePercent === null ? (
                <p className="muted">{runsLateText(next.late)}</p>
              ) : (
                <>
                  <div className="big-number">{Math.round(next.late.latePercent)}%</div>
                  <div className="cell-sub">
                    of {next.late.sampleSize} trains like this one, {timeBandLabel(next.late.timeBand)}
                  </div>
                </>
              )}
            </section>

            <section>
              <h3 className="column-label">After that</h3>
              {after.length === 0 ? (
                <p className="muted">No more direct trains soon.</p>
              ) : (
                <ul className="next-trains">
                  {after.map((d) => (
                    <li key={`${d.tripId}-${d.scheduledAt}`}>
                      <span className="cell-main">
                        <TimeRange departure={d} />
                      </span>
                      <LineBadge line={d.line} color={d.lineColor} />
                      <span className={`tone-${statusTone(d)}`}>{statusText(d)}</span>
                    </li>
                  ))}
                </ul>
              )}
              <Link to={`/?${new URLSearchParams({ from: trip.fromId, to: trip.toId })}`}>All trains for this trip</Link>
            </section>
          </>
        )}
      </div>
    </article>
  )
}

/** "9:04 pm – 9:31 pm", from expected departure to expected arrival. */
function TimeRange({ departure: d }: { departure: Departure }) {
  return (
    <>
      {clockTime(d.expectedAt)}
      {d.arrival && d.status !== 'Cancelled' && d.status !== 'Skipped' && ` – ${clockTime(d.arrival.expectedAt)}`}
    </>
  )
}

const leavesText = (d: Departure, now: number) => {
  const text = minutesUntil(d.expectedAt, now)
  return text === 'Now' ? 'now' : `in ${text}`
}

function AddTrip({ onAdd, onCancel }: { onAdd: (trip: SavedTrip) => void; onCancel?: () => void }) {
  const [from, setFrom] = useState<Station | null>(null)
  const [to, setTo] = useState<Station | null>(null)
  const [error, setError] = useState<string | null>(null)

  return (
    <form
      className="add-trip-form"
      aria-label="Add a trip"
      onSubmit={(e) => {
        e.preventDefault()
        if (!from || !to) return setError('Choose both stations from the list.')
        if (from.id === to.id) return setError('Choose two different stations.')
        onAdd({ fromId: from.id, fromName: from.name, toId: to.id, toName: to.name })
      }}
    >
      <StationPicker label="From" value={from} onPick={setFrom} autoFocus />
      <StationPicker label="To" value={to} onPick={setTo} />
      <button type="submit" className="button">
        Save trip
      </button>
      {onCancel && (
        <button type="button" className="button-outline" onClick={onCancel}>
          Cancel
        </button>
      )}
      {error && <p className="form-error">{error}</p>}
    </form>
  )
}
