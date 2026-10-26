import type { TimeBand } from '../api'
import { shortTimeBandLabel } from '../format'
import { TIME_BANDS } from '../stats'

/** Underline-style tabs for the Time Bands, sitting on the bottom edge of the band. */
export function TimeBandTabs({ value, onChange }: { value: TimeBand; onChange: (band: TimeBand) => void }) {
  return (
    <div className="tabs" role="tablist" aria-label="Time of day">
      {TIME_BANDS.map((b) => (
        <button
          key={b}
          type="button"
          role="tab"
          aria-selected={b === value}
          aria-controls="band-panel"
          className="tab"
          onClick={() => onChange(b)}
        >
          {shortTimeBandLabel(b)}
        </button>
      ))}
    </div>
  )
}
