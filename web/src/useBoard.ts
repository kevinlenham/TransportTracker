import { useInfiniteQuery } from '@tanstack/react-query'
import { getDepartures, NotFoundError } from './api'

const PAGE_SIZE = 20
/** Matches the API's poll interval, so each refresh can bring new Live Delays. */
const REFRESH_MS = 30_000

/**
 * A departure board that starts at the next train and pages each way: earlier pages go back through
 * trains that have left (with how late they were), later pages carry on overnight to the morning.
 * With `to`, only direct trains to that Station, each with its arrival. Off until there's a Station.
 */
export function useBoard(stationId: string | null, to?: string | null) {
  return useInfiniteQuery({
    queryKey: ['board', stationId, to ?? null],
    queryFn: ({ pageParam, signal }) =>
      getDepartures(stationId!, { to: to ?? undefined, cursor: pageParam, limit: PAGE_SIZE }, signal),
    enabled: stationId !== null && to !== null,
    initialPageParam: null as string | null,
    getNextPageParam: (last) => last.later,
    getPreviousPageParam: (first) => first.earlier,
    refetchInterval: REFRESH_MS,
    retry: (count, error) => !(error instanceof NotFoundError) && count < 3,
  })
}

export type BoardQuery = ReturnType<typeof useBoard>
