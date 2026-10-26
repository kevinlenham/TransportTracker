import type { ReactNode } from 'react'

/** The strip under the header with the page title and the page's form or controls. */
export function Band({ title, children }: { title: ReactNode; children?: ReactNode }) {
  return (
    <div className="band">
      <div className="container">
        <h1 className="page-title">{title}</h1>
        {children}
      </div>
    </div>
  )
}

/** The page's content under the band. */
export function Content({ children }: { children: ReactNode }) {
  return (
    <div className="content">
      <div className="container">{children}</div>
    </div>
  )
}
