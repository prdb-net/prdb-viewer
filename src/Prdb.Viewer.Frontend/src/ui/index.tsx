import { useEffect } from 'react'
import type { FormEvent, ReactNode } from 'react'

import { RequestFailure } from '../api/client'

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

/// What went wrong, in the API's own words wherever it had any.
///
/// A refusal usually says what to do about it, and that instruction is more use than a generic
/// apology — particularly when the generic one ("try again") is advice that cannot work.
export function RequestError({ error }: { error?: unknown }) {
  const detail = error instanceof RequestFailure ? error.detail : undefined
  return <Notice kind="error">{detail ?? 'The request could not be completed. Try again.'}</Notice>
}

/// The first of the several failures a screen watches that actually happened. A screen shows one
/// notice, so it should be the one belonging to whatever failed.
export function firstError(...candidates: unknown[]): unknown {
  return candidates.find((candidate) => candidate !== null && candidate !== undefined)
}

/// The window's own name for the screen that is open.
///
/// A page that states its title already knows what the tab, the history entry and the bookmark
/// should say. Leaving all three reading "prdb-viewer" made every address look identical in the
/// one place a person goes back through them, which is a strange thing for an application whose
/// screens each have an address of their own.
function useDocumentTitle(title: string) {
  useEffect(() => {
    document.title = `${title} · prdb-viewer`
    return () => { document.title = 'prdb-viewer' }
  }, [title])
}

/// The heading every page carries. A page states what it is and what it is for in one place, so
/// the shell never has to guess a title from the route — and neither does the browser.
export function PageHeading({ eyebrow, title, children, actions }: {
  eyebrow?: string
  title: string
  children?: ReactNode
  actions?: ReactNode
}) {
  useDocumentTitle(title)

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

/// The mark a personal reference is kept under, wherever one is offered: a Video's card, an
/// Actor's card, an Actor's own page.
export function HeartIcon() {
  return (
    <svg viewBox="0 0 24 24" width="16" height="16" aria-hidden="true" focusable="false">
      <path
        d="M12 21s-7.5-4.6-9.5-9.2C1 8 3.4 4.5 7 4.5c2 0 3.5 1.1 5 2.9 1.5-1.8 3-2.9 5-2.9 3.6 0 6 3.5 4.5 7.3C19.5 16.4 12 21 12 21z"
        fill="currentColor"
      />
    </svg>
  )
}
