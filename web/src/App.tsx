import { Link, Route, Routes } from 'react-router'
import { StationDepartures } from './pages/StationDepartures'
import { StationSearch } from './pages/StationSearch'

export default function App() {
  return (
    <>
      <header className="app-header">
        <Link to="/" className="app-name">
          TransportTracker
        </Link>
        <span className="app-tagline">Sydney Trains, and how often they run late</span>
      </header>
      <main className="app-main">
        <Routes>
          <Route path="/" element={<StationSearch />} />
          <Route path="/stations/:stationId" element={<StationDepartures />} />
          <Route path="*" element={<p className="hint">Page not found. <Link to="/">Find a station</Link></p>} />
        </Routes>
      </main>
    </>
  )
}
