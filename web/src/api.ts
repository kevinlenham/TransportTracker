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
  /** Only for a Saved Trip: when the train reaches the destination. */
  arrival: Arrival | null
}

export interface Arrival {
  platformId: string
  platformName: string | null
  scheduledAt: string
  expectedAt: string
}

export interface Board {
  station: Station
  /** Set when the board is for a Saved Trip. */
  destination: Station | null
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

/** The next direct trains from one Station to another: a Saved Trip. */
export const getTripDepartures = (fromId: string, toId: string, limit: number, signal?: AbortSignal) =>
  get<Board>(
    `/v1/stations/${encodeURIComponent(fromId)}/departures?to=${encodeURIComponent(toId)}&limit=${limit}`,
    signal,
  )

/** Observation counts over the last 3 weeks. Percentages are null below the Minimum Sample. */
export interface Stats {
  observed: number
  late: number
  timetabled: number
  cancelled: number
  latePercent: number | null
  cancellationPercent: number | null
}

export interface LineSummary {
  line: string
  color: string | null
  stats: Stats
}

export interface LineStation {
  stationId: string
  name: string
  stats: Stats
  timeBands: { timeBand: TimeBand; stats: Stats }[]
}

export interface LineDirection {
  directionId: number | null
  /** The most common destinations this way, most common first. */
  headsigns: string[]
  stats: Stats
  stations: LineStation[]
}

export interface LineReport {
  line: string
  color: string | null
  stats: Stats
  directions: LineDirection[]
}

export const getLines = (signal?: AbortSignal) => get<LineSummary[]>('/v1/lines', signal)

export const getLine = (line: string, signal?: AbortSignal) =>
  get<LineReport>(`/v1/lines/${encodeURIComponent(line)}`, signal)

export interface ServiceAlert {
  id: string
  header: string
  description: string | null
  url: string | null
  cause: string
  effect: string
  activePeriods: { start: string | null; end: string | null }[]
  lines: string[]
  stationIds: string[]
  networkWide: boolean
}

/** Active TfNSW alerts. With stations or lines, only those affecting them, plus network-wide ones. */
export const getAlerts = (filter: { stations?: string[]; lines?: string[] }, signal?: AbortSignal) => {
  const params = new URLSearchParams()
  if (filter.stations?.length) params.set('stations', filter.stations.join(','))
  if (filter.lines?.length) params.set('lines', filter.lines.join(','))
  const query = params.size ? `?${params}` : ''
  return get<{ fetchedAt: string | null; alerts: ServiceAlert[] }>(`/v1/alerts${query}`, signal)
}
