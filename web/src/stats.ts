import type { LineReport, Stats, TimeBand } from './api'

export const TIME_BANDS: TimeBand[] = ['AmPeak', 'PmPeak', 'OffPeak', 'Weekend']

/** A Line at one Station in one Direction, per Time Band: what a trip on it can expect. */
export function stationBandStats(
  report: LineReport,
  stationId: string,
  directionId: number | null,
): { timeBand: TimeBand; stats: Stats }[] | null {
  const direction = report.directions.find((d) => d.directionId === directionId)
  return direction?.stations.find((s) => s.stationId === stationId)?.timeBands ?? null
}

const sydneyParts = new Intl.DateTimeFormat('en-AU', {
  timeZone: 'Australia/Sydney',
  weekday: 'short',
  hour: 'numeric',
  minute: 'numeric',
  hourCycle: 'h23',
})

/** The Time Band a moment falls in, by Sydney time. Peaks include their start but not their end, as in the API. */
export function timeBandAt(when: number): TimeBand {
  const parts = Object.fromEntries(sydneyParts.formatToParts(new Date(when)).map((p) => [p.type, p.value]))
  if (parts.weekday === 'Sat' || parts.weekday === 'Sun') return 'Weekend'
  const minutes = Number(parts.hour) * 60 + Number(parts.minute)
  if (minutes >= 6 * 60 + 30 && minutes < 9 * 60 + 30) return 'AmPeak'
  if (minutes >= 15 * 60 && minutes < 19 * 60) return 'PmPeak'
  return 'OffPeak'
}

/** Whether a Late % is high enough to stand out: a quarter of trains or more. */
export const isHighLate = (latePercent: number | null) => latePercent !== null && latePercent >= 25
