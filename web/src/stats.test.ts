import { describe, expect, it } from 'vitest'
import type { LineReport, Stats, TimeBand } from './api'
import { lineBandStats, stationBandStats, stationStats, sumStats, timeBandAt } from './stats'

const counts = (observed: number, late: number, timetabled = observed, cancelled = 0): Stats => ({
  observed, late, timetabled, cancelled, latePercent: null, cancellationPercent: null,
})

const bands = (am: Stats, pm: Stats, off: Stats, weekend: Stats) =>
  (['AmPeak', 'PmPeak', 'OffPeak', 'Weekend'] as TimeBand[]).map((timeBand, i) => ({ timeBand, stats: [am, pm, off, weekend][i] }))

const station = (stationId: string, b: ReturnType<typeof bands>) => ({
  stationId, name: stationId, stats: sumStats(b.map((x) => x.stats)), timeBands: b,
})

const none = counts(0, 0)

const report: LineReport = {
  line: 'T1',
  color: null,
  stats: counts(0, 0),
  directions: [
    { directionId: 0, headsigns: [], stats: none, stations: [station('A', bands(counts(30, 6), counts(10, 1), none, none)), station('B', bands(counts(10, 2), none, none, none))] },
    { directionId: 1, headsigns: [], stats: none, stations: [station('A', bands(counts(20, 4, 25, 5), none, none, none))] },
  ],
}

describe('sumStats', () => {
  it('adds the counts and works out percentages like the API', () =>
    expect(sumStats([counts(30, 6), counts(10, 1, 12, 2)])).toEqual({
      observed: 40, late: 7, timetabled: 42, cancelled: 2, latePercent: 17.5, cancellationPercent: 4.8,
    }))

  it('leaves percentages out below the Minimum Sample', () =>
    expect(sumStats([counts(19, 5)])).toMatchObject({ latePercent: null, cancellationPercent: null }))
})

describe('line stats from a report', () => {
  it('combines both Directions at a Station', () =>
    expect(stationStats(report, 'A')).toMatchObject({ observed: 60, late: 11, timetabled: 65, cancelled: 5 }))

  it('says when a Line does not stop at a Station', () => expect(stationStats(report, 'Z')).toBeNull())

  it('gives one Direction at a Station by Time Band', () =>
    expect(stationBandStats(report, 'A', 0)?.map((b) => b.stats.observed)).toEqual([30, 10, 0, 0]))

  it('adds a whole Line up for one Time Band', () =>
    expect(lineBandStats(report, 'AmPeak')).toMatchObject({ observed: 60, late: 12, latePercent: 20 }))
})

describe('timeBandAt', () => {
  // Sydney is UTC+11 in October (AEDT). Monday 26 October 2026.
  it('finds the peaks by Sydney time, including their start but not their end', () => {
    expect(timeBandAt(Date.parse('2026-10-25T19:30:00Z'))).toBe('AmPeak') // 6:30 am
    expect(timeBandAt(Date.parse('2026-10-25T22:30:00Z'))).toBe('OffPeak') // 9:30 am
    expect(timeBandAt(Date.parse('2026-10-26T04:00:00Z'))).toBe('PmPeak') // 3:00 pm
    expect(timeBandAt(Date.parse('2026-10-26T08:00:00Z'))).toBe('OffPeak') // 7:00 pm
  })

  it('treats Saturday and Sunday in Sydney as the weekend', () => {
    expect(timeBandAt(Date.parse('2026-10-24T20:00:00Z'))).toBe('Weekend') // 7 am Sunday
    expect(timeBandAt(Date.parse('2026-10-25T13:30:00Z'))).toBe('OffPeak') // 12:30 am Monday, still Sunday in UTC
  })
})
