import { useCallback, useId, useState, type FormEvent } from 'react'
import { useSearchParams } from 'react-router'
import { api } from '../api'
import { CopyButton } from '../components/CopyButton'
import { LoadError, Loading } from '../components/LoadState'
import { PageHeader } from '../components/PageHeader'
import { pageCount, Pagination } from '../components/Pagination'
import { t } from '../i18n'
import { useLoader } from '../lib/useLoader'

const pageSize = 25

/** Admin only: every registered developer, with search and paging kept in the URL. */
export function AdminDevelopersPage() {
  const [params, setParams] = useSearchParams()
  const search = params.get('search') ?? ''
  const page = Math.max(1, Number.parseInt(params.get('page') ?? '1', 10) || 1)
  const [draft, setDraft] = useState(search)
  const searchId = useId()

  const load = useCallback(() => api.admin.developers({ search: search || undefined, page, pageSize }), [search, page])
  const { data, error, loading, reload } = useLoader(load, { keepPrevious: true })

  const go = (next: { search?: string; page?: number }) => {
    const nextSearch = next.search ?? search
    const nextPage = next.page ?? page
    const query: Record<string, string> = {}
    if (nextSearch) query.search = nextSearch
    if (nextPage > 1) query.page = String(nextPage)
    setParams(query)
  }

  function submit(event: FormEvent) {
    event.preventDefault()
    go({ search: draft.trim(), page: 1 })
  }

  return (
    <>
      <PageHeader title={t('developers.title')} intro={t('developers.intro')} />
      <form className="search-bar" role="search" onSubmit={submit}>
        <label htmlFor={searchId} className="visually-hidden">
          {t('developers.searchLabel')}
        </label>
        <input
          id={searchId}
          type="search"
          placeholder={t('developers.searchPlaceholder')}
          value={draft}
          onChange={(e) => setDraft(e.target.value)}
        />
        <button type="submit" className="btn">
          {t('common.search')}
        </button>
        {search && (
          <button
            type="button"
            className="btn btn-ghost"
            onClick={() => {
              setDraft('')
              go({ search: '', page: 1 })
            }}
          >
            {t('developers.clearSearch')}
          </button>
        )}
      </form>

      {error ? (
        <LoadError error={error} onRetry={reload} pageLevel />
      ) : !data ? (
        <Loading />
      ) : data.items.length === 0 ? (
        <div className="empty-state">
          <p>{search ? t('developers.noMatches', { search }) : t('developers.empty')}</p>
        </div>
      ) : (
        <>
          <p className="muted" aria-live="polite">
            {t('developers.total', { total: data.total })}
          </p>
          <div className="table-wrap" aria-busy={loading}>
            <table className="table">
              <caption className="visually-hidden">{t('developers.title')}</caption>
              <thead>
                <tr>
                  <th scope="col">{t('developers.columns.name')}</th>
                  <th scope="col">{t('developers.columns.email')}</th>
                  <th scope="col">{t('developers.columns.status')}</th>
                  <th scope="col">{t('developers.columns.key')}</th>
                </tr>
              </thead>
              <tbody>
                {data.items.map((dev) => (
                  <tr key={dev.key}>
                    <th scope="row" data-label={t('developers.columns.name')}>
                      {[dev.firstName, dev.lastName].filter(Boolean).join(' ') || <span className="muted">{t('developers.noName')}</span>}
                    </th>
                    <td data-label={t('developers.columns.email')}>
                      <a href={`mailto:${dev.email}`}>{dev.email}</a>
                    </td>
                    <td data-label={t('developers.columns.status')}>
                      <span className={`status ${dev.isActivated ? 'status-ok' : 'status-pending'}`}>
                        {dev.isActivated ? t('developers.active') : t('developers.notActivated')}
                      </span>
                    </td>
                    <td data-label={t('developers.columns.key')}>
                      <span className="key-cell">
                        <code className="key">{dev.key}</code>
                        <CopyButton value={dev.key} what={t('developers.keyOf', { email: dev.email })} />
                      </span>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          {pageCount(data.total, pageSize) > 1 && (
            <Pagination page={page} pageSize={pageSize} total={data.total} onPageChange={(p) => go({ page: p })} />
          )}
        </>
      )}
    </>
  )
}
