import { useId, useState, type FormEvent } from 'react'
import { api } from '../../api'
import type { Item, ItemInput } from '../../api/types'
import { ErrorAlert } from '../../components/Alert'
import { Dialog } from '../../components/Dialog'
import { checkDuration, DurationField, durationFromSeconds, durationToSeconds, type DurationValue } from '../../components/DurationField'
import { CheckboxField, TextAreaField, TextField } from '../../components/Field'
import { t } from '../../i18n'
import { checkField, integer, required, unique } from '../../lib/validation'

interface Row {
  id: number
  name: string
  value: string
}

interface PriceRow {
  id: number
  item: string
  count: string
}

interface Form {
  name: string
  description: string
  stackable: boolean
  maxAllowedToHave: string
  isConsumable: boolean
  automaticallyConsumed: boolean
  effectTime: DurationValue
  isRegenerating: boolean
  regenerationAmount: string
  regenerationPeriod: DurationValue
  metaData: Row[]
  price: PriceRow[]
}

type Errors = Partial<Record<'name' | 'maxAllowedToHave' | 'effectTime' | 'regenerationAmount' | 'regenerationPeriod', string>> & {
  metaData?: Record<number, string>
  price?: Record<number, string>
}

let nextRowId = 1
const rowId = () => nextRowId++

function toForm(item?: Item): Form {
  return {
    name: item?.name ?? '',
    description: item?.description ?? '',
    stackable: item?.stackable ?? true,
    maxAllowedToHave: String(item?.maxAllowedToHave ?? 0),
    isConsumable: item?.isConsumable ?? false,
    automaticallyConsumed: item?.automaticallyConsumed ?? false,
    effectTime: durationFromSeconds(item?.effectTime ?? 0),
    isRegenerating: item?.isRegenerating ?? false,
    regenerationAmount: String(item?.regenerationAmount ?? 1),
    regenerationPeriod: durationFromSeconds(item?.regenerationPeriod ?? 3600),
    metaData: Object.entries(item?.metaData ?? {}).map(([name, value]) => ({ id: rowId(), name, value })),
    price: (item?.price ?? []).map((p) => ({ id: rowId(), item: p.item, count: String(p.count) })),
  }
}

function check(form: Form, takenNames: string[], editing: boolean): Errors {
  const errors: Errors = {}
  const name = editing ? undefined : checkField(form.name, [required(), unique(takenNames)])
  if (name) errors.name = name
  const max = checkField(form.maxAllowedToHave, [required(), integer(0)])
  if (max) errors.maxAllowedToHave = max
  const effect = checkDuration(form.effectTime)
  if (effect) errors.effectTime = effect
  if (form.isRegenerating) {
    const amount = checkField(form.regenerationAmount, [required(), integer(1)])
    if (amount) errors.regenerationAmount = amount
    const period = checkDuration(form.regenerationPeriod, { positive: true })
    if (period) errors.regenerationPeriod = period
  }

  const seen = new Set<string>()
  for (const row of form.metaData) {
    const key = row.name.trim()
    const message = key === '' ? t('validation.required') : seen.has(key) ? t('validation.duplicate') : undefined
    if (message) (errors.metaData ??= {})[row.id] = message
    seen.add(key)
  }
  for (const row of form.price) {
    const message = checkField(row.item, [required()]) ?? checkField(row.count, [required(), integer(1)])
    if (message) (errors.price ??= {})[row.id] = message
  }
  return errors
}

function toInput(form: Form): ItemInput {
  const metaData: Record<string, string> = {}
  for (const row of form.metaData) metaData[row.name.trim()] = row.value
  return {
    description: form.description.trim(),
    stackable: form.stackable,
    maxAllowedToHave: Number.parseInt(form.maxAllowedToHave, 10),
    isConsumable: form.isConsumable,
    automaticallyConsumed: form.isConsumable && form.automaticallyConsumed,
    effectTime: durationToSeconds(form.effectTime),
    isRegenerating: form.isRegenerating,
    regenerationAmount: form.isRegenerating ? Number.parseInt(form.regenerationAmount, 10) : 0,
    regenerationPeriod: form.isRegenerating ? durationToSeconds(form.regenerationPeriod) : 0,
    metaData,
    price: form.price.map((row) => ({ item: row.item.trim(), count: Number.parseInt(row.count, 10) })),
  }
}

