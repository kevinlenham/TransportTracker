import { describe, expect, it } from 'vitest'
import type { LateChance } from './api'
import { clockTime, lateText, minutesUntil, platformLabel, stationLabel, statusText, textColorOn } from './format'

describe('statusText', () => {
  it('rounds the delay to whole minutes', () =>
    expect(statusText({ status: 'Live', delaySeconds: 142 })).toBe('Delayed 2 min'))

  it('treats under a minute, or early, as on time', () => {
    expect(statusText({ status: 'Live', delaySeconds: 59 })).toBe('On time')
    expect(statusText({ status: 'Live', delaySeconds: -30 })).toBe('On time')
    expect(statusText({ status: 'Live', delaySeconds: null })).toBe('On time')
  })

  it('names the other statuses', () => {
    expect(statusText({ status: 'Cancelled', delaySeconds: null })).toBe('Cancelled')
    expect(statusText({ status: 'Skipped', delaySeconds: null })).toBe('Not stopping here')
    expect(statusText({ status: 'Scheduled', delaySeconds: null })).toBe('Scheduled')
  })
})

describe('minutesUntil', () => {
  const now = Date.parse('2026-10-05T10:00:00Z')

  it('counts whole minutes', () => expect(minutesUntil('2026-10-05T10:04:59Z', now)).toBe('4 min'))

  it('says now under a minute, including trains that are due', () => {
    expect(minutesUntil('2026-10-05T10:00:59Z', now)).toBe('Now')
    expect(minutesUntil('2026-10-05T09:59:30Z', now)).toBe('Now')
  })
})

describe('lateText', () => {
  const late: LateChance = { timeBand: 'OffPeak', latePercent: 23.4, sampleSize: 140, minimumSample: 20 }

  it('shows the Late % as a whole number', () => expect(lateText(late)).toBe('Late 23% of the time'))

  it('shows progress below the Minimum Sample', () =>
    expect(lateText({ ...late, latePercent: null, sampleSize: 12 })).toBe('Collecting data (12 of 20)'))
})

describe('labels', () => {
  it('drops "Station" from Station names', () => expect(stationLabel('Central Station')).toBe('Central'))

  it('keeps only the platform from a platform name', () => {
    expect(platformLabel('Central Station Platform 16')).toBe('Platform 16')
    expect(platformLabel('Central Station')).toBeNull()
    expect(platformLabel(null)).toBeNull()
  })

  it('shows clock times in Sydney time', () => expect(clockTime('2026-10-05T10:05:00Z')).toBe('9:05 pm'))
})

describe('textColorOn', () => {
  it('picks white on dark Line colours and black on light ones', () => {
    expect(textColorOn('005AA3')).toBe('#fff') // T4 blue
    expect(textColorOn('F99D1C')).toBe('#000') // T1 orange
  })
})
