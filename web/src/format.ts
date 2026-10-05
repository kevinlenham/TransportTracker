import type { Departure, LateChance, TimeBand } from './api'

const sydneyClock = new Intl.DateTimeFormat('en-AU', {
  timeZone: 'Australia/Sydney',
  hour: 'numeric',
  minute: '2-digit',
})

/** Clock time in Sydney, e.g. "9:05 pm", whatever time zone the rider's device is in. */
export const clockTime = (iso: string) => sydneyClock.format(new Date(iso))

/** "Now", or whole minutes until the train is expected, e.g. "4 min". */
export function minutesUntil(iso: string, now: number): string {
  const minutes = Math.floor((Date.parse(iso) - now) / 60_000)
  return minutes < 1 ? 'Now' : `${minutes} min`
}

/** "Central Station" → "Central". */
export const stationLabel = (name: string) => name.replace(/ Station$/, '')

/** "Central Station Platform 16" → "Platform 16". */
export function platformLabel(platformName: string | null): string | null {
  const match = platformName?.match(/Platform \S+$/)
  return match ? match[0] : null
}

/** The rider-facing status. Trains under a minute behind, or early, are on time. */
export function statusText(d: Pick<Departure, 'status' | 'delaySeconds'>): string {
  switch (d.status) {
    case 'Cancelled':
      return 'Cancelled'
    case 'Skipped':
      return 'Not stopping here'
    case 'Scheduled':
      return 'Scheduled'
    case 'Live':
      return d.delaySeconds !== null && d.delaySeconds >= 60
        ? `Delayed ${Math.round(d.delaySeconds / 60)} min`
        : 'On time'
  }
}

const bandLabels: Record<TimeBand, string> = {
  AmPeak: 'weekday AM peak',
  PmPeak: 'weekday PM peak',
  OffPeak: 'weekday off-peak',
  Weekend: 'weekends',
}

export const timeBandLabel = (band: TimeBand) => bandLabels[band]

/** e.g. "Late 23% of the time", or "Collecting data (12 of 20)" below the Minimum Sample. */
export function lateText(late: LateChance): string {
  return late.latePercent === null
    ? `Collecting data (${late.sampleSize} of ${late.minimumSample})`
    : `Late ${Math.round(late.latePercent)}% of the time`
}

/** A whole-number percentage, or a dash below the Minimum Sample. */
export const percentText = (percent: number | null) => (percent === null ? '–' : `${Math.round(percent)}%`)

/** Black or white, whichever reads better on a Line's colour (a 6-digit hex without #). */
export function textColorOn(hex: string): '#000' | '#fff' {
  const [r, g, b] = [0, 2, 4].map((i) => {
    const c = parseInt(hex.slice(i, i + 2), 16) / 255
    return c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4
  })
  const luminance = 0.2126 * r + 0.7152 * g + 0.0722 * b
  // The point where black and white have equal contrast.
  return luminance > 0.179 ? '#000' : '#fff'
}
