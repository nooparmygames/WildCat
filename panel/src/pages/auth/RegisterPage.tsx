import { useState, type FormEvent } from 'react'
import { Link } from 'react-router'
import { api } from '../../api'
import { ErrorAlert, Notice } from '../../components/Alert'
import { TextField } from '../../components/Field'
import { t } from '../../i18n'
import {
  email as emailRule,
  hasErrors,
  matches,
  minLength,
  minPasswordLength,
  required,
  validate,
  type FieldErrors,
} from '../../lib/validation'
import { AuthCard } from './AuthCard'

type Fields = 'email' | 'password' | 'confirm'

export function RegisterPage() {
  const [values, setValues] = useState<Record<Fields, string>>({ email: '', password: '', confirm: '' })
  const [errors, setErrors] = useState<FieldErrors<Fields>>({})
  const [error, setError] = useState<unknown>(null)
  const [busy, setBusy] = useState(false)
  const [registeredEmail, setRegisteredEmail] = useState<string | null>(null)

  const set = (field: Fields) => (value: string) => setValues((v) => ({ ...v, [field]: value }))

  async function submit(event: FormEvent) {
    event.preventDefault()
    const found = validate(values, {
      email: [required(), emailRule()],
      password: [required(), minLength(minPasswordLength)],
      confirm: [required(), matches(values.password)],
    })
    setErrors(found)
    if (hasErrors(found)) return
    setBusy(true)
    setError(null)
    try {
      const email = values.email.trim()
      await api.auth.register(email, values.password)
      setRegisteredEmail(email)
    } catch (e) {
      setError(e)
    } finally {
      setBusy(false)
    }
  }

  if (registeredEmail) {
    return (
      <AuthCard title={t('auth.register.doneTitle')}>
        <Notice tone="success">
          <p>{t('auth.register.doneBody', { email: registeredEmail })}</p>
        </Notice>
        <p className="form-actions">
          <Link className="btn btn-primary" to={`/activate?email=${encodeURIComponent(registeredEmail)}`}>
            {t('auth.register.goToActivation')}
          </Link>
        </p>
      </AuthCard>
    )
  }

  return (
    <AuthCard
      title={t('auth.register.title')}
      intro={t('auth.register.intro')}
      footer={
        <p>
          {t('auth.register.haveAccount')} <Link to="/login">{t('auth.register.signIn')}</Link>
        </p>
      }
    >
      <ErrorAlert error={error} />
      <form className="form" onSubmit={submit} noValidate>
        <TextField
          label={t('auth.email')}
          type="email"
          autoComplete="email"
          value={values.email}
          onChange={set('email')}
          error={errors.email}
          required
        />
        <TextField
          label={t('auth.password')}
          type="password"
          autoComplete="new-password"
          value={values.password}
          onChange={set('password')}
          hint={t('auth.passwordHint', { min: minPasswordLength })}
          error={errors.password}
          required
        />
        <TextField
          label={t('auth.confirmPassword')}
          type="password"
          autoComplete="new-password"
          value={values.confirm}
          onChange={set('confirm')}
          error={errors.confirm}
          required
        />
        <div className="form-actions">
          <button type="submit" className="btn btn-primary btn-block" disabled={busy}>
            {busy ? t('auth.register.submitting') : t('auth.register.submit')}
          </button>
        </div>
      </form>
    </AuthCard>
  )
}
