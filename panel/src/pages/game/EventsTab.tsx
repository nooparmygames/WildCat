import { useCallback, useState } from 'react'
import { Link } from 'react-router'
import { api } from '../../api'
import type { ScriptEvent } from '../../api/types'
import { ConfirmDialog } from '../../components/ConfirmDialog'
import { LoadError, Loading } from '../../components/LoadState'
import { PageHeader } from '../../components/PageHeader'
import { t } from '../../i18n'
import { useLoader } from '../../lib/useLoader'
import { useGame } from './GamePage'
import { parameterList } from './scriptEvents'

export function EventsTab() {
  const game = useGame()
  const load = useCallback(() => api.events.list(game.key), [game.key])
  const { data: events, error, loading, reload } = useLoader(load)
  const [deleting, setDeleting] = useState<ScriptEvent | null>(null)

  return (
    <>
      <PageHeader
        level={2}
        title={t('game.tabs.events')}
        intro={t('events.intro')}
        actions={
          <Link className="btn btn-primary" to="new">
            {t('events.new')}
          </Link>
        }
      />
      {error ? (
        <LoadError error={error} onRetry={reload} />
      ) : !events ? (
        <Loading />
      ) : events.length === 0 ? (
        <div className="empty-state">
          <p>{t('events.empty')}</p>
        </div>
      ) : (
        <div className="table-wrap" aria-busy={loading}>
          <table className="table">
            <caption className="visually-hidden">{t('game.tabs.events')}</caption>
            <thead>
              <tr>
                <th scope="col">{t('events.columns.name')}</th>
                <th scope="col">{t('events.columns.parameters')}</th>
                <th scope="col">
                  <span className="visually-hidden">{t('common.actions')}</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {events.map((event) => (
                <tr key={event.name}>
                  <th scope="row" data-label={t('events.columns.name')}>
                    <Link to={encodeURIComponent(event.name)}>{event.name}</Link>
                  </th>
                  <td data-label={t('events.columns.parameters')}>
                    {event.parameters.length > 0 ? (
                      <code>{parameterList(event.parameters)}</code>
                    ) : (
                      <span className="muted">{t('events.noParameters')}</span>
                    )}
                  </td>
                  <td className="actions">
                    <div className="button-row">
                      <Link
                        className="btn btn-small"
                        to={encodeURIComponent(event.name)}
                        aria-label={t('common.editWhat', { what: event.name })}
                      >
                        {t('common.edit')}
                      </Link>
                      <button
                        type="button"
                        className="btn btn-small btn-danger-quiet"
                        onClick={() => setDeleting(event)}
                        aria-label={t('common.deleteWhat', { what: event.name })}
                      >
                        {t('common.delete')}
                      </button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {deleting && (
        <ConfirmDialog
          title={t('events.deleteTitle', { name: deleting.name })}
          confirmLabel={t('common.delete')}
          busyLabel={t('common.deleting')}
          danger
          onClose={() => setDeleting(null)}
          onConfirm={async () => {
            await api.events.remove(game.key, deleting.name)
            setDeleting(null)
            reload()
          }}
        >
          <p>{t('events.deleteBody')}</p>
        </ConfirmDialog>
      )}
    </>
  )
}
