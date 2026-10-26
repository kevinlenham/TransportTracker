import { useQuery } from '@tanstack/react-query'
import { useId, useState } from 'react'
import { Link, useSearchParams } from 'react-router'
import type { Departure, Station } from '../api'
import { DepartureTable } from '../components/DepartureTable'
import { Band, Content } from '../components/Page'
import { StationPicker } from '../components/StationPicker'
import { percentText, shortTimeBandLabel, stationLabel, timeBandHours } from '../format'
import { useNow } from '../hooks'
import { lineReportQuery } from '../queries'
import { tripKey, useSavedTrips } from '../savedTrips'
import { isHighLate, stationBandStats, timeBandAt } from '../stats'
import { alertsQuery } from '../tripAlerts'
import { useBoard } from '../useBoard'

/** Alerts listed in the notice before it links to the rest. */
const ALERTS_SHOWN = 2

/** Direct trains between two Stations. The search is in the URL (?from=&to=), so it can be shared or bookmarked. */
export function PlanTrip() {
  const [params, setParams] = useSearchParams()
  const fromId = params.get('from')
  const toId = params.get('to')
  const searching = fromId !== null && toId !== null
  const now = useNow(15_000)
  const whenId = useId()

  const board = useBoard(searching ? fromId : null, searching ? toId : null)
  const first = board.data?.pages[0]

  // The form starts from the trip shown, until the rider changes it.
  const [from, setFrom] = useState<Station | null | undefined>(undefined)
  const [to, setTo] = useState<Station | null | undefined>(undefined)
  const [error, setError] = useState<string | null>(null)
  const fromValue = from !== undefined ? from : (first?.station ?? null)
  const toValue = to !== undefined ? to : (first?.destination ?? null)

  return (
    <>
      <Band title="Plan a trip">
        <form
          className="trip-form"
          onSubmit={(e) => {
            e.preventDefault()
            if (!fromValue || !toValue) return setError('Choose both stations from the list.')
            if (fromValue.id === toValue.id) return setError('Choose two different stations.')
            setError(null)
            setParams({ from: fromValue.id, to: toValue.id })
          }}
        >
          <StationPicker label="From" value={fromValue} onPick={setFrom} className="field-from" autoFocus={!searching} />
          <button
            type="button"
            className="swap-button"
            aria-label="Swap From and To"
            title="Swap From and To"
            onClick={() => {
              setFrom(toValue)
              setTo(fromValue)
            }}
          >
            ⇄
          </button>
          <StationPicker label="To" value={toValue} onPick={setTo} className="field-to" />
          <div className="field field-when">
            <label className="field-label" htmlFor={whenId}>
              When
            </label>
            {/* TODO(backend): Depart at and Arrive by need the departures API to start from a chosen time. */}
            <select id={whenId} className="select" defaultValue="now">
              <option value="now">Leave now</option>
              <option value="depart" disabled>
                Depart at (coming soon)
              </option>
              <option value="arrive" disabled>
                Arrive by (coming soon)
              </option>
            </select>
          </div>
          <button type="submit" className="button">
            Find trains
          </button>
          {error && <p className="form-error">{error}</p>}
        </form>
        <p className="band-links">
          Only want to see what's leaving one station? <Link to="/stations">View station departures</Link>
        </p>
      </Band>

      <Content>{searching ? <Results fromId={fromId} toId={toId} board={board} now={now} /> : <Start />}</Content>
    </>
  )
}

function Results({ fromId, toId, board, now }: { fromId: string; toId: string; board: ReturnType<typeof useBoard>; now: number }) {
  const first = board.data?.pages[0]
  const departures = board.data?.pages.flatMap((p) => p.departures) ?? []
  const lines = [...new Set(departures.map((d) => d.line).filter((l) => l !== null))]
  const alerts = useQuery({ ...alertsQuery({ stations: [fromId, toId], lines }), enabled: board.isSuccess })
  const shownAlerts = alerts.data?.alerts ?? []

  return (
    <div className="with-sidebar">
      <section aria-labelledby="results-heading">
        {shownAlerts.length > 0 && (
          <div className="notice" role="status">
            <p>
              <strong>
                {shownAlerts.length === 1 ? 'A service alert' : `${shownAlerts.length} service alerts`} may affect this trip.
              </strong>
            </p>
            {shownAlerts.slice(0, ALERTS_SHOWN).map((a) => (
              <p key={a.id}>{a.header}</p>
            ))}
            <p>
              <Link to="/alerts">See all alerts</Link>
            </p>
          </div>
        )}

        <h2 id="results-heading" className="section-title results-heading">
          {first?.destination ? `${stationLabel(first.station.name)} to ${stationLabel(first.destination.name)}` : 'Trains'}
        </h2>
        <DepartureTable board={board} now={now} trip />
        <p className="footnote">
          Direct trains only. “Runs late” is how often trains like this one left {first ? stationLabel(first.station.name) : 'here'}{' '}
          more than 2 minutes late over the last 3 weeks.
        </p>
      </section>

      {first?.destination && (
        <aside className="sidebar">
          <TripByTimeOfDay stationId={first.station.id} stationName={stationLabel(first.station.name)} departures={departures} now={now} />
          <SaveTrip from={first.station} to={first.destination} />
        </aside>
      )}
    </div>
  )
}

