import { describe, expect, it } from 'vitest'
import type { Board, Departure, DepartureStatus } from './api'
import { boardRows } from './boardRows'

// 11:30 pm Friday 9 October in Sydney (AEDT).
const now = Date.parse('2026-10-09T12:30:00Z')

function train(tripId: string, expectedAt: string, status: DepartureStatus = 'Scheduled'): Departure {
  return {
    tripId, line: 'T1', lineColor: null, headsign: null, directionId: 0, platformId: 'P', platformName: null,
    scheduledAt: expectedAt, expectedAt, delaySeconds: null, status, arrival: null,
    late: { timeBand: 'Weekend', latePercent: null, sampleSize: 0, minimumSample: 20 },
  }
}

const page = (...departures: Departure[]): Board => ({
  station: { id: 'S', name: 'S', lat: 0, lon: 0 }, destination: null, generatedAt: '', feedTime: null,
  departures, earlier: null, later: null,
})

const shape = (pages: Board[]) => boardRows(pages, now).map((r) => (r.kind === 'train' ? r.departure.tripId : `[${r.kind}${'label' in r ? `: ${r.label}` : ''}]`))

describe('boardRows', () => {
  it('puts a Now divider between trains that have gone and those to come', () =>
    expect(shape([page(train('A', '2026-10-09T12:20:00Z', 'Departed')), page(train('B', '2026-10-09T12:40:00Z'))]))
      .toEqual(['A', '[now]', 'B']))

  it('marks midnight and long overnight gaps', () =>
    expect(shape([page(train('A', '2026-10-09T12:50:00Z'), train('B', '2026-10-09T17:41:00Z'))])).toEqual([
      'A',
      '[gap: No trains 11:50 pm – 4:41 am]',
      '[day: Tomorrow, Saturday 10 Oct]',
      'B',
    ]))

  it('starts with the day when the first train is not today', () =>
    expect(shape([page(train('A', '2026-10-09T17:41:00Z'))])).toEqual(['[day: Tomorrow, Saturday 10 Oct]', 'A']))

  it('drops trains repeated across overlapping pages', () =>
    expect(shape([page(train('A', '2026-10-09T12:40:00Z'), train('B', '2026-10-09T12:45:00Z')), page(train('B', '2026-10-09T12:45:00Z'))]))
      .toEqual(['A', 'B']))

  it('treats a cancellation whose time has passed as gone', () =>
    expect(shape([page(train('A', '2026-10-09T12:00:00Z', 'Cancelled'), train('B', '2026-10-09T12:40:00Z'))]))
      .toEqual(['A', '[now]', 'B']))
})
