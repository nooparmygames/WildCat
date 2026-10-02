import { useState, type FormEvent } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router'
import { api } from '../../api'
import { ErrorAlert } from '../../components/Alert'
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
import { AuthCard, type LoginState } from './AuthCard'

type Fields = 'email' | 'code' | 'password' | 'confirm'

/** /reset-password?email=&code= fills the form from the emailed link. */
export function ResetPasswordPage() {
  const [params] = useSearchParams()
  const navigate = useNavigate()
  const [values, setValues] = useState<Record<Fields, string>>({
    email: params.get('email') ?? '',
    code: params.get('code') ?? '',
    password: '',
    confirm: '',
  })
  const [errors, setErrors] = useState<FieldErrors<Fields>>({})
  const [error, setError] = useState<unknown>(null)
  const [busy, setBusy] = useState(false)

  const set = (field: Fields) => (value: string) => setValues((v) => ({ ...v, [field]: value }))

  async function submit(event: FormEvent) {
    event.preventDefault()
    const found = validate(values, {
      email: [required(), emailRule()],
      code: [required()],
      password: [required(), minLength(minPasswordLength)],
      confirm: [required(), matches(values.password)],
    })
    setErrors(found)
    if (hasErrors(found)) return
    setBusy(true)
    setError(null)
    try {
      const email = values.email.trim()
      await api.auth.resetPassword(email, values.code.trim(), values.password)
      navigate('/login', { replace: true, state: { notice: 'passwordReset', email } satisfies LoginState })
    } catch (e) {
      setError(e)
      setBusy(false)
    }
  }

  return (
    <AuthCard
      title={t('auth.reset.title')}
      intro={t('auth.reset.intro')}
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
          autoComplete="username"
          value={values.email}
          onChange={set('email')}
          error={errors.email}
          required
        />
        <TextField
          label={t('auth.reset.code')}
          autoComplete="one-time-code"
          value={values.code}
          onChange={set('code')}
          error={errors.code}
          required
        />
        <TextField
          label={t('auth.newPassword')}
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
            {busy ? t('auth.reset.submitting') : t('auth.reset.submit')}
          </button>
        </div>
      </form>
    </AuthCard>
  )
}
