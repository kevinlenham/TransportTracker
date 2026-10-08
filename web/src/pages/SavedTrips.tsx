import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { Link } from 'react-router'
import type { Station } from '../api'
import { DepartureRow } from '../components/DepartureRow'
import { StationPicker } from '../components/StationPicker'
import { stationLabel } from '../format'
import { useNow } from '../hooks'
import { tripBoardQuery, tripKey, useSavedTrips, type SavedTrip } from '../savedTrips'

export function SavedTrips() {
  const { trips, add, remove } = useSavedTrips()
  const [adding, setAdding] = useState(trips.length === 0)

  return (
    <section>
      <h1 className="page-title">My trips</h1>
      <p className="hint">Your trips are saved on this device only. They show direct trains only.</p>

      {trips.map((trip) => (
        <TripCard key={tripKey(trip)} trip={trip} onRemove={() => remove(trip)} />
      ))}

      {adding ? (
        <AddTrip
          onAdd={(trip) => {
            add(trip)
            setAdding(false)
          }}
          onCancel={trips.length > 0 ? () => setAdding(false) : undefined}
        />
      ) : (
        <button className="primary" onClick={() => setAdding(true)}>
          + Add a trip
        </button>
      )}
    </section>
  )
}

function TripCard({ trip, onRemove }: { trip: SavedTrip; onRemove: () => void }) {
  const now = useNow(15_000)
  const board = useQuery(tripBoardQuery(trip))

  return (
    <article className="card">
      <header className="card-header">
        <h2>
          {stationLabel(trip.fromName)} → {stationLabel(trip.toName)}
        </h2>
        <button onClick={onRemove} aria-label={`Remove ${trip.fromName} to ${trip.toName}`}>
          Remove
        </button>
      </header>

      {board.isPending && <p className="hint">Loading trains…</p>}
      {board.isError && <p className="error">Couldn't load trains for this trip.</p>}
      {board.data?.departures.length === 0 && (
        <p className="hint">No direct trains in the next 24 hours. This trip may need a change of trains.</p>
      )}
      {board.data && board.data.departures.length > 0 && (
        <>
          <ul className="departures">
            {board.data.departures.map((d) => (
              <DepartureRow key={`${d.tripId}-${d.scheduledAt}`} departure={d} now={now} />
            ))}
          </ul>
          <Link to={`/trips/${trip.fromId}/${trip.toId}`} className="see-all">
            See all trains →
          </Link>
        </>
      )}
    </article>
  )
}

function AddTrip({ onAdd, onCancel }: { onAdd: (trip: SavedTrip) => void; onCancel?: () => void }) {
  const [from, setFrom] = useState<Station | null>(null)
  const [to, setTo] = useState<Station | null>(null)
  const same = from !== null && to !== null && from.id === to.id

  return (
    <form
      className="card"
      onSubmit={(e) => {
        e.preventDefault()
        if (from && to && !same) onAdd({ fromId: from.id, fromName: from.name, toId: to.id, toName: to.name })
      }}
    >
      <h2>Add a trip</h2>
      <StationPicker label="From" value={from} onPick={setFrom} />
      <StationPicker label="To" value={to} onPick={setTo} />
      {same && <p className="error">Choose two different stations.</p>}
      <div className="actions">
        <button type="submit" className="primary" disabled={!from || !to || same}>
          Save trip
        </button>
        {onCancel && (
          <button type="button" onClick={onCancel}>
            Cancel
          </button>
        )}
      </div>
    </form>
  )
}
