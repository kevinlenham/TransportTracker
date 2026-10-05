import { textColorOn } from '../format'

export function LineBadge({ line, color }: { line: string | null; color: string | null }) {
  const background = color ?? '888888'
  return (
    <span className="line-badge" style={{ background: `#${background}`, color: textColorOn(background) }}>
      {line ?? '?'}
    </span>
  )
}
