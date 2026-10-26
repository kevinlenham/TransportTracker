import { useEffect, useState } from 'react'

export type Theme = 'light' | 'dark'

const KEY = 'transporttracker.theme'
const darkQuery = () => window.matchMedia('(prefers-color-scheme: dark)')

function saved(): Theme | null {
  try {
    const value = localStorage.getItem(KEY)
    return value === 'light' || value === 'dark' ? value : null
  } catch {
    return null
  }
}

/**
 * The theme in use: the rider's saved choice, otherwise the device's setting. `toggle` switches to the
 * other one and saves it. index.html applies a saved choice before the page paints.
 */
export function useTheme() {
  const [choice, setChoice] = useState<Theme | null>(saved)
  const [systemDark, setSystemDark] = useState(() => darkQuery().matches)

  useEffect(() => {
    const query = darkQuery()
    const onChange = () => setSystemDark(query.matches)
    query.addEventListener('change', onChange)
    return () => query.removeEventListener('change', onChange)
  }, [])

  const theme: Theme = choice ?? (systemDark ? 'dark' : 'light')

  return {
    theme,
    toggle() {
      const next: Theme = theme === 'dark' ? 'light' : 'dark'
      document.documentElement.dataset.theme = next
      try {
        localStorage.setItem(KEY, next)
      } catch {
        // Applies for this visit only.
      }
      setChoice(next)
    },
  }
}
