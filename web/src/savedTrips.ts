import { useSyncExternalStore } from 'react'
import { getTripDepartures } from './api'

/** A rider's favourite Origin → Destination pair. Kept on the device only, so there's no login. */
export interface SavedTrip {
  fromId: string
  fromName: string
  toId: string
  toName: string
}

const KEY = 'transporttracker.savedTrips'
const listeners = new Set<() => void>()

// Storage can be unavailable (private browsing, blocked site data), so the app still works without it,
// keeping trips in memory for the visit.
let memory: SavedTrip[] = []
let cachedJson: string | null = null
let cached: SavedTrip[] = []

function read(): SavedTrip[] {
  let json: string | null
  try {
    json = localStorage.getItem(KEY)
  } catch {
    return memory
  }
  if (json === cachedJson) return cached
  cachedJson = json
  try {
    const parsed: unknown = json ? JSON.parse(json) : []
    cached = Array.isArray(parsed) ? parsed.filter(isSavedTrip) : []
  } catch {
    cached = []
  }
  return cached
}

function write(trips: SavedTrip[]) {
  memory = trips
  try {
    localStorage.setItem(KEY, JSON.stringify(trips))
  } catch {
    // Kept in memory only.
  }
  listeners.forEach((l) => l())
}

function isSavedTrip(x: unknown): x is SavedTrip {
  const t = x as SavedTrip
  return typeof t?.fromId === 'string' && typeof t.toId === 'string' && typeof t.fromName === 'string' && typeof t.toName === 'string'
}

function subscribe(listener: () => void) {
  listeners.add(listener)
  // Keep tabs in step when another tab changes the trips.
  const onStorage = (e: StorageEvent) => e.key === KEY && listener()
  window.addEventListener('storage', onStorage)
  return () => {
    listeners.delete(listener)
    window.removeEventListener('storage', onStorage)
  }
}

export const tripKey = (t: Pick<SavedTrip, 'fromId' | 'toId'>) => `${t.fromId}-${t.toId}`

/** Trains shown per Saved Trip. */
const NEXT_TRAINS = 3

/** The next direct trains for a Saved Trip. Shared by the My trips and Alerts pages, so they read one cache. */
export const tripBoardQuery = (trip: SavedTrip) => ({
  queryKey: ['trip', trip.fromId, trip.toId],
  queryFn: ({ signal }: { signal: AbortSignal }) => getTripDepartures(trip.fromId, trip.toId, NEXT_TRAINS, signal),
  refetchInterval: 30_000,
})

export function useSavedTrips() {
  const trips = useSyncExternalStore(subscribe, read)
  return {
    trips,
    add(trip: SavedTrip) {
      if (!read().some((t) => tripKey(t) === tripKey(trip))) write([...read(), trip])
    },
    remove(trip: SavedTrip) {
      write(read().filter((t) => tripKey(t) !== tripKey(trip)))
    },
  }
}
