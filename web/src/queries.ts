import { getLine, getLines, getStationLines, NotFoundError } from './api'

/** Line stats change slowly (they cover 3 weeks), so they're kept a few minutes before reloading. */
const STATS_STALE_MS = 5 * 60_000

export const linesQuery = {
  queryKey: ['lines'],
  queryFn: ({ signal }: { signal: AbortSignal }) => getLines(signal),
  staleTime: STATS_STALE_MS,
}

export const stationLinesQuery = (stationId: string) => ({
  queryKey: ['station-lines', stationId],
  queryFn: ({ signal }: { signal: AbortSignal }) => getStationLines(stationId, signal),
  staleTime: STATS_STALE_MS,
})

export const lineReportQuery = (line: string) => ({
  queryKey: ['line', line],
  queryFn: ({ signal }: { signal: AbortSignal }) => getLine(line, signal),
  staleTime: STATS_STALE_MS,
  retry: (count: number, error: Error) => !(error instanceof NotFoundError) && count < 3,
})

/** Lines in the order the API lists them (T1 to T9, then intercity), any it doesn't know at the end. */
export function inLineOrder(lines: string[], order: string[] | undefined): string[] {
  const position = (line: string) => {
    const i = order?.indexOf(line) ?? -1
    return i === -1 ? Number.MAX_SAFE_INTEGER : i
  }
  return [...lines].sort((a, b) => position(a) - position(b) || a.localeCompare(b))
}