interface ItemDialogProps {
  gameKey: string
  item?: Item
  /** Names of every item in the game, for uniqueness and price suggestions. */
  itemNames: string[]
  onClose: () => void
  onSaved: () => void
}

export function ItemDialog({ gameKey, item, itemNames, onClose, onSaved }: ItemDialogProps) {
  const [form, setForm] = useState<Form>(() => toForm(item))
  const [errors, setErrors] = useState<Errors>({})
  const [error, setError] = useState<unknown>(null)
  const [busy, setBusy] = useState(false)
  const listId = useId()

  const update = <K extends keyof Form>(field: K, value: Form[K]) => setForm((f) => ({ ...f, [field]: value }))

  async function submit(event: FormEvent) {
    event.preventDefault()
    const found = check(form, itemNames, !!item)
    setErrors(found)
    if (Object.keys(found).length > 0) return
    setBusy(true)
    setError(null)
    try {
      await api.items.save(gameKey, item?.name ?? form.name.trim(), toInput(form))
      onSaved()
    } catch (e) {
      setError(e)
      setBusy(false)
    }
  }

  const otherItems = itemNames.filter((name) => name !== (item?.name ?? form.name.trim()))

  return (
    <Dialog title={item ? t('items.editTitle', { name: item.name }) : t('items.createTitle')} onClose={onClose} busy={busy} size="large">
      <form onSubmit={submit} noValidate>
        <div className="dialog-body form">
          <ErrorAlert error={error} />
          {item ? (
            <TextField label={t('items.fields.name')} value={form.name} onChange={() => {}} readOnly hint={t('items.fields.nameFixed')} />
          ) : (
            <TextField
              label={t('items.fields.name')}
              value={form.name}
              onChange={(v) => update('name', v)}
              error={errors.name}
              required
            />
          )}
          <TextAreaField
            label={t('items.fields.description')}
            value={form.description}
            onChange={(v) => update('description', v)}
            rows={2}
          />

          <fieldset className="form-section">
            <legend>{t('items.sections.holding')}</legend>
            <div className="form-grid">
              <CheckboxField
                label={t('items.fields.stackable')}
                hint={t('items.fields.stackableHint')}
                checked={form.stackable}
                onCheckedChange={(v) => update('stackable', v)}
              />
              <TextField
                label={t('items.fields.maxAllowedToHave')}
                type="number"
                min={0}
                inputMode="numeric"
                value={form.maxAllowedToHave}
                onChange={(v) => update('maxAllowedToHave', v)}
                hint={t('items.fields.maxAllowedToHaveHint')}
                error={errors.maxAllowedToHave}
              />
            </div>
          </fieldset>

          <fieldset className="form-section">
            <legend>{t('items.sections.using')}</legend>
            <div className="form-grid">
              <div className="stack">
                <CheckboxField
                  label={t('items.fields.isConsumable')}
                  hint={t('items.fields.isConsumableHint')}
                  checked={form.isConsumable}
                  onCheckedChange={(v) => update('isConsumable', v)}
                />
                <CheckboxField
                  label={t('items.fields.automaticallyConsumed')}
                  hint={t('items.fields.automaticallyConsumedHint')}
                  checked={form.isConsumable && form.automaticallyConsumed}
                  disabled={!form.isConsumable}
                  onCheckedChange={(v) => update('automaticallyConsumed', v)}
                />
              </div>
              <DurationField
                label={t('items.fields.effectTime')}
                hint={t('items.fields.effectTimeHint')}
                value={form.effectTime}
                onChange={(v) => update('effectTime', v)}
                error={errors.effectTime}
              />
            </div>
          </fieldset>

          <fieldset className="form-section">
            <legend>{t('items.sections.regeneration')}</legend>
            <CheckboxField
              label={t('items.fields.isRegenerating')}
              hint={t('items.fields.isRegeneratingHint')}
              checked={form.isRegenerating}
              onCheckedChange={(v) => update('isRegenerating', v)}
            />
            {form.isRegenerating && (
              <div className="form-grid">
                <TextField
                  label={t('items.fields.regenerationAmount')}
                  type="number"
                  min={1}
                  inputMode="numeric"
                  value={form.regenerationAmount}
                  onChange={(v) => update('regenerationAmount', v)}
                  error={errors.regenerationAmount}
                />
                <DurationField
                  label={t('items.fields.regenerationPeriod')}
                  value={form.regenerationPeriod}
                  onChange={(v) => update('regenerationPeriod', v)}
                  error={errors.regenerationPeriod}
                />
              </div>
            )}
          </fieldset>

          <fieldset className="form-section">
            <legend>{t('items.sections.metaData')}</legend>
            <p className="field-hint">{t('items.fields.metaDataHint')}</p>
            {form.metaData.length > 0 && (
              <ul className="row-list">
                {form.metaData.map((row, index) => (
                  <li key={row.id} className="row-item">
                    <div className="row-fields">
                      <TextField
                        label={t('items.fields.metaName', { n: index + 1 })}
                        value={row.name}
                        onChange={(v) =>
                          update(
                            'metaData',
                            form.metaData.map((r) => (r.id === row.id ? { ...r, name: v } : r)),
                          )
                        }
                        error={errors.metaData?.[row.id]}
                      />
                      <TextField
                        label={t('items.fields.metaValue', { n: index + 1 })}
                        value={row.value}
                        onChange={(v) =>
                          update(
                            'metaData',
                            form.metaData.map((r) => (r.id === row.id ? { ...r, value: v } : r)),
                          )
                        }
                      />
                    </div>
                    <button
                      type="button"
                      className="btn btn-small btn-ghost"
                      onClick={() => update('metaData', form.metaData.filter((r) => r.id !== row.id))}
                      aria-label={t('items.removeMeta', { n: index + 1 })}
                    >
                      {t('common.remove')}
                    </button>
                  </li>
                ))}
              </ul>
            )}
            <button
              type="button"
              className="btn btn-small"
              onClick={() => update('metaData', [...form.metaData, { id: rowId(), name: '', value: '' }])}
            >
              {t('items.addMeta')}
            </button>
          </fieldset>

          <fieldset className="form-section">
            <legend>{t('items.sections.price')}</legend>
            <p className="field-hint">{t('items.fields.priceHint')}</p>
            <datalist id={listId}>
              {otherItems.map((name) => (
                <option key={name} value={name} />
              ))}
            </datalist>
            {form.price.length > 0 && (
              <ul className="row-list">
                {form.price.map((row, index) => (
                  <li key={row.id} className="row-item">
                    <div className="row-fields">
                      <TextField
                        label={t('items.fields.priceItem', { n: index + 1 })}
                        list={listId}
                        value={row.item}
                        onChange={(v) =>
                          update(
                            'price',
                            form.price.map((r) => (r.id === row.id ? { ...r, item: v } : r)),
                          )
                        }
                        error={errors.price?.[row.id]}
                      />
                      <TextField
                        label={t('items.fields.priceCount', { n: index + 1 })}
                        type="number"
                        min={1}
                        inputMode="numeric"
                        value={row.count}
                        onChange={(v) =>
                          update(
                            'price',
                            form.price.map((r) => (r.id === row.id ? { ...r, count: v } : r)),
                          )
                        }
                      />
                    </div>
                    <button
                      type="button"
                      className="btn btn-small btn-ghost"
                      onClick={() => update('price', form.price.filter((r) => r.id !== row.id))}
                      aria-label={t('items.removePrice', { n: index + 1 })}
                    >
                      {t('common.remove')}
                    </button>
                  </li>
                ))}
              </ul>
            )}
            <button
              type="button"
              className="btn btn-small"
              onClick={() => update('price', [...form.price, { id: rowId(), item: '', count: '1' }])}
            >
              {t('items.addPrice')}
            </button>
          </fieldset>
        </div>
        <div className="dialog-footer">
          {Object.keys(errors).length > 0 && (
            <p className="field-error footer-message" role="alert">
              {t('validation.fixErrors')}
            </p>
          )}
          <button type="button" className="btn" onClick={onClose} disabled={busy}>
            {t('common.cancel')}
          </button>
          <button type="submit" className="btn btn-primary" disabled={busy}>
            {busy ? t('common.saving') : item ? t('common.save') : t('common.create')}
          </button>
        </div>
      </form>
    </Dialog>
  )
}
