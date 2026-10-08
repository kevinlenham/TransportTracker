import { describe, expect, it } from 'vitest'
import type { LateChance } from './api'
import { clockTime, dayLabel, gapText, lateText, minutesUntil, platformLabel, stationLabel, statusText, sydneyDate, textColorOn } from './format'

describe('statusText', () => {
  it('rounds the delay to whole minutes', () =>
    expect(statusText({ status: 'Live', delaySeconds: 142 })).toBe('Delayed 2 min'))

  it('treats under a minute, or early, as on time', () => {
    expect(statusText({ status: 'Live', delaySeconds: 59 })).toBe('On time')
    expect(statusText({ status: 'Live', delaySeconds: -30 })).toBe('On time')
    expect(statusText({ status: 'Live', delaySeconds: null })).toBe('On time')
  })

  it('says how late a train that has left was, if it was recorded', () => {
    expect(statusText({ status: 'Departed', delaySeconds: 240 })).toBe('Departed 4 min late')
    expect(statusText({ status: 'Departed', delaySeconds: 20 })).toBe('Departed on time')
    expect(statusText({ status: 'Departed', delaySeconds: null })).toBe('Departed')
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

describe('days and gaps', () => {
  // 10:30 pm Friday 9 October in Sydney (AEDT).
  const now = Date.parse('2026-10-09T11:30:00Z')

  it('labels days relative to today in Sydney', () => {
    expect(dayLabel('2026-10-09T12:50:00Z', now)).toBe('Today') // 11:50 pm
    expect(dayLabel('2026-10-09T13:10:00Z', now)).toBe('Tomorrow, Saturday 10 Oct') // 12:10 am
    expect(dayLabel('2026-10-08T22:00:00Z', now)).toBe('Today') // 9 am
    expect(dayLabel('2026-10-08T12:00:00Z', now)).toBe('Yesterday, Thursday 8 Oct')
  })

  it('compares calendar days in Sydney, not UTC', () =>
    expect(sydneyDate('2026-10-09T13:10:00Z')).toBe('2026-10-10'))

  it('describes a gap with both times', () =>
    expect(gapText('2026-10-09T14:05:00Z', '2026-10-09T17:41:00Z')).toBe('No trains 1:05 am – 4:41 am'))
})

describe('textColorOn', () => {
  it('picks white on dark Line colours and black on light ones', () => {
    expect(textColorOn('005AA3')).toBe('#fff') // T4 blue
    expect(textColorOn('F99D1C')).toBe('#000') // T1 orange
  })
})
