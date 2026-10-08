import { Link, useParams } from 'react-router'
import { DepartureList } from '../components/DepartureList'
import { stationLabel } from '../format'

/** Every direct train for a Saved Trip, earlier and later, with arrival times. */
export function TripDepartures() {
  const { fromId = '', toId = '' } = useParams()

  return (
    <section>
      <Link to="/trips" className="back">
        ← My trips
      </Link>
      <DepartureList
        stationId={fromId}
        to={toId}
        header={(board) => (
          <h1 className="page-title">
            {stationLabel(board.station.name)} → {board.destination ? stationLabel(board.destination.name) : ''}
          </h1>
        )}
      />
      <p className="footnote">Direct trains only. Late % is how often trains left the first station late over the last 3 weeks.</p>
    </section>
  )
}
