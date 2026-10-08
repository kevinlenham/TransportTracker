import { Link, useParams } from 'react-router'
import { DepartureList } from '../components/DepartureList'
import { stationLabel } from '../format'

export function StationDepartures() {
  const { stationId = '' } = useParams()

  return (
    <section>
      <Link to="/" className="back">
        ← Stations
      </Link>
      <DepartureList
        stationId={stationId}
        header={(board) => <h1 className="page-title">{stationLabel(board.station.name)}</h1>}
      />
      <p className="footnote">
        Late means more than 2 minutes behind the timetable. Late % comes from the last 3 weeks of this station’s trains
        on the same line, direction and time of week.
      </p>
    </section>
  )
}
