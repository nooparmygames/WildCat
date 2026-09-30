import { useState } from 'react'
import { Link } from 'react-router'
import { api } from '../../api'
import type { Game } from '../../api/types'
import { ConfirmDialog } from '../../components/ConfirmDialog'
import { LoadError, Loading } from '../../components/LoadState'
import { PageHeader } from '../../components/PageHeader'
import { t } from '../../i18n'
import { useLoader } from '../../lib/useLoader'
import { CredentialsDialog } from './CredentialsDialog'
import { GameDialog, genreLabel } from './GameDialog'

type Open =
  | { kind: 'create' }
  | { kind: 'edit'; game: Game }
  | { kind: 'delete'; game: Game }
  | { kind: 'credentials'; game: Game; created?: boolean }

export function GamesPage() {
  const { data: games, error, loading, reload } = useLoader(api.games.list)
  const [open, setOpen] = useState<Open | null>(null)
  const close = () => setOpen(null)

  return (
    <>
      <PageHeader
        title={t('games.title')}
        intro={t('games.intro')}
        actions={
          <button type="button" className="btn btn-primary" onClick={() => setOpen({ kind: 'create' })}>
            {t('games.new')}
          </button>
        }
      />

      {error ? (
        <LoadError error={error} onRetry={reload} />
      ) : !games ? (
        <Loading />
      ) : games.length === 0 ? (
        <div className="empty-state">
          <p>{t('games.empty')}</p>
        </div>
      ) : (
        <div className="table-wrap" aria-busy={loading}>
          <table className="table">
            <caption className="visually-hidden">{t('games.title')}</caption>
            <thead>
              <tr>
                <th scope="col">{t('games.columns.name')}</th>
                <th scope="col">{t('games.columns.genre')}</th>
                <th scope="col">{t('games.columns.platform')}</th>
                <th scope="col">{t('games.columns.key')}</th>
                <th scope="col">
                  <span className="visually-hidden">{t('common.actions')}</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {games.map((game) => (
                <tr key={game.key}>
                  <th scope="row" data-label={t('games.columns.name')}>
                    <Link to={`/games/${encodeURIComponent(game.key)}`}>{game.name || game.key}</Link>
                  </th>
                  <td data-label={t('games.columns.genre')}>{genreLabel(game.genre)}</td>
                  <td data-label={t('games.columns.platform')}>{game.platform || <span className="muted">-</span>}</td>
                  <td data-label={t('games.columns.key')}>
                    <code className="key">{game.key}</code>
                  </td>
                  <td className="actions">
                    <div className="button-row">
                      <button
                        type="button"
                        className="btn btn-small"
                        onClick={() => setOpen({ kind: 'credentials', game })}
                        aria-label={t('games.credentialsFor', { name: game.name })}
                      >
                        {t('games.credentials')}
                      </button>
                      <button
                        type="button"
                        className="btn btn-small"
                        onClick={() => setOpen({ kind: 'edit', game })}
                        aria-label={t('common.editWhat', { what: game.name })}
                      >
                        {t('common.edit')}
                      </button>
                      <button
                        type="button"
                        className="btn btn-small btn-danger-quiet"
                        onClick={() => setOpen({ kind: 'delete', game })}
                        aria-label={t('common.deleteWhat', { what: game.name })}
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

      {(open?.kind === 'create' || open?.kind === 'edit') && (
        <GameDialog
          game={open.kind === 'edit' ? open.game : undefined}
          onClose={close}
          onSaved={(game) => {
            reload()
            setOpen(open.kind === 'create' && game.key ? { kind: 'credentials', game, created: true } : null)
          }}
        />
      )}
      {open?.kind === 'credentials' && (
        <CredentialsDialog
          game={open.game}
          intro={open.created ? t('games.createdNotice', { name: open.game.name }) : undefined}
          onClose={close}
        />
      )}
      {open?.kind === 'delete' && (
        <ConfirmDialog
          title={t('games.deleteTitle', { name: open.game.name })}
          confirmLabel={t('games.deleteConfirm')}
          busyLabel={t('common.deleting')}
          danger
          onClose={close}
          onConfirm={async () => {
            await api.games.remove(open.game.key)
            close()
            reload()
          }}
        >
          <p>{t('games.deleteBody')}</p>
        </ConfirmDialog>
      )}
    </>
  )
}
