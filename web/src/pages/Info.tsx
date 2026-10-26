import { Link } from 'react-router'
import { Band, Content } from '../components/Page'

export function About() {
  return (
    <>
      <Band title="About TransportTracker" />
      <Content>
        <div className="prose">
          <p>
            TransportTracker shows live Sydney Trains departures alongside something the official apps don't: how often each
            service has actually run late over the last 3 weeks.
          </p>
          <p>
            It checks the Transport for NSW realtime feed every 30 seconds, records how late every train was at every station,
            and turns that into a Late % for the line, direction and time of the week you're travelling in.
          </p>
          <p>
            It covers Sydney Trains only for now. It isn't run by or affiliated with Transport for NSW. For official
            information, see <a href="https://transportnsw.info">transportnsw.info</a>.
          </p>
        </div>
      </Content>
    </>
  )
}

export function HowWeMeasure() {
  return (
    <>
      <Band title="How we measure late" />
      <Content>
        <div className="prose">
          <h2>What counts as late</h2>
          <p>
            A train is late at a station if it leaves more than 2 minutes after its timetabled time. We record how late it was
            the last time the realtime feed reported it at that station.
          </p>
          <h2>Cancelled trains</h2>
          <p>
            Cancelled trains, and trains that skip a station, are counted separately as cancellations. They're never counted as
            late.
          </p>
          <h2>Like-for-like comparisons</h2>
          <p>A train's Late % is worked out from trains at the same station, on the same line, going the same way, in the same time band:</p>
          <ul>
            <li>Weekday AM peak, 6:30–9:30 am</li>
            <li>Weekday PM peak, 3–7 pm</li>
            <li>Other weekday times (off-peak)</li>
            <li>Weekends</li>
          </ul>
          <h2>How much data</h2>
          <p>
            Stats cover the last 3 weeks. We only show a percentage once there are at least 20 trains to base it on. Until
            then you'll see “collecting data” or a dash. The number of trains is always shown next to the percentage.
          </p>
          <p>
            <Link to="/lines">See the stats for every line</Link>
          </p>
        </div>
      </Content>
    </>
  )
}

export function Privacy() {
  return (
    <>
      <Band title="Privacy" />
      <Content>
        <div className="prose">
          <p>There are no accounts, and no advertising or tracking cookies.</p>
          <p>
            Your saved trips and your Dark mode or Light mode choice are stored in your browser on this device only. They
            never leave it. Clearing your browser's site data removes them.
          </p>
          <p>
            When you search for a station or load departures, your browser asks our server for them. Like most websites, our
            hosting providers may keep standard server logs, such as IP addresses and the pages requested.
          </p>
        </div>
      </Content>
    </>
  )
}
