import type { Departure, LateChance, TimeBand } from './api'

const sydneyClock = new Intl.DateTimeFormat('en-AU', {
  timeZone: 'Australia/Sydney',
  hour: 'numeric',
  minute: '2-digit',
})

/** Clock time in Sydney, e.g. "9:05 pm", whatever time zone the rider's device is in. */
export const clockTime = (iso: string) => sydneyClock.format(new Date(iso))

const sydneyDay = new Intl.DateTimeFormat('en-CA', { timeZone: 'Australia/Sydney' }) // YYYY-MM-DD
const sydneyWeekday = new Intl.DateTimeFormat('en-AU', { timeZone: 'Australia/Sydney', weekday: 'long', day: 'numeric', month: 'short' })

/** The calendar day in Sydney, as YYYY-MM-DD, for telling when a list crosses midnight. */
export const sydneyDate = (iso: string | number) => sydneyDay.format(new Date(iso))

/** "Today", "Tomorrow, Friday 9 Oct", "Yesterday, …", or the weekday, for a day divider. */
export function dayLabel(iso: string, now: number): string {
  const day = Date.parse(`${sydneyDate(iso)}T00:00:00Z`)
  const today = Date.parse(`${sydneyDate(now)}T00:00:00Z`)
  const offset = Math.round((day - today) / 86_400_000)
  const date = sydneyWeekday.format(new Date(iso))
  if (offset === 0) return 'Today'
  if (offset === 1) return `Tomorrow, ${date}`
  if (offset === -1) return `Yesterday, ${date}`
  return date
}

/** "No trains 1:05 am – 4:41 am" for a long gap between two departures. */
export const gapText = (fromIso: string, toIso: string) => `No trains ${clockTime(fromIso)} – ${clockTime(toIso)}`

/** "Now", or the time until the train is expected, e.g. "4 min" or "4 h 36 min". */
export function minutesUntil(iso: string, now: number): string {
  const minutes = Math.floor((Date.parse(iso) - now) / 60_000)
  if (minutes < 1) return 'Now'
  if (minutes < 60) return `${minutes} min`
  const rest = minutes % 60
  return rest ? `${Math.floor(minutes / 60)} h ${rest} min` : `${minutes / 60} h`
}

/** "Central Station" → "Central". */
export const stationLabel = (name: string) => name.replace(/ Station$/, '')

/** "Central Station Platform 16" → "Platform 16". */
export function platformLabel(platformName: string | null): string | null {
  const match = platformName?.match(/Platform \S+$/)
  return match ? match[0] : null
}

/** Whether a train is (or was) running at least a minute behind the Timetable. */
export const isRunningLate = (d: Pick<Departure, 'status' | 'delaySeconds'>) =>
  (d.status === 'Live' || d.status === 'Departed') && (d.delaySeconds ?? 0) >= 60

/** The rider-facing status. Trains under a minute behind, or early, are on time. */
export function statusText(d: Pick<Departure, 'status' | 'delaySeconds'>): string {
  const minutes = Math.round((d.delaySeconds ?? 0) / 60)
  switch (d.status) {
    case 'Cancelled':
      return 'Cancelled'
    case 'Skipped':
      return 'Not stopping here'
    case 'Scheduled':
      return 'Timetabled'
    case 'Departed':
      if (d.delaySeconds === null) return 'Departed'
      return isRunningLate(d) ? `Departed ${minutes} min late` : 'Departed on time'
    case 'Live':
      return isRunningLate(d) ? `Running ${minutes} min late` : 'On time'
  }
}

export type Tone = 'good' | 'warn' | 'bad' | 'muted'

/** The colour a status is shown in. */
export function statusTone(d: Pick<Departure, 'status' | 'delaySeconds'>): Tone {
  if (d.status === 'Cancelled' || d.status === 'Skipped') return 'bad'
  if (isRunningLate(d)) return 'warn'
  if (d.status === 'Live' || (d.status === 'Departed' && d.delaySeconds !== null)) return 'good'
  return 'muted'
}

/** "27 min" or "1 h 5 min" between two times. */
export function durationText(fromIso: string, toIso: string): string {
  const minutes = Math.max(0, Math.round((Date.parse(toIso) - Date.parse(fromIso)) / 60_000))
  if (minutes < 60) return `${minutes} min`
  const rest = minutes % 60
  return rest ? `${Math.floor(minutes / 60)} h ${rest} min` : `${minutes / 60} h`
}

const bandLabels: Record<TimeBand, string> = {
  AmPeak: 'weekday AM peak',
  PmPeak: 'weekday PM peak',
  OffPeak: 'weekday off-peak',
  Weekend: 'weekends',
}

export const timeBandLabel = (band: TimeBand) => bandLabels[band]

const shortBandLabels: Record<TimeBand, string> = {
  AmPeak: 'AM peak',
  PmPeak: 'PM peak',
  OffPeak: 'Off-peak',
  Weekend: 'Weekends',
}

/** "AM peak", "PM peak", "Off-peak" or "Weekends", for tabs and table rows. */
export const shortTimeBandLabel = (band: TimeBand) => shortBandLabels[band]

/** The hours each Time Band covers. */
export const timeBandHours: Record<TimeBand, string> = {
  AmPeak: 'Weekdays 6:30–9:30 am',
  PmPeak: 'Weekdays 3–7 pm',
  OffPeak: 'Other weekday times',
  Weekend: 'Saturday and Sunday',
}

// TODO(backend): /v1/lines doesn't return Line names yet, so they're kept here for now.
const lineNames: Record<string, string> = {
  T1: 'North Shore & Western Line',
  T2: 'Leppington & Inner West Line',
  T3: 'Liverpool & Inner West Line',
  T4: 'Eastern Suburbs & Illawarra Line',
  T5: 'Cumberland Line',
  T6: 'Lidcombe & Bankstown Line',
  T7: 'Olympic Park Line',
  T8: 'Airport & South Line',
  T9: 'Northern Line',
  BMT: 'Blue Mountains Line',
  CCN: 'Central Coast & Newcastle Line',
  HUN: 'Hunter Line',
  SCO: 'South Coast Line',
  SHL: 'Southern Highlands Line',
}

/** e.g. "North Shore & Western Line" for T1, or null for a Line without a known name. */
export const lineName = (line: string): string | null => lineNames[line] ?? null

/** e.g. "12% of trains (63)", or "Collecting data (12 of 20)" below the Minimum Sample. */
export function runsLateText(late: LateChance): string {
  return late.latePercent === null
    ? `Collecting data (${late.sampleSize} of ${late.minimumSample})`
    : `${Math.round(late.latePercent)}% of trains (${late.sampleSize})`
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
