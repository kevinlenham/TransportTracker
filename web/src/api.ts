// Types mirror the API's JSON (src/TransportTracker.Api/Stations). Times are ISO 8601 strings.

export type TimeBand = 'AmPeak' | 'PmPeak' | 'OffPeak' | 'Weekend'

/** Scheduled: not in the realtime feed yet. Skipped: the train runs but won't stop at this Station. */
export type DepartureStatus = 'Scheduled' | 'Live' | 'Cancelled' | 'Skipped'

export interface Station {
  id: string
  name: string
  lat: number
  lon: number
}

export interface LateChance {
  timeBand: TimeBand
  /** Null below the Minimum Sample, when the UI shows "collecting data". */
  latePercent: number | null
  sampleSize: number
  minimumSample: number
}

export interface Departure {
  tripId: string
  line: string | null
  lineColor: string | null
  headsign: string | null
  directionId: number | null
  platformId: string
  platformName: string | null
  scheduledAt: string
  expectedAt: string
  delaySeconds: number | null
  status: DepartureStatus
  late: LateChance
}

export interface Board {
  station: Station
  generatedAt: string
  /** When the realtime feed was last read. Null if the API hasn't polled yet. */
  feedTime: string | null
  departures: Departure[]
}

export class NotFoundError extends Error {}

/**
 * Usually empty: the site calls /v1 on its own origin, and Vite (in development) or a Vercel rewrite
 * (in production) forwards it to the API. Set it only to call an API directly, which needs CORS.
 */
const baseUrl = import.meta.env.VITE_API_URL ?? ''

async function get<T>(path: string, signal?: AbortSignal): Promise<T> {
  const response = await fetch(`${baseUrl}${path}`, { signal })
  if (response.status === 404) throw new NotFoundError(path)
  if (!response.ok) throw new Error(`${path} returned ${response.status}`)
  return response.json() as Promise<T>
}

export const searchStations = (q: string, signal?: AbortSignal) =>
  get<Station[]>(`/v1/stations?q=${encodeURIComponent(q)}`, signal)

export const getDepartures = (stationId: string, signal?: AbortSignal) =>
  get<Board>(`/v1/stations/${encodeURIComponent(stationId)}/departures`, signal)
