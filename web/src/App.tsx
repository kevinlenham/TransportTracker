import { Link, Navigate, NavLink, Route, Routes, useParams } from 'react-router'
import { Band, Content } from './components/Page'
import { Alerts } from './pages/Alerts'
import { About, HowWeMeasure, Privacy } from './pages/Info'
import { LineDetail } from './pages/LineDetail'
import { Lines } from './pages/Lines'
import { PlanTrip } from './pages/PlanTrip'
import { SavedTrips } from './pages/SavedTrips'
import { StationDepartures } from './pages/StationDepartures'
import { useTheme } from './theme'
import { useTripAlerts } from './tripAlerts'

const navClass = ({ isActive }: { isActive: boolean }) => (isActive ? 'active' : undefined)

export default function App() {
  const { count } = useTripAlerts()
  const alertsLabel = count > 0 ? `Alerts (${count})` : 'Alerts'

  return (
    <>
      <a href="#main" className="skip-link">
        Skip to content
      </a>
      <Header alertsLabel={alertsLabel} />
      <main id="main" className="main">
        <Routes>
          <Route path="/" element={<PlanTrip />} />
          <Route path="/stations" element={<StationDepartures />} />
          <Route path="/stations/:stationId" element={<StationDepartures />} />
          <Route path="/trips" element={<SavedTrips />} />
          <Route path="/trips/:fromId/:toId" element={<TripRedirect />} />
          <Route path="/lines" element={<Lines />} />
          <Route path="/lines/:line" element={<LineDetail />} />
          <Route path="/alerts" element={<Alerts />} />
          <Route path="/about" element={<About />} />
          <Route path="/how-we-measure-late" element={<HowWeMeasure />} />
          <Route path="/privacy" element={<Privacy />} />
          <Route path="*" element={<NotFound />} />
        </Routes>
      </main>
      <Footer />
      <TabBar alertsLabel={alertsLabel} />
    </>
  )
}

function Header({ alertsLabel }: { alertsLabel: string }) {
  const { theme, toggle } = useTheme()

  return (
    <header className="site-header">
      <div className="container">
        <Link to="/" className="logo">
          <span className="logo-mark" aria-hidden="true" />
          <span className="logo-text">TransportTracker</span>
        </Link>
        <nav className="site-nav" aria-label="Main">
          <NavLink to="/" end className={navClass}>
            Plan a trip
          </NavLink>
          <NavLink to="/stations" className={navClass}>
            Station departures
          </NavLink>
          <NavLink to="/trips" className={navClass}>
            My trips
          </NavLink>
          <NavLink to="/lines" className={navClass}>
            Line stats
          </NavLink>
          <NavLink to="/alerts" className={navClass}>
            {alertsLabel}
          </NavLink>
        </nav>
        <div className="header-end">
          {/* On phones the tab bar has no room for Line stats, so it's here instead. */}
          <Link to="/lines" className="header-mobile-link">
            Line stats
          </Link>
          <button type="button" className="theme-toggle" onClick={toggle}>
            {theme === 'dark' ? 'Light mode' : 'Dark mode'}
          </button>
        </div>
      </div>
    </header>
  )
}

/** On phones, the main pages as a bar along the bottom of the screen. */
function TabBar({ alertsLabel }: { alertsLabel: string }) {
  return (
    <nav className="tab-bar" aria-label="Main">
      <NavLink to="/" end className={navClass}>
        Plan
      </NavLink>
      <NavLink to="/stations" className={navClass}>
        Stations
      </NavLink>
      <NavLink to="/trips" end className={navClass}>
        My trips
      </NavLink>
      <NavLink to="/alerts" className={navClass}>
        {alertsLabel}
      </NavLink>
    </nav>
  )
}

function Footer() {
  return (
    <footer className="site-footer">
      <div className="container">
        <p>Train data from Transport for NSW. Times are Sydney time.</p>
        <nav className="footer-links" aria-label="About this site">
          <Link to="/about">About</Link>
          <Link to="/how-we-measure-late">How we measure late</Link>
          <Link to="/privacy">Privacy</Link>
        </nav>
      </div>
    </footer>
  )
}

/** Old links to a Saved Trip's trains now open Plan a trip with that trip. */
function TripRedirect() {
  const { fromId = '', toId = '' } = useParams()
  return <Navigate to={`/?${new URLSearchParams({ from: fromId, to: toId })}`} replace />
}

function NotFound() {
  return (
    <>
      <Band title="Page not found" />
      <Content>
        <p>
          <Link to="/">Plan a trip</Link> or <Link to="/stations">see a station's departures</Link>.
        </p>
      </Content>
    </>
  )
}
