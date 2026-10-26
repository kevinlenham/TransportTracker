import type { LineReport, Stats, TimeBand } from './api'

/**
 * Stats worked out in the browser from a Line's report (/v1/lines/{line}), which has every Station,
 * Direction and Time Band. The API's own totals are plain sums of these, so adding them up here gives
 * the same numbers the API would.
 * TODO(backend): endpoints for these views would save loading a whole Line's report for each one.
 */

/** Below this many Observations a percentage isn't shown. Must match the API's Minimum Sample. */
export const MINIMUM_SAMPLE = 20

export const TIME_BANDS: TimeBand[] = ['AmPeak', 'PmPeak', 'OffPeak', 'Weekend']

const percent = (part: number, whole: number) => Math.round((1000 * part) / whole) / 10

/** The counts added up, with the percentages worked out the way the API does. */
export function sumStats(all: Stats[]): Stats {
  const total = all.reduce(
    (sum, s) => ({
      observed: sum.observed + s.observed,
      late: sum.late + s.late,
      timetabled: sum.timetabled + s.timetabled,
      cancelled: sum.cancelled + s.cancelled,
    }),
    { observed: 0, late: 0, timetabled: 0, cancelled: 0 },
  )
  return {
    ...total,
    latePercent: total.observed >= MINIMUM_SAMPLE ? percent(total.late, total.observed) : null,
    cancellationPercent: total.timetabled >= MINIMUM_SAMPLE ? percent(total.cancelled, total.timetabled) : null,
  }
}

/** A Line at one Station, both Directions together. Null if the Line doesn't stop there. */
export function stationStats(report: LineReport, stationId: string): Stats | null {
  const here = report.directions.flatMap((d) => d.stations.filter((s) => s.stationId === stationId))
  return here.length ? sumStats(here.map((s) => s.stats)) : null
}

/** A Line at one Station in one Direction, per Time Band: what a trip on it can expect. */
export function stationBandStats(
  report: LineReport,
  stationId: string,
  directionId: number | null,
): { timeBand: TimeBand; stats: Stats }[] | null {
  const direction = report.directions.find((d) => d.directionId === directionId)
  return direction?.stations.find((s) => s.stationId === stationId)?.timeBands ?? null
}

/** A whole Line in one Time Band, every Station and Direction together. */
export function lineBandStats(report: LineReport, band: TimeBand): Stats {
  return sumStats(
    report.directions.flatMap((d) => d.stations.flatMap((s) => s.timeBands.filter((b) => b.timeBand === band).map((b) => b.stats))),
  )
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
