import { useState, type FormEvent } from 'react'
import { Link } from 'react-router'
import { api } from '../../api'
import { ErrorAlert, Notice } from '../../components/Alert'
import { TextField } from '../../components/Field'
import { t } from '../../i18n'
import { email as emailRule, hasErrors, required, validate } from '../../lib/validation'
import { AuthCard } from './AuthCard'

export function ForgotPasswordPage() {
  const [email, setEmail] = useState('')
  const [fieldError, setFieldError] = useState<string | undefined>()
  const [error, setError] = useState<unknown>(null)
  const [busy, setBusy] = useState(false)
  const [sentTo, setSentTo] = useState<string | null>(null)

  async function submit(event: FormEvent) {
    event.preventDefault()
    const found = validate({ email }, { email: [required(), emailRule()] })
    setFieldError(found.email)
    if (hasErrors(found)) return
    setBusy(true)
    setError(null)
    try {
      await api.auth.requestPasswordReset(email.trim())
      setSentTo(email.trim())
    } catch (e) {
      setError(e)
    } finally {
      setBusy(false)
    }
  }

  if (sentTo) {
    return (
      <AuthCard
        title={t('auth.forgot.doneTitle')}
        footer={
          <p>
            <Link to="/login">{t('auth.backToSignIn')}</Link>
          </p>
        }
      >
        <Notice tone="success">
          <p>{t('auth.forgot.doneBody', { email: sentTo })}</p>
        </Notice>
        <p className="form-actions">
          <Link className="btn btn-primary" to={`/reset-password?email=${encodeURIComponent(sentTo)}`}>
            {t('auth.forgot.haveCode')}
          </Link>
        </p>
      </AuthCard>
    )
  }

  return (
    <AuthCard
      title={t('auth.forgot.title')}
      intro={t('auth.forgot.intro')}
      footer={
        <p>
          <Link to="/login">{t('auth.backToSignIn')}</Link>
        </p>
      }
    >
      <ErrorAlert error={error} />
      <form className="form" onSubmit={submit} noValidate>
        <TextField
          label={t('auth.email')}
          type="email"
          autoComplete="email"
          value={email}
          onChange={setEmail}
          error={fieldError}
          required
        />
        <div className="form-actions">
          <button type="submit" className="btn btn-primary btn-block" disabled={busy}>
            {busy ? t('auth.forgot.submitting') : t('auth.forgot.submit')}
          </button>
        </div>
      </form>
    </AuthCard>
  )
}
