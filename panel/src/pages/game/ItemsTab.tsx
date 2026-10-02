import { useCallback, useState } from 'react'
import { api } from '../../api'
import type { Item } from '../../api/types'
import { ConfirmDialog } from '../../components/ConfirmDialog'
import { LoadError, Loading } from '../../components/LoadState'
import { PageHeader } from '../../components/PageHeader'
import { t } from '../../i18n'
import { useLoader } from '../../lib/useLoader'
import { useGame } from './GamePage'
import { ItemDialog } from './ItemDialog'

type Open = { kind: 'create' } | { kind: 'edit'; item: Item } | { kind: 'delete'; item: Item }

function traits(item: Item): string {
  const parts: string[] = []
  if (item.stackable) parts.push(t('items.traits.stackable'))
  if (item.isConsumable) parts.push(t('items.traits.consumable'))
  if (item.isRegenerating) parts.push(t('items.traits.regenerating'))
  return parts.join(', ')
}

function priceText(item: Item): string {
  return item.price.map((p) => t('items.priceEntry', { count: p.count, item: p.item })).join(', ')
}

export function ItemsTab() {
  const game = useGame()
  const load = useCallback(() => api.items.list(game.key), [game.key])
  const { data: items, error, loading, reload } = useLoader(load)
  const [open, setOpen] = useState<Open | null>(null)
  const close = () => setOpen(null)
  const names = items?.map((item) => item.name) ?? []

  return (
    <>
      <PageHeader
        level={2}
        title={t('game.tabs.items')}
        intro={t('items.intro')}
        actions={
          <button type="button" className="btn btn-primary" onClick={() => setOpen({ kind: 'create' })}>
            {t('items.new')}
          </button>
        }
      />
      {error ? (
        <LoadError error={error} onRetry={reload} />
      ) : !items ? (
        <Loading />
      ) : items.length === 0 ? (
        <div className="empty-state">
          <p>{t('items.empty')}</p>
        </div>
      ) : (
        <div className="table-wrap" aria-busy={loading}>
          <table className="table">
            <caption className="visually-hidden">{t('game.tabs.items')}</caption>
            <thead>
              <tr>
                <th scope="col">{t('items.columns.name')}</th>
                <th scope="col">{t('items.columns.description')}</th>
                <th scope="col">{t('items.columns.traits')}</th>
                <th scope="col">{t('items.columns.price')}</th>
                <th scope="col">
                  <span className="visually-hidden">{t('common.actions')}</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {items.map((item) => (
                <tr key={item.name}>
                  <th scope="row" data-label={t('items.columns.name')}>
                    {item.name}
                  </th>
                  <td data-label={t('items.columns.description')}>
                    {item.description ? <span className="clamp">{item.description}</span> : <span className="muted">-</span>}
                  </td>
                  <td data-label={t('items.columns.traits')}>{traits(item) || <span className="muted">-</span>}</td>
                  <td data-label={t('items.columns.price')}>{priceText(item) || <span className="muted">{t('items.free')}</span>}</td>
                  <td className="actions">
                    <div className="button-row">
                      <button
                        type="button"
                        className="btn btn-small"
                        onClick={() => setOpen({ kind: 'edit', item })}
                        aria-label={t('common.editWhat', { what: item.name })}
                      >
                        {t('common.edit')}
                      </button>
                      <button
                        type="button"
                        className="btn btn-small btn-danger-quiet"
                        onClick={() => setOpen({ kind: 'delete', item })}
                        aria-label={t('common.deleteWhat', { what: item.name })}
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
        <ItemDialog
          gameKey={game.key}
          item={open.kind === 'edit' ? open.item : undefined}
          itemNames={names}
          onClose={close}
          onSaved={() => {
            close()
            reload()
          }}
        />
      )}
      {open?.kind === 'delete' && (
        <ConfirmDialog
          title={t('items.deleteTitle', { name: open.item.name })}
          confirmLabel={t('common.delete')}
          busyLabel={t('common.deleting')}
          danger
          onClose={close}
          onConfirm={async () => {
            await api.items.remove(game.key, open.item.name)
            close()
            reload()
          }}
        >
          <p>{t('items.deleteBody')}</p>
        </ConfirmDialog>
      )}
    </>
  )
}
