import { Link, NavLink, Route, Routes } from 'react-router'
import { Alerts } from './pages/Alerts'
import { LineDetail } from './pages/LineDetail'
import { Lines } from './pages/Lines'
import { SavedTrips } from './pages/SavedTrips'
import { StationDepartures } from './pages/StationDepartures'
import { StationSearch } from './pages/StationSearch'
import { TripDepartures } from './pages/TripDepartures'

const navLinkClass = ({ isActive }: { isActive: boolean }) => (isActive ? 'nav-link nav-link--active' : 'nav-link')

export default function App() {
  return (
    <>
      <header className="app-header">
        <div className="app-brand">
          <Link to="/" className="app-name">
            TransportTracker
          </Link>
          <span className="app-tagline">Sydney Trains, and how often they run late</span>
        </div>
        <nav className="app-nav" aria-label="Main">
          <NavLink to="/" end className={navLinkClass}>
            Departures
          </NavLink>
          <NavLink to="/trips" className={navLinkClass}>
            My trips
          </NavLink>
          <NavLink to="/lines" className={navLinkClass}>
            Lines
          </NavLink>
          <NavLink to="/alerts" className={navLinkClass}>
            Alerts
          </NavLink>
        </nav>
      </header>
      <main className="app-main">
        <Routes>
          <Route path="/" element={<StationSearch />} />
          <Route path="/stations/:stationId" element={<StationDepartures />} />
          <Route path="/trips" element={<SavedTrips />} />
          <Route path="/trips/:fromId/:toId" element={<TripDepartures />} />
          <Route path="/lines" element={<Lines />} />
          <Route path="/lines/:line" element={<LineDetail />} />
          <Route path="/alerts" element={<Alerts />} />
          <Route path="*" element={<p className="hint">Page not found. <Link to="/">Find a station</Link></p>} />
        </Routes>
      </main>
    </>
  )
}