/**
 * The Late % for this trip in each Time Band, from the Line most of its trains run on. That's the
 * same Stats Bucket each train's own "Runs late" comes from, in every Time Band at once.
 */
function TripByTimeOfDay({ stationId, stationName, departures, now }: {
  stationId: string
  stationName: string
  departures: Departure[]
  now: number
}) {
  const line = mostCommon(departures.map((d) => d.line))
  const directionId = mostCommon(departures.filter((d) => d.line === line).map((d) => d.directionId))
  const report = useQuery({ ...lineReportQuery(line ?? ''), enabled: line !== null })
  const bands = report.data ? stationBandStats(report.data, stationId, directionId) : null
  const current = timeBandAt(now)
  const otherLines = new Set(departures.map((d) => d.line)).size > 1

  if (!line) return null

  return (
    <section>
      <h2 className="section-title">This trip by time of day</h2>
      {report.isPending && <p className="hint">Loading…</p>}
      {report.isError && <p className="hint">Couldn't load the stats for this trip.</p>}
      {report.isSuccess && !bands && <p className="hint">No stats for this trip yet.</p>}
      {bands && (
        <table className="data-table data-table--compact">
          <thead>
            <tr>
              <th scope="col">Time of day</th>
              <th scope="col" className="num">
                Late
              </th>
            </tr>
          </thead>
          <tbody>
            {bands.map(({ timeBand, stats }) => (
              <tr key={timeBand} className={timeBand === current ? 'current' : undefined}>
                <th scope="row">
                  {shortTimeBandLabel(timeBand)}
                  {timeBand === current && <span className="visually-hidden"> (now)</span>}
                  <div className="cell-sub">{timeBandHours[timeBand]}</div>
                </th>
                <td className={`num${isHighLate(stats.latePercent) ? ' tone-warn' : ''}`}>
                  {percentText(stats.latePercent)}
                  <div className="cell-sub">{stats.observed} trains</div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      <p className="footnote">
        {line} trains leaving {stationName}, last 3 weeks.{otherLines && ` Most trains on this trip are ${line}.`} The time of
        day you're in now is in bold.
      </p>
    </section>
  )
}

function SaveTrip({ from, to }: { from: Station; to: Station }) {
  const { trips, add, remove } = useSavedTrips()
  const trip = { fromId: from.id, fromName: from.name, toId: to.id, toName: to.name }
  const saved = trips.some((t) => tripKey(t) === tripKey(trip))

  return saved ? (
    <p>
      Saved to <Link to="/trips">My trips</Link>.{' '}
      <button type="button" className="link-button" onClick={() => remove(trip)}>
        Remove
      </button>
    </p>
  ) : (
    <button type="button" className="button-outline" onClick={() => add(trip)}>
      Save this trip
    </button>
  )
}

/** Before a search: the rider's Saved Trips, one tap from their trains. */
function Start() {
  const { trips } = useSavedTrips()

  return (
    <>
      <p className="hint">Find direct Sydney Trains between two stations, with how often each one runs late.</p>
      {trips.length > 0 && (
        <section>
          <h2 className="section-title">Your saved trips</h2>
          <ul className="plain-list">
            {trips.map((t) => (
              <li key={tripKey(t)}>
                <Link to={`/?${new URLSearchParams({ from: t.fromId, to: t.toId })}`}>
                  {stationLabel(t.fromName)} to {stationLabel(t.toName)}
                </Link>
              </li>
            ))}
          </ul>
        </section>
      )}
    </>
  )
}

function mostCommon<T>(values: T[]): T | null {
  const counts = new Map<T, number>()
  for (const v of values) if (v !== null) counts.set(v, (counts.get(v) ?? 0) + 1)
  let best: T | null = null
  for (const [v, n] of counts) if (best === null || n > counts.get(best)!) best = v
  return best
}
