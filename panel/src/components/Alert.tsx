import type { ReactNode } from 'react'
import { errorDetails, errorMessage, type StatusMessages } from '../lib/errors'

interface NoticeProps {
  tone?: 'info' | 'success' | 'warning' | 'danger'
  title?: string
  children?: ReactNode
}

/** A calm message box. Danger notices are announced right away; the others politely. */
export function Notice({ tone = 'info', title, children }: NoticeProps) {
  return (
    <div className={`notice notice-${tone}`} role={tone === 'danger' ? 'alert' : 'status'}>
      {title && <p className="notice-title">{title}</p>}
      {children}
    </div>
  )
}

/** Shows why a request failed, with any validation messages the server listed. */
export function ErrorAlert({ error, title, messages }: { error: unknown; title?: string; messages?: StatusMessages }) {
  if (!error) return null
  const details = errorDetails(error, messages)
  return (
    <Notice tone="danger" title={title}>
      <p>{errorMessage(error, messages)}</p>
      {details.length > 0 && (
        <ul className="notice-list">
          {details.map((message, index) => (
            <li key={index}>{message}</li>
          ))}
        </ul>
      )}
    </Notice>
  )
}
