import { useState, type FormEvent } from 'react'
import { api } from '../api'
import { useAuth, useDeveloper } from '../auth/AuthContext'
import { ErrorAlert, Notice } from '../components/Alert'
import { CopyButton } from '../components/CopyButton'
import { ReadOnlyField, TextField } from '../components/Field'
import { PageHeader } from '../components/PageHeader'
import { t } from '../i18n'
import { hasErrors, matches, minLength, minPasswordLength, required, validate, type FieldErrors } from '../lib/validation'

function ProfileForm() {
  const developer = useDeveloper()
  const { updateDeveloper } = useAuth()
  const [firstName, setFirstName] = useState(developer.firstName)
  const [lastName, setLastName] = useState(developer.lastName)
  const [error, setError] = useState<unknown>(null)
  const [busy, setBusy] = useState(false)
  const [saved, setSaved] = useState(false)

  async function submit(event: FormEvent) {
    event.preventDefault()
    setBusy(true)
    setError(null)
    setSaved(false)
    try {
      await api.me.update(firstName.trim(), lastName.trim())
      updateDeveloper({ firstName: firstName.trim(), lastName: lastName.trim() })
      setSaved(true)
    } catch (e) {
      setError(e)
    } finally {
      setBusy(false)
    }
  }

  return (
    <section className="card" aria-labelledby="profile-title">
      <h2 id="profile-title">{t('account.profileTitle')}</h2>
      <form className="form" onSubmit={submit} noValidate>
        <ReadOnlyField label={t('auth.email')} value={developer.email} />
        <ReadOnlyField
          label={t('account.developerKey')}
          value={developer.key}
          mono
          action={<CopyButton value={developer.key} what={t('account.developerKey')} />}
        />
        <div className="form-grid">
          <TextField
            label={t('account.firstName')}
            autoComplete="given-name"
            value={firstName}
            onChange={(v) => {
              setFirstName(v)
              setSaved(false)
            }}
          />
          <TextField
            label={t('account.lastName')}
            autoComplete="family-name"
            value={lastName}
            onChange={(v) => {
              setLastName(v)
              setSaved(false)
            }}
          />
        </div>
        <ErrorAlert error={error} />
        {saved && <Notice tone="success">{t('account.profileSaved')}</Notice>}
        <div className="form-actions">
          <button type="submit" className="btn btn-primary" disabled={busy}>
            {busy ? t('common.saving') : t('account.saveProfile')}
          </button>
        </div>
      </form>
    </section>
  )
}

type PasswordFields = 'current' | 'password' | 'confirm'

function PasswordForm() {
  const empty = { current: '', password: '', confirm: '' }
  const [values, setValues] = useState<Record<PasswordFields, string>>(empty)
  const [errors, setErrors] = useState<FieldErrors<PasswordFields>>({})
  const [error, setError] = useState<unknown>(null)
  const [busy, setBusy] = useState(false)
  const [saved, setSaved] = useState(false)

  const set = (field: PasswordFields) => (value: string) => {
    setValues((v) => ({ ...v, [field]: value }))
    setSaved(false)
  }

  async function submit(event: FormEvent) {
    event.preventDefault()
    const found = validate(values, {
      current: [required()],
      password: [required(), minLength(minPasswordLength)],
      confirm: [required(), matches(values.password)],
    })
    setErrors(found)
    if (hasErrors(found)) return
    setBusy(true)
    setError(null)
    try {
      await api.auth.changePassword(values.current, values.password)
      setValues(empty)
      setSaved(true)
    } catch (e) {
      setError(e)
    } finally {
      setBusy(false)
    }
  }

  return (
    <section className="card" aria-labelledby="password-title">
      <h2 id="password-title">{t('account.passwordTitle')}</h2>
      <form className="form" onSubmit={submit} noValidate>
        <TextField
          label={t('auth.currentPassword')}
          type="password"
          autoComplete="current-password"
          value={values.current}
          onChange={set('current')}
          error={errors.current}
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
        <ErrorAlert error={error} messages={{ 400: t('account.passwordRejected'), 401: t('account.passwordRejected') }} />
        {saved && <Notice tone="success">{t('account.passwordChanged')}</Notice>}
        <div className="form-actions">
          <button type="submit" className="btn btn-primary" disabled={busy}>
            {busy ? t('common.saving') : t('account.changePassword')}
          </button>
        </div>
      </form>
    </section>
  )
}

export function AccountPage() {
  return (
    <>
      <PageHeader title={t('account.title')} intro={t('account.intro')} />
      <div className="card-stack">
        <ProfileForm />
        <PasswordForm />
      </div>
    </>
  )
}
