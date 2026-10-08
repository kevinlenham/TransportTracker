import type { Board, Departure } from './api'
import { dayLabel, gapText, sydneyDate } from './format'

/** A gap between trains longer than this gets a divider saying so, e.g. overnight. */
export const LONG_GAP_MS = 60 * 60_000

export type BoardRow =
  | { kind: 'train'; key: string; departure: Departure; past: boolean }
  | { kind: 'now'; key: string }
  | { kind: 'day'; key: string; label: string }
  | { kind: 'gap'; key: string; label: string }

export const departureKey = (d: Departure) => `${d.tripId}-${d.scheduledAt}`

/** Whether a train has already gone: it departed, or was cancelled or skipped at a time that has passed. */
export function isPast(d: Departure, now: number) {
  if (d.status === 'Departed') return true
  return (d.status === 'Cancelled' || d.status === 'Skipped') && Date.parse(d.expectedAt) < now - 60_000
}

/**
 * The departures from every loaded page, in order and without repeats (pages can overlap after a
 * refresh), with dividers: "Now" between trains that have gone and those to come, the day when the
 * list crosses midnight, and long gaps such as overnight.
 */
export function boardRows(pages: Board[], now: number): BoardRow[] {
  const seen = new Set<string>()
  const departures = pages.flatMap((p) => p.departures).filter((d) => {
    const key = departureKey(d)
    if (seen.has(key)) return false
    seen.add(key)
    return true
  })

  const rows: BoardRow[] = []
  let previous: Departure | null = null
  for (const d of departures) {
    const past = isPast(d, now)
    if (previous && isPast(previous, now) && !past) rows.push({ kind: 'now', key: 'now' })
    if (previous && !past && Date.parse(d.expectedAt) - Date.parse(previous.expectedAt) > LONG_GAP_MS)
      rows.push({ kind: 'gap', key: `gap-${departureKey(d)}`, label: gapText(previous.expectedAt, d.expectedAt) })

    const newDay = previous ? sydneyDate(d.expectedAt) !== sydneyDate(previous.expectedAt) : sydneyDate(d.expectedAt) !== sydneyDate(now)
    if (newDay) rows.push({ kind: 'day', key: `day-${sydneyDate(d.expectedAt)}`, label: dayLabel(d.expectedAt, now) })

    rows.push({ kind: 'train', key: departureKey(d), departure: d, past })
    previous = d
  }
  return rows
}
