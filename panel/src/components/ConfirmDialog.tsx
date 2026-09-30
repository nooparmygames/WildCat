import { useId, useState, type ReactNode } from 'react'
import { t } from '../i18n'
import { ErrorAlert } from './Alert'
import { Dialog } from './Dialog'

interface ConfirmDialogProps {
  title: string
  children: ReactNode
  confirmLabel: string
  busyLabel?: string
  danger?: boolean
  /** Runs the action; the dialog stays open and shows the error if it throws. */
  onConfirm: () => Promise<void>
  onClose: () => void
}

export function ConfirmDialog({ title, children, confirmLabel, busyLabel, danger = false, onConfirm, onClose }: ConfirmDialogProps) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<unknown>(null)
  const bodyId = useId()

  async function confirm() {
    setBusy(true)
    setError(null)
    try {
      await onConfirm()
    } catch (e) {
      setError(e)
      setBusy(false)
    }
  }

  return (
    <Dialog title={title} onClose={onClose} busy={busy} size="small" describedBy={bodyId}>
      <div className="dialog-body">
        <div id={bodyId}>{children}</div>
        <ErrorAlert error={error} />
      </div>
      <div className="dialog-footer">
        <button type="button" className="btn" onClick={onClose} disabled={busy} data-autofocus>
          {t('common.cancel')}
        </button>
        <button type="button" className={`btn ${danger ? 'btn-danger' : 'btn-primary'}`} onClick={confirm} disabled={busy}>
          {busy ? (busyLabel ?? t('common.working')) : confirmLabel}
        </button>
      </div>
    </Dialog>
  )
}
