import { isApiError } from '../api/client'
import { t } from '../i18n'

/** Messages by HTTP status for a page that knows better than the generic ones. */
export type StatusMessages = Partial<Record<number, string>>

/**
 * The sentence to show a person for a failed request: the server's detail when it sent one,
 * otherwise a page-specific or generic message for the status.
 */
export function errorMessage(error: unknown, overrides: StatusMessages = {}): string {
  if (!isApiError(error)) return t('errors.unknown')
  if (error.status === 0) return t('errors.network')
  if (error.detail) return error.detail
  const override = overrides[error.status]
  if (override) return override
  switch (error.status) {
    case 401:
      return t('errors.unauthorized')
    case 403:
      return t('errors.forbidden')
    case 404:
      return t('errors.notFound')
    case 409:
      return t('errors.conflict')
  }
  if (error.status >= 500) return t('errors.server')
  if (error.errors.length > 0) return t('errors.validation')
  return error.title || t('errors.unknown')
}

/** Validation messages that should be listed under the main message, without repeating it. */
export function errorDetails(error: unknown, overrides: StatusMessages = {}): string[] {
  if (!isApiError(error)) return []
  const main = errorMessage(error, overrides)
  return error.errors.filter((message) => message !== main)
}

export function isStatus(error: unknown, status: number): boolean {
  return isApiError(error) && error.status === status
}
