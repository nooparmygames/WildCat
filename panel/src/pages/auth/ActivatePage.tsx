import { useEffect, useRef, useState, type FormEvent } from 'react'
import { Link, useSearchParams } from 'react-router'
import { api } from '../../api'
import { ErrorAlert, Notice } from '../../components/Alert'
import { TextField } from '../../components/Field'
import { Loading } from '../../components/LoadState'
import { t } from '../../i18n'
import { email as emailRule, hasErrors, required, validate, type FieldErrors } from '../../lib/validation'
import { AuthCard, type LoginState } from './AuthCard'

type Fields = 'email' | 'code'

/**
 * /activate?email=&code= activates straight away (that is the link in the activation email);
 * without both values it shows a form.
 */
export function ActivatePage() {
  const [params] = useSearchParams()
  const linkEmail = params.get('email')?.trim() ?? ''
  const linkCode = params.get('code')?.trim() ?? ''
  const fromLink = linkEmail !== '' && linkCode !== ''

  const [values, setValues] = useState<Record<Fields, string>>({ email: linkEmail, code: linkCode })
  const [errors, setErrors] = useState<FieldErrors<Fields>>({})
  const [error, setError] = useState<unknown>(null)
  // 'linking' is the automatic activation from the emailed link.
  const [status, setStatus] = useState<'linking' | 'form' | 'busy' | 'done'>(fromLink ? 'linking' : 'form')
  const started = useRef(false)

  useEffect(() => {
    if (!fromLink || started.current) return
    started.current = true
    api.auth.activate(linkEmail, linkCode).then(
      () => setStatus('done'),
      (e: unknown) => {
        setError(e)
        setStatus('form')
      },
    )
  }, [fromLink, linkEmail, linkCode])

  const set = (field: Fields) => (value: string) => setValues((v) => ({ ...v, [field]: value }))

  async function submit(event: FormEvent) {
    event.preventDefault()
    const found = validate(values, { email: [required(), emailRule()], code: [required()] })
    setErrors(found)
    if (hasErrors(found)) return
    setStatus('busy')
    setError(null)
    try {
      await api.auth.activate(values.email.trim(), values.code.trim())
      setStatus('done')
    } catch (e) {
      setError(e)
      setStatus('form')
    }
  }

  if (status === 'done') {
    const state: LoginState = { notice: 'activated', email: values.email.trim() }
    return (
      <AuthCard title={t('auth.activate.doneTitle')}>
        <Notice tone="success">
          <p>{t('auth.activate.doneBody')}</p>
        </Notice>
        <p className="form-actions">
          <Link className="btn btn-primary" to="/login" state={state}>
            {t('auth.activate.signIn')}
          </Link>
        </p>
      </AuthCard>
    )
  }

  if (status === 'linking') {
    return (
      <AuthCard title={t('auth.activate.title')}>
        <Loading label={t('auth.activate.working')} />
      </AuthCard>
    )
  }

  return (
    <AuthCard
      title={t('auth.activate.title')}
      intro={t('auth.activate.intro')}
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
          value={values.email}
          onChange={set('email')}
          error={errors.email}
          required
        />
        <TextField
          label={t('auth.activate.code')}
          autoComplete="one-time-code"
          inputMode="text"
          value={values.code}
          onChange={set('code')}
          error={errors.code}
          required
        />
        <div className="form-actions">
          <button type="submit" className="btn btn-primary btn-block" disabled={status === 'busy'}>
            {status === 'busy' ? t('auth.activate.submitting') : t('auth.activate.submit')}
          </button>
        </div>
      </form>
    </AuthCard>
  )
}
