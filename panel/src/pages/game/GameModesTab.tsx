import { useCallback, useState } from 'react'
import { api } from '../../api'
import type { GameMode } from '../../api/types'
import { ConfirmDialog } from '../../components/ConfirmDialog'
import { LoadError, Loading } from '../../components/LoadState'
import { PageHeader } from '../../components/PageHeader'
import { t } from '../../i18n'
import { useLoader } from '../../lib/useLoader'
import { useGame } from './GamePage'
import { GameModeDialog } from './GameModeDialog'

type Open = { kind: 'create' } | { kind: 'edit'; mode: GameMode } | { kind: 'delete'; mode: GameMode }

function players(mode: GameMode): string {
  return mode.minPlayers === mode.maxPlayers
    ? t('gameModes.playersExact', { n: mode.minPlayers })
    : t('gameModes.playersRange', { min: mode.minPlayers, max: mode.maxPlayers })
}

function schedule(mode: GameMode): string {
  return mode.criteria
    .map((c) => t('gameModes.scheduleEntry', { seconds: c.startTimeInSeconds, threshold: c.acceptableThreshold }))
    .join('; ')
}

export function GameModesTab() {
  const game = useGame()
  const load = useCallback(() => api.gameModes.list(game.key), [game.key])
  const { data: modes, error, loading, reload } = useLoader(load)
  const [open, setOpen] = useState<Open | null>(null)
  const close = () => setOpen(null)

  return (
    <>
      <PageHeader
        level={2}
        title={t('game.tabs.gameModes')}
        intro={t('gameModes.intro')}
        actions={
          <button type="button" className="btn btn-primary" onClick={() => setOpen({ kind: 'create' })}>
            {t('gameModes.new')}
          </button>
        }
      />
      {error ? (
        <LoadError error={error} onRetry={reload} />
      ) : !modes ? (
        <Loading />
      ) : modes.length === 0 ? (
        <div className="empty-state">
          <p>{t('gameModes.empty')}</p>
        </div>
      ) : (
        <div className="table-wrap" aria-busy={loading}>
          <table className="table">
            <caption className="visually-hidden">{t('game.tabs.gameModes')}</caption>
            <thead>
              <tr>
                <th scope="col">{t('gameModes.columns.name')}</th>
                <th scope="col">{t('gameModes.columns.players')}</th>
                <th scope="col">{t('gameModes.columns.schedule')}</th>
                <th scope="col">
                  <span className="visually-hidden">{t('common.actions')}</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {modes.map((mode) => (
                <tr key={mode.name}>
                  <th scope="row" data-label={t('gameModes.columns.name')}>
                    {mode.name}
                  </th>
                  <td data-label={t('gameModes.columns.players')}>{players(mode)}</td>
                  <td data-label={t('gameModes.columns.schedule')}>{schedule(mode)}</td>
                  <td className="actions">
                    <div className="button-row">
                      <button
                        type="button"
                        className="btn btn-small"
                        onClick={() => setOpen({ kind: 'edit', mode })}
                        aria-label={t('common.editWhat', { what: mode.name })}
                      >
                        {t('common.edit')}
                      </button>
                      <button
                        type="button"
                        className="btn btn-small btn-danger-quiet"
                        onClick={() => setOpen({ kind: 'delete', mode })}
                        aria-label={t('common.deleteWhat', { what: mode.name })}
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
        <GameModeDialog
          gameKey={game.key}
          mode={open.kind === 'edit' ? open.mode : undefined}
          takenNames={modes?.map((m) => m.name) ?? []}
          onClose={close}
          onSaved={() => {
            close()
            reload()
          }}
        />
      )}
      {open?.kind === 'delete' && (
        <ConfirmDialog
          title={t('gameModes.deleteTitle', { name: open.mode.name })}
          confirmLabel={t('common.delete')}
          busyLabel={t('common.deleting')}
          danger
          onClose={close}
          onConfirm={async () => {
            await api.gameModes.remove(game.key, open.mode.name)
            close()
            reload()
          }}
        >
          <p>{t('gameModes.deleteBody')}</p>
        </ConfirmDialog>
      )}
    </>
  )
}
