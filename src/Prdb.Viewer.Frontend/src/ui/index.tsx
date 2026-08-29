import type { FormEvent, ReactNode } from 'react'

/// The small vocabulary every screen is written in. It is deliberately plain: a screen that needs
/// something these do not offer says so by naming its own element rather than by growing a
/// variant here that only one caller passes.

export function CenteredCard({ children }: { children: ReactNode }) {
  return <main className="shell"><section className="card">{children}</section></main>
}

export function Brand({ compact = false }: { compact?: boolean }) {
  return <div className={compact ? 'brand compact' : 'brand'}><span aria-hidden="true">▶</span><h1>prdb-viewer</h1></div>
}

export function Field({ label, ...props }: React.InputHTMLAttributes<HTMLInputElement> & { label: string }) {
  return <label className="field"><span>{label}</span><input {...props} /></label>
}

export function Tab({ active, children, onClick }: { active: boolean; children: ReactNode; onClick: () => void }) {
  return <button type="button" className={active ? 'tab active' : 'tab'} aria-pressed={active} onClick={onClick}>{children}</button>
}

export function SubmitButton({ pending, children }: { pending: boolean; children: ReactNode }) {
  return <button className="primary-button" type="submit" disabled={pending}>{pending ? 'Working…' : children}</button>
}

export function Notice({ kind, children }: { kind: 'error' | 'success'; children: ReactNode }) {
  return <div className={`notice ${kind}`} role={kind === 'error' ? 'alert' : 'status'}>{children}</div>
}

export function RequestError() {
  return <Notice kind="error">The request could not be completed. Try again.</Notice>
}

/// The heading every page carries. A page states what it is and what it is for in one place, so
/// the shell never has to guess a title from the route.
export function PageHeading({ eyebrow, title, children, actions }: {
  eyebrow?: string
  title: string
  children?: ReactNode
  actions?: ReactNode
}) {
  return (
    <div className="page-heading">
      <div>
        {eyebrow && <span className="eyebrow">{eyebrow}</span>}
        <h2>{title}</h2>
        {children && <p>{children}</p>}
      </div>
      {actions && <div className="page-actions">{actions}</div>}
    </div>
  )
}

export function values<T>(form: HTMLFormElement, keys: string[]): T {
  const data = new FormData(form)
  return Object.fromEntries(keys.map((key) => [key, data.get(key)?.toString() || null])) as T
}

export function submitting(handler: (form: HTMLFormElement) => void) {
  return (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    handler(event.currentTarget)
  }
}
