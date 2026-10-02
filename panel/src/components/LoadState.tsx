import { Link } from 'react-router'
import { t } from '../i18n'
import { isStatus } from '../lib/errors'
import { ErrorAlert } from './Alert'

export function Loading({ label }: { label?: string }) {
  return (
    <div className="loading" role="status" aria-live="polite">
      <span className="spinner" aria-hidden="true" />
      <span>{label ?? t('app.loading')}</span>
    </div>
  )
}

export function NoPermission() {
  return (
    <section className="empty-page" aria-labelledby="no-permission-title">
      <h1 id="no-permission-title">{t('noPermission.title')}</h1>
      <p>{t('noPermission.body')}</p>
      <Link className="btn" to="/games">
        {t('noPermission.back')}
      </Link>
    </section>
  )
}

export function NotFound() {
  return (
    <section className="empty-page" aria-labelledby="not-found-title">
      <h1 id="not-found-title">{t('notFound.title')}</h1>
      <p>{t('notFound.body')}</p>
      <Link className="btn" to="/games">
        {t('notFound.back')}
      </Link>
    </section>
  )
}

interface LoadErrorProps {
  error: unknown
  onRetry?: () => void
  /** Show the full-page no-permission and not-found views for 403 and 404. */
  pageLevel?: boolean
}

/** What to show when loading a page's data failed. */
export function LoadError({ error, onRetry, pageLevel = false }: LoadErrorProps) {
  if (pageLevel && isStatus(error, 403)) return <NoPermission />
  if (pageLevel && isStatus(error, 404)) return <NotFound />
  return (
    <div className="load-error">
      <ErrorAlert error={error} />
      {onRetry && (
        <button type="button" className="btn" onClick={onRetry}>
          {t('common.retry')}
        </button>
      )}
    </div>
  )
}
