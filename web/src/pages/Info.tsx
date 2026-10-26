import type { CSSProperties, ReactNode } from 'react'
import { Link } from 'react-router'
import { Band, Content } from '../components/Page'

/** A text page: an intro across the page, then its sections as blocks in columns. */
function InfoPage({ title, intro, columns = 2, children }: { title: string; intro: ReactNode; columns?: number; children: ReactNode }) {
  return (
    <>
      <Band title={title}>
        <p className="band-text info-intro">{intro}</p>
      </Band>
      <Content>
        <div className="info-grid" style={{ "--columns": columns } as CSSProperties}>
          {children}
        </div>
      </Content>
    </>
  )
}

function Block({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="info-block">
      <h2 className="section-title">{title}</h2>
      {children}
    </section>
  )
}

export function About() {
  return (
    <InfoPage
      title="About TransportTracker"
      columns={3}
      intro="Live Sydney Trains departures, alongside something the official apps don't show: how often each service has actually run late over the last 3 weeks."
    >
      <Block title="How it works">
        <p>
          It checks the Transport for NSW realtime feed every 30 seconds and records how late every train was at every
          station. That becomes a Late % for the line, direction and time of the week you're travelling in.
        </p>
      </Block>
      <Block title="What it covers">
        <p>
          Sydney Trains only for now, with live departures, saved trips, line stats and service alerts. Buses and light rail
          may come later.
        </p>
      </Block>
      <Block title="Who runs it">
        <p>
          It's an independent project, not run by or affiliated with Transport for NSW. For official information, see{' '}
          <a href="https://transportnsw.info">transportnsw.info</a>.
        </p>
      </Block>
    </InfoPage>
  )
}

export function HowWeMeasure() {
  return (
    <InfoPage
      title="How we measure late"
      intro="Every Late % on this site comes from trains we've recorded ourselves, compared like for like with the trip you're taking."
    >
      <Block title="What counts as late">
        <p>
          A train is late at a station if it leaves more than 2 minutes after its timetabled time. We record how late it was
          the last time the realtime feed reported it at that station.
        </p>
      </Block>
      <Block title="Cancelled trains">
        <p>
          Cancelled trains, and trains that skip a station, are counted separately as cancellations. They're never counted as
          late.
        </p>
      </Block>
      <Block title="Like-for-like comparisons">
        <p>A train's Late % comes from trains at the same station, on the same line, going the same way, in the same time band:</p>
        <ul>
          <li>Weekday AM peak, 6:30–9:30 am</li>
          <li>Weekday PM peak, 3–7 pm</li>
          <li>Other weekday times (off-peak)</li>
          <li>Weekends</li>
        </ul>
      </Block>
      <Block title="How much data">
        <p>
          Stats cover the last 3 weeks. We only show a percentage once there are at least 20 trains to base it on. Until
          then you'll see “collecting data” or a dash. The number of trains is always shown next to the percentage.
        </p>
        <p>
          <Link to="/lines">See the stats for every line</Link>
        </p>
      </Block>
    </InfoPage>
  )
}

export function Privacy() {
  return (
    <InfoPage title="Privacy" intro="There are no accounts, and no advertising or tracking cookies.">
      <Block title="Stored on your device">
        <p>
          Your saved trips and your Dark mode or Light mode choice are kept in your browser on this device only. They never
          leave it. Clearing your browser's site data removes them.
        </p>
      </Block>
      <Block title="What our server sees">
        <p>
          When you search for a station or load departures, your browser asks our server for them. Like most websites, our
          hosting providers may keep standard server logs, such as IP addresses and the pages requested.
        </p>
      </Block>
    </InfoPage>
  )
}
