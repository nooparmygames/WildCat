import { useState, type FormEvent } from 'react'
import { api } from '../api'
import type { PublisherDataEntry } from '../api/types'
import { ErrorAlert } from '../components/Alert'
import { ConfirmDialog } from '../components/ConfirmDialog'
import { Dialog } from '../components/Dialog'
import { TextAreaField, TextField } from '../components/Field'
import { LoadError, Loading } from '../components/LoadState'
import { PageHeader } from '../components/PageHeader'
import { t } from '../i18n'
import { checkField, required, unique } from '../lib/validation'
import { useLoader } from '../lib/useLoader'

type Open = { kind: 'create' } | { kind: 'edit'; entry: PublisherDataEntry } | { kind: 'delete'; entry: PublisherDataEntry }

function EntryDialog({
  entry,
  takenKeys,
  onClose,
  onSaved,
}: {
  entry?: PublisherDataEntry
  takenKeys: string[]
  onClose: () => void
  onSaved: () => void
}) {
  const [key, setKey] = useState(entry?.key ?? '')
  const [value, setValue] = useState(entry?.value ?? '')
  const [keyError, setKeyError] = useState<string | undefined>()
  const [error, setError] = useState<unknown>(null)
  const [busy, setBusy] = useState(false)

  async function submit(event: FormEvent) {
    event.preventDefault()
    const message = entry ? undefined : checkField(key, [required(), unique(takenKeys)])
    setKeyError(message)
    if (message) return
    setBusy(true)
    setError(null)
    try {
      await api.publisherData.save(entry?.key ?? key.trim(), value)
      onSaved()
    } catch (e) {
      setError(e)
      setBusy(false)
    }
  }

  return (
    <Dialog title={entry ? t('publisherData.editTitle', { key: entry.key }) : t('publisherData.createTitle')} onClose={onClose} busy={busy}>
      <form onSubmit={submit} noValidate>
        <div className="dialog-body form">
          <ErrorAlert error={error} />
          {entry ? (
            <TextField label={t('publisherData.fields.key')} value={key} onChange={() => {}} readOnly className="mono" />
          ) : (
            <TextField
              label={t('publisherData.fields.key')}
              value={key}
              onChange={setKey}
              error={keyError}
              hint={t('publisherData.fields.keyHint')}
              className="mono"
              autoComplete="off"
              spellCheck={false}
              required
            />
          )}
          <TextAreaField
            label={t('publisherData.fields.value')}
            value={value}
            onChange={setValue}
            rows={8}
            className="mono"
            spellCheck={false}
          />
        </div>
        <div className="dialog-footer">
          <button type="button" className="btn" onClick={onClose} disabled={busy}>
            {t('common.cancel')}
          </button>
          <button type="submit" className="btn btn-primary" disabled={busy}>
            {busy ? t('common.saving') : t('common.save')}
          </button>
        </div>
      </form>
    </Dialog>
  )
}

export function PublisherDataPage() {
  const { data: entries, error, loading, reload } = useLoader(api.publisherData.list)
  const [open, setOpen] = useState<Open | null>(null)
  const close = () => setOpen(null)

  return (
    <>
      <PageHeader
        title={t('publisherData.title')}
        intro={t('publisherData.intro')}
        actions={
          <button type="button" className="btn btn-primary" onClick={() => setOpen({ kind: 'create' })}>
            {t('publisherData.new')}
          </button>
        }
      />
      {error ? (
        <LoadError error={error} onRetry={reload} />
      ) : !entries ? (
        <Loading />
      ) : entries.length === 0 ? (
        <div className="empty-state">
          <p>{t('publisherData.empty')}</p>
        </div>
      ) : (
        <div className="table-wrap" aria-busy={loading}>
          <table className="table">
            <caption className="visually-hidden">{t('publisherData.title')}</caption>
            <thead>
              <tr>
                <th scope="col">{t('publisherData.columns.key')}</th>
                <th scope="col">{t('publisherData.columns.value')}</th>
                <th scope="col">
                  <span className="visually-hidden">{t('common.actions')}</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {entries.map((entry) => (
                <tr key={entry.key}>
                  <th scope="row" data-label={t('publisherData.columns.key')}>
                    <code>{entry.key}</code>
                  </th>
                  <td data-label={t('publisherData.columns.value')}>
                    {entry.value ? (
                      <span className="clamp mono">{entry.value}</span>
                    ) : (
                      <span className="muted">{t('publisherData.emptyValue')}</span>
                    )}
                  </td>
                  <td className="actions">
                    <div className="button-row">
                      <button
                        type="button"
                        className="btn btn-small"
                        onClick={() => setOpen({ kind: 'edit', entry })}
                        aria-label={t('common.editWhat', { what: entry.key })}
                      >
                        {t('common.edit')}
                      </button>
                      <button
                        type="button"
                        className="btn btn-small btn-danger-quiet"
                        onClick={() => setOpen({ kind: 'delete', entry })}
                        aria-label={t('common.deleteWhat', { what: entry.key })}
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
        <EntryDialog
          entry={open.kind === 'edit' ? open.entry : undefined}
          takenKeys={entries?.map((e) => e.key) ?? []}
          onClose={close}
          onSaved={() => {
            close()
            reload()
          }}
        />
      )}
      {open?.kind === 'delete' && (
        <ConfirmDialog
          title={t('publisherData.deleteTitle', { key: open.entry.key })}
          confirmLabel={t('common.delete')}
          busyLabel={t('common.deleting')}
          danger
          onClose={close}
          onConfirm={async () => {
            await api.publisherData.remove(open.entry.key)
            close()
            reload()
          }}
        >
          <p>{t('publisherData.deleteBody')}</p>
        </ConfirmDialog>
      )}
    </>
  )
}
