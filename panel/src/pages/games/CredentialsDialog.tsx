import { useId, useState } from 'react'
import { api } from '../../api'
import { useAuth, useDeveloper } from '../../auth/AuthContext'
import { ErrorAlert, Notice } from '../../components/Alert'
import { CopyButton } from '../../components/CopyButton'
import { Dialog } from '../../components/Dialog'
import { ReadOnlyField } from '../../components/Field'
import { t } from '../../i18n'

function SecretField({ label, value }: { label: string; value: string }) {
  const id = useId()
  const [visible, setVisible] = useState(false)
  return (
    <div className="field">
      <label htmlFor={id}>{label}</label>
      <div className="field-inline">
        <input id={id} className="mono" type={visible ? 'text' : 'password'} value={value} readOnly autoComplete="off" />
        <button type="button" className="btn btn-small" aria-pressed={visible} onClick={() => setVisible((v) => !v)}>
          {visible ? t('common.hide') : t('common.show')}
        </button>
        <CopyButton value={value} what={label} />
      </div>
    </div>
  )
}

interface CredentialsDialogProps {
  game: { key: string; name: string }
  onClose: () => void
  /** Shown above the keys, for example right after the game was created. */
  intro?: string
}

/** The three values a game needs for the SDK, with a way to replace the developer secret. */
export function CredentialsDialog({ game, onClose, intro }: CredentialsDialogProps) {
  const developer = useDeveloper()
  const { updateDeveloper } = useAuth()
  const [confirming, setConfirming] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<unknown>(null)
  const [regenerated, setRegenerated] = useState(false)
  const confirmTitleId = useId()

  async function regenerate() {
    setBusy(true)
    setError(null)
    try {
      const secret = await api.me.regenerateSecret()
      if (secret) updateDeveloper({ secret })
      else await api.me.get().then((fresh) => updateDeveloper({ secret: fresh.secret }))
      setRegenerated(true)
      setConfirming(false)
    } catch (e) {
      setError(e)
    } finally {
      setBusy(false)
    }
  }

  return (
    <Dialog title={t('credentials.title', { name: game.name })} onClose={onClose} busy={busy}>
      <div className="dialog-body form">
        {intro && <Notice tone="success">{intro}</Notice>}
        <p className="muted">{t('credentials.intro')}</p>
        <ReadOnlyField
          label={t('credentials.gameKey')}
          value={game.key}
          mono
          action={<CopyButton value={game.key} what={t('credentials.gameKey')} />}
        />
        <ReadOnlyField
          label={t('credentials.developerKey')}
          value={developer.key}
          mono
          action={<CopyButton value={developer.key} what={t('credentials.developerKey')} />}
        />
        <SecretField label={t('credentials.developerSecret')} value={developer.secret} />
        <p className="field-hint">{t('credentials.secretHint')}</p>

        {regenerated && <Notice tone="success">{t('credentials.regenerated')}</Notice>}
        <ErrorAlert error={error} />
        {confirming ? (
          <div className="confirm-box" role="group" aria-labelledby={confirmTitleId}>
            <p id={confirmTitleId} className="notice-title">
              {t('credentials.regenerateTitle')}
            </p>
            <p>{t('credentials.regenerateBody')}</p>
            <div className="button-row">
              <button type="button" className="btn" onClick={() => setConfirming(false)} disabled={busy}>
                {t('credentials.keepSecret')}
              </button>
              <button type="button" className="btn btn-danger" onClick={regenerate} disabled={busy}>
                {busy ? t('common.working') : t('credentials.regenerateConfirm')}
              </button>
            </div>
          </div>
        ) : (
          <div>
            <button
              type="button"
              className="btn"
              onClick={() => {
                setRegenerated(false)
                setConfirming(true)
              }}
            >
              {t('credentials.regenerate')}
            </button>
          </div>
        )}
      </div>
      <div className="dialog-footer">
        <button type="button" className="btn btn-primary" onClick={onClose} disabled={busy} data-autofocus>
          {t('common.done')}
        </button>
      </div>
    </Dialog>
  )
}
