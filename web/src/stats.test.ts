import { describe, expect, it } from 'vitest'
import type { LineReport, Stats, TimeBand } from './api'
import { stationBandStats, timeBandAt } from './stats'

const observed = (n: number): Stats => ({
  observed: n, late: 0, timetabled: n, cancelled: 0, latePercent: null, cancellationPercent: null,
})

const station = (stationId: string, am: number, pm: number) => ({
  stationId,
  name: stationId,
  stats: observed(am + pm),
  timeBands: (['AmPeak', 'PmPeak', 'OffPeak', 'Weekend'] as TimeBand[]).map((timeBand, i) => ({ timeBand, stats: observed([am, pm, 0, 0][i]) })),
})

const report: LineReport = {
  line: 'T1',
  color: null,
  stats: observed(0),
  directions: [
    { directionId: 0, headsigns: [], stats: observed(0), stations: [station('A', 30, 10)] },
    { directionId: 1, headsigns: [], stats: observed(0), stations: [station('A', 20, 0)] },
  ],
}

describe('stationBandStats', () => {
  it('gives one Direction at a Station by Time Band', () =>
    expect(stationBandStats(report, 'A', 1)?.map((b) => b.stats.observed)).toEqual([20, 0, 0, 0]))

  it('says when the Line does not stop there in that Direction', () => {
    expect(stationBandStats(report, 'Z', 0)).toBeNull()
    expect(stationBandStats(report, 'A', null)).toBeNull()
  })
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
