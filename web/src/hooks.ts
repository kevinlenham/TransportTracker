import { useEffect, useLayoutEffect, useRef, useState, type RefObject } from 'react'

/** The value, once it has stopped changing for `delayMs`. */
export function useDebounced<T>(value: T, delayMs: number): T {
  const [debounced, setDebounced] = useState(value)
  useEffect(() => {
    const timer = setTimeout(() => setDebounced(value), delayMs)
    return () => clearTimeout(timer)
  }, [value, delayMs])
  return debounced
}

/**
 * Keeps what the rider is looking at in place when content is added above it. Call the returned
 * `remember` just before adding the content: it notes the first item (`[data-key]` inside the container)
 * on screen and where it sits. When `changed` next changes, the page is scrolled by however far that
 * item moved, before the browser paints, so nothing visibly jumps. Browsers' own scroll anchoring
 * isn't reliable everywhere (notably Safari), hence doing it by hand.
 */
export function useKeepInPlace(container: RefObject<HTMLElement | null>, changed: unknown) {
  const anchor = useRef<{ key: string; top: number } | null>(null)

  useLayoutEffect(() => {
    const remembered = anchor.current
    anchor.current = null
    if (!remembered || !container.current) return
    const item = container.current.querySelector(`[data-key="${CSS.escape(remembered.key)}"]`)
    if (item) window.scrollBy(0, item.getBoundingClientRect().top - remembered.top)
  }, [changed, container])

  return function remember() {
    const items = container.current?.querySelectorAll<HTMLElement>('[data-key]') ?? []
    const onScreen = [...items].find((el) => el.getBoundingClientRect().bottom > 0)
    anchor.current = onScreen ? { key: onScreen.dataset.key!, top: onScreen.getBoundingClientRect().top } : null
  }
}

/** The current time, updated every `intervalMs`, so countdowns move between data refreshes. */
export function useNow(intervalMs: number): number {
  const [now, setNow] = useState(() => Date.now())
  useEffect(() => {
    const timer = setInterval(() => setNow(Date.now()), intervalMs)
    return () => clearInterval(timer)
  }, [intervalMs])
  return now
}
