import { useState, type FormEvent } from 'react'
import { Link, useLocation } from 'react-router'
import { useAuth } from '../../auth/AuthContext'
import { ErrorAlert, Notice } from '../../components/Alert'
import { TextField } from '../../components/Field'
import { t } from '../../i18n'
import { email as emailRule, hasErrors, required, validate, type FieldErrors } from '../../lib/validation'
import { AuthCard, type LoginState } from './AuthCard'

export function LoginPage() {
  const { signIn, signOutReason } = useAuth()
  const state = (useLocation().state as LoginState | null) ?? {}
  const [email, setEmail] = useState(state.email ?? '')
  const [password, setPassword] = useState('')
  const [errors, setErrors] = useState<FieldErrors<'email' | 'password'>>({})
  const [error, setError] = useState<unknown>(null)
  const [busy, setBusy] = useState(false)

  async function submit(event: FormEvent) {
    event.preventDefault()
    const found = validate({ email, password }, { email: [required(), emailRule()], password: [required()] })
    setErrors(found)
    if (hasErrors(found)) return
    setBusy(true)
    setError(null)
    try {
      // On success the guest-only route sends the developer on to where they were going.
      await signIn(email.trim(), password)
    } catch (e) {
      setError(e)
      setBusy(false)
    }
  }

  return (
    <AuthCard
      title={t('auth.login.title')}
      intro={t('auth.login.intro')}
      footer={
        <p>
          {t('auth.login.noAccount')} <Link to="/register">{t('auth.login.register')}</Link>
        </p>
      }
    >
      {signOutReason === 'expired' && !error && <Notice tone="warning">{t('auth.login.sessionExpired')}</Notice>}
      {signOutReason === 'passwordChanged' && !error && <Notice tone="success">{t('auth.login.passwordReset')}</Notice>}
      {state.notice === 'activated' && <Notice tone="success">{t('auth.login.activated')}</Notice>}
      {state.notice === 'passwordReset' && <Notice tone="success">{t('auth.login.passwordReset')}</Notice>}
      <ErrorAlert
        error={error}
        messages={{ 400: t('auth.login.failed'), 401: t('auth.login.failed'), 403: t('auth.login.notActivated') }}
      />
      <form className="form" onSubmit={submit} noValidate>
        <TextField
          label={t('auth.email')}
          type="email"
          autoComplete="username"
          value={email}
          onChange={setEmail}
          error={errors.email}
          required
        />
        <TextField
          label={t('auth.password')}
          type="password"
          autoComplete="current-password"
          value={password}
          onChange={setPassword}
          error={errors.password}
          required
        />
        <div className="form-actions">
          <button type="submit" className="btn btn-primary btn-block" disabled={busy}>
            {busy ? t('auth.login.submitting') : t('auth.login.submit')}
          </button>
        </div>
        <p className="form-link">
          <Link to="/forgot-password">{t('auth.login.forgot')}</Link>
        </p>
      </form>
    </AuthCard>
  )
}
