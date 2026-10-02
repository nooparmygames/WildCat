import { useEffect, useId, useRef, type ReactNode } from 'react'
import { t } from '../i18n'

interface DialogProps {
  title: string
  onClose: () => void
  children: ReactNode
  /** While busy, Escape and the close button do nothing. */
  busy?: boolean
  size?: 'small' | 'medium' | 'large'
  /** Optional id of an element that describes the dialog. */
  describedBy?: string
}

const focusableInBody =
  '.dialog-body input:not([disabled]):not([readonly]), .dialog-body select:not([disabled]), .dialog-body textarea:not([disabled]):not([readonly])'

/**
 * A modal built on the native <dialog> element, which keeps focus inside, makes the rest of the
 * page inert and closes on Escape. Mount it to open it and unmount it to close it; focus goes
 * back to whatever was focused before it opened.
 */
export function Dialog({ title, onClose, children, busy = false, size = 'medium', describedBy }: DialogProps) {
  const ref = useRef<HTMLDialogElement>(null)
  const titleId = useId()

  useEffect(() => {
    const dialog = ref.current
    if (!dialog) return
    const previous = document.activeElement instanceof HTMLElement ? document.activeElement : null
    if (!dialog.open) dialog.showModal()
    const target =
      dialog.querySelector<HTMLElement>('[data-autofocus]') ?? dialog.querySelector<HTMLElement>(focusableInBody)
    target?.focus()
    return () => {
      if (dialog.open) dialog.close()
      previous?.focus()
    }
  }, [])

  return (
    <dialog
      ref={ref}
      className={`dialog dialog-${size}`}
      aria-labelledby={titleId}
      aria-describedby={describedBy}
      onCancel={(event) => {
        event.preventDefault()
        if (!busy) onClose()
      }}
    >
      <div className="dialog-header">
        <h2 id={titleId}>{title}</h2>
        <button type="button" className="btn btn-ghost btn-small" onClick={onClose} disabled={busy}>
          {t('common.close')}
        </button>
      </div>
      {children}
    </dialog>
  )
}
