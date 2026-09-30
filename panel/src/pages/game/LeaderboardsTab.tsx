import { useCallback, useState, type FormEvent } from 'react'
import { api } from '../../api'
import type { Leaderboard } from '../../api/types'
import { ErrorAlert } from '../../components/Alert'
import { ConfirmDialog } from '../../components/ConfirmDialog'
import { Dialog } from '../../components/Dialog'
import { TextField } from '../../components/Field'
import { LoadError, Loading } from '../../components/LoadState'
import { PageHeader } from '../../components/PageHeader'
import { t } from '../../i18n'
import { hasErrors, required, unique, validate } from '../../lib/validation'
import { useLoader } from '../../lib/useLoader'
import { useGame } from './GamePage'

function CreateLeaderboardDialog({
  gameKey,
  taken,
  onClose,
  onSaved,
}: {
  gameKey: string
  taken: string[]
  onClose: () => void
  onSaved: () => void
}) {
  const [name, setName] = useState('')
  const [bestScore, setBestScore] = useState(true)
  const [nameError, setNameError] = useState<string | undefined>()
  const [error, setError] = useState<unknown>(null)
  const [busy, setBusy] = useState(false)

  async function submit(event: FormEvent) {
    event.preventDefault()
    const found = validate({ name }, { name: [required(), unique(taken)] })
    setNameError(found.name)
    if (hasErrors(found)) return
    setBusy(true)
    setError(null)
    try {
      await api.leaderboards.create(gameKey, name.trim(), bestScore)
      onSaved()
    } catch (e) {
      setError(e)
      setBusy(false)
    }
  }

  return (
    <Dialog title={t('leaderboards.createTitle')} onClose={onClose} busy={busy} size="small">
      <form onSubmit={submit} noValidate>
        <div className="dialog-body form">
          <ErrorAlert error={error} />
          <TextField
            label={t('leaderboards.fields.name')}
            value={name}
            onChange={setName}
            error={nameError}
            hint={t('leaderboards.fields.nameHint')}
            required
          />
          <fieldset className="choice-group">
            <legend>{t('leaderboards.fields.scoreRule')}</legend>
            <label className="choice">
              <input type="radio" name="scoreRule" checked={bestScore} onChange={() => setBestScore(true)} />
              <span>
                <strong>{t('leaderboards.fields.best')}</strong>
                <span className="field-hint">{t('leaderboards.fields.bestHint')}</span>
              </span>
            </label>
            <label className="choice">
              <input type="radio" name="scoreRule" checked={!bestScore} onChange={() => setBestScore(false)} />
              <span>
                <strong>{t('leaderboards.fields.latest')}</strong>
                <span className="field-hint">{t('leaderboards.fields.latestHint')}</span>
              </span>
            </label>
          </fieldset>
        </div>
        <div className="dialog-footer">
          <button type="button" className="btn" onClick={onClose} disabled={busy}>
            {t('common.cancel')}
          </button>
          <button type="submit" className="btn btn-primary" disabled={busy}>
            {busy ? t('common.saving') : t('common.create')}
          </button>
        </div>
      </form>
    </Dialog>
  )
}

export function LeaderboardsTab() {
  const game = useGame()
  const load = useCallback(() => api.leaderboards.list(game.key), [game.key])
  const { data: boards, error, loading, reload } = useLoader(load)
  const [creating, setCreating] = useState(false)
  const [deleting, setDeleting] = useState<Leaderboard | null>(null)

  return (
    <>
      <PageHeader
        level={2}
        title={t('game.tabs.leaderboards')}
        intro={t('leaderboards.intro')}
        actions={
          <button type="button" className="btn btn-primary" onClick={() => setCreating(true)}>
            {t('leaderboards.new')}
          </button>
        }
      />
      {error ? (
        <LoadError error={error} onRetry={reload} />
      ) : !boards ? (
        <Loading />
      ) : boards.length === 0 ? (
        <div className="empty-state">
          <p>{t('leaderboards.empty')}</p>
        </div>
      ) : (
        <div className="table-wrap" aria-busy={loading}>
          <table className="table">
            <caption className="visually-hidden">{t('game.tabs.leaderboards')}</caption>
            <thead>
              <tr>
                <th scope="col">{t('leaderboards.columns.name')}</th>
                <th scope="col">{t('leaderboards.columns.keeps')}</th>
                <th scope="col">{t('leaderboards.columns.key')}</th>
                <th scope="col">
                  <span className="visually-hidden">{t('common.actions')}</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {boards.map((board) => (
                <tr key={board.name}>
                  <th scope="row" data-label={t('leaderboards.columns.name')}>
                    {board.name}
                  </th>
                  <td data-label={t('leaderboards.columns.keeps')}>
                    {board.bestScore ? t('leaderboards.keepsBest') : t('leaderboards.keepsLatest')}
                  </td>
                  <td data-label={t('leaderboards.columns.key')}>
                    <code className="key">{board.key}</code>
                  </td>
                  <td className="actions">
                    <div className="button-row">
                      <button
                        type="button"
                        className="btn btn-small btn-danger-quiet"
                        onClick={() => setDeleting(board)}
                        aria-label={t('common.deleteWhat', { what: board.name })}
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

      {creating && (
        <CreateLeaderboardDialog
          gameKey={game.key}
          taken={boards?.map((b) => b.name) ?? []}
          onClose={() => setCreating(false)}
          onSaved={() => {
            setCreating(false)
            reload()
          }}
        />
      )}
      {deleting && (
        <ConfirmDialog
          title={t('leaderboards.deleteTitle', { name: deleting.name })}
          confirmLabel={t('common.delete')}
          busyLabel={t('common.deleting')}
          danger
          onClose={() => setDeleting(null)}
          onConfirm={async () => {
            await api.leaderboards.remove(game.key, deleting.name)
            setDeleting(null)
            reload()
          }}
        >
          <p>{t('leaderboards.deleteBody')}</p>
        </ConfirmDialog>
      )}
    </>
  )
}
