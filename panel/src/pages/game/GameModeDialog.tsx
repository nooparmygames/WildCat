import { useState, type FormEvent } from 'react'
import { api } from '../../api'
import type { GameMode, GameModeInput } from '../../api/types'
import { ErrorAlert } from '../../components/Alert'
import { Dialog } from '../../components/Dialog'
import { CheckboxField, TextField } from '../../components/Field'
import { t } from '../../i18n'
import { checkField, integer, required, unique } from '../../lib/validation'

export const minPlayersLimit = 2
export const maxPlayersLimit = 1000
/** The default threshold the backend uses for its built-in game mode. */
const defaultThreshold = 10000

interface CriterionRow {
  id: number
  startTimeInSeconds: string
  acceptableThreshold: string
  isMinPlayersEnough: boolean
}

interface Form {
  name: string
  minPlayers: string
  maxPlayers: string
  criteria: CriterionRow[]
}

interface Errors {
  name?: string
  minPlayers?: string
  maxPlayers?: string
  criteria: Record<number, { start?: string; threshold?: string }>
}

let nextRowId = 1

function toForm(mode?: GameMode): Form {
  const criteria = (mode?.criteria.length ? mode.criteria : [{ startTimeInSeconds: 0, acceptableThreshold: defaultThreshold, isMinPlayersEnough: false }]).map(
    (c, index) => ({
      id: nextRowId++,
      // The first step always starts right away.
      startTimeInSeconds: index === 0 ? '0' : String(c.startTimeInSeconds),
      acceptableThreshold: String(c.acceptableThreshold),
      isMinPlayersEnough: c.isMinPlayersEnough,
    }),
  )
  return {
    name: mode?.name ?? '',
    minPlayers: String(mode?.minPlayers ?? 2),
    maxPlayers: String(mode?.maxPlayers ?? 2),
    criteria,
  }
}

/** Exported for tests: checks a game mode form and returns the messages per field. */
export function checkGameMode(form: Form, takenNames: string[] | null): Errors {
  const errors: Errors = { criteria: {} }
  if (takenNames) {
    const name = checkField(form.name, [required(), unique(takenNames)])
    if (name) errors.name = name
  }
  const range = [required(), integer(minPlayersLimit, maxPlayersLimit)]
  errors.minPlayers = checkField(form.minPlayers, range)
  errors.maxPlayers = checkField(form.maxPlayers, range)
  if (!errors.minPlayers && !errors.maxPlayers && Number(form.maxPlayers) < Number(form.minPlayers)) {
    errors.maxPlayers = t('validation.maxBelowMin')
  }

  let previous = -1
  form.criteria.forEach((row, index) => {
    const rowErrors: { start?: string; threshold?: string } = {}
    if (index > 0) {
      rowErrors.start = checkField(row.startTimeInSeconds, [required(), integer(1)])
      if (!rowErrors.start && Number(row.startTimeInSeconds) <= previous) rowErrors.start = t('validation.criteriaOrder')
    }
    rowErrors.threshold = checkField(row.acceptableThreshold, [required(), integer(0)])
    if (rowErrors.start || rowErrors.threshold) errors.criteria[row.id] = rowErrors
    previous = index === 0 ? 0 : Number(row.startTimeInSeconds)
  })
  return errors
}

function isClean(errors: Errors): boolean {
  return !errors.name && !errors.minPlayers && !errors.maxPlayers && Object.keys(errors.criteria).length === 0
}

function toInput(form: Form): GameModeInput {
  return {
    minPlayers: Number(form.minPlayers),
    maxPlayers: Number(form.maxPlayers),
    criteria: form.criteria.map((row, index) => ({
      startTimeInSeconds: index === 0 ? 0 : Number(row.startTimeInSeconds),
      acceptableThreshold: Number(row.acceptableThreshold),
      isMinPlayersEnough: row.isMinPlayersEnough,
    })),
  }
}

interface GameModeDialogProps {
  gameKey: string
  mode?: GameMode
  takenNames: string[]
  onClose: () => void
  onSaved: () => void
}

export function GameModeDialog({ gameKey, mode, takenNames, onClose, onSaved }: GameModeDialogProps) {
  const [form, setForm] = useState<Form>(() => toForm(mode))
  const [errors, setErrors] = useState<Errors>({ criteria: {} })
  const [error, setError] = useState<unknown>(null)
  const [busy, setBusy] = useState(false)

  const update = <K extends keyof Form>(field: K, value: Form[K]) => setForm((f) => ({ ...f, [field]: value }))
  const updateRow = (id: number, change: Partial<CriterionRow>) =>
    update(
      'criteria',
      form.criteria.map((row) => (row.id === id ? { ...row, ...change } : row)),
    )

  async function submit(event: FormEvent) {
    event.preventDefault()
    const found = checkGameMode(form, mode ? null : takenNames)
    setErrors(found)
    if (!isClean(found)) return
    setBusy(true)
    setError(null)
    try {
      await api.gameModes.save(gameKey, mode?.name ?? form.name.trim(), toInput(form))
      onSaved()
    } catch (e) {
      setError(e)
      setBusy(false)
    }
  }

  const lastStart = Number(form.criteria[form.criteria.length - 1]?.startTimeInSeconds ?? 0) || 0

  return (
    <Dialog
      title={mode ? t('gameModes.editTitle', { name: mode.name }) : t('gameModes.createTitle')}
      onClose={onClose}
      busy={busy}
      size="large"
    >
      <form onSubmit={submit} noValidate>
        <div className="dialog-body form">
          <ErrorAlert error={error} />
          {mode ? (
            <TextField label={t('gameModes.fields.name')} value={form.name} onChange={() => {}} readOnly hint={t('gameModes.fields.nameFixed')} />
          ) : (
            <TextField
              label={t('gameModes.fields.name')}
              value={form.name}
              onChange={(v) => update('name', v)}
              hint={t('gameModes.fields.nameHint')}
              error={errors.name}
              required
            />
          )}
          <div className="form-grid">
            <TextField
              label={t('gameModes.fields.minPlayers')}
              type="number"
              min={minPlayersLimit}
              max={maxPlayersLimit}
              inputMode="numeric"
              value={form.minPlayers}
              onChange={(v) => update('minPlayers', v)}
              error={errors.minPlayers}
              required
            />
            <TextField
              label={t('gameModes.fields.maxPlayers')}
              type="number"
              min={minPlayersLimit}
              max={maxPlayersLimit}
              inputMode="numeric"
              value={form.maxPlayers}
              onChange={(v) => update('maxPlayers', v)}
              error={errors.maxPlayers}
              required
            />
          </div>

          <section className="form-section" aria-labelledby="criteria-title">
            <h3 id="criteria-title" className="section-title">
              {t('gameModes.criteriaTitle')}
            </h3>
            <p className="field-hint">{t('gameModes.criteriaHint')}</p>
            <ol className="row-list">
              {form.criteria.map((row, index) => (
                <li key={row.id}>
                  <fieldset className="row-item criterion">
                    <legend>{t('gameModes.step', { n: index + 1 })}</legend>
                    <div className="row-fields">
                      <TextField
                        label={t('gameModes.fields.startTime')}
                        type="number"
                        min={index === 0 ? 0 : 1}
                        inputMode="numeric"
                        value={row.startTimeInSeconds}
                        onChange={(v) => updateRow(row.id, { startTimeInSeconds: v })}
                        readOnly={index === 0}
                        hint={index === 0 ? t('gameModes.fields.firstStepHint') : undefined}
                        error={errors.criteria[row.id]?.start}
                      />
                      <TextField
                        label={t('gameModes.fields.threshold')}
                        type="number"
                        min={0}
                        inputMode="numeric"
                        value={row.acceptableThreshold}
                        onChange={(v) => updateRow(row.id, { acceptableThreshold: v })}
                        error={errors.criteria[row.id]?.threshold}
                      />
                      <CheckboxField
                        label={t('gameModes.fields.minPlayersEnough')}
                        checked={row.isMinPlayersEnough}
                        onCheckedChange={(v) => updateRow(row.id, { isMinPlayersEnough: v })}
                      />
                    </div>
                    {index > 0 && (
                      <button
                        type="button"
                        className="btn btn-small btn-ghost"
                        onClick={() =>
                          update(
                            'criteria',
                            form.criteria.filter((r) => r.id !== row.id),
                          )
                        }
                        aria-label={t('gameModes.removeStep', { n: index + 1 })}
                      >
                        {t('common.remove')}
                      </button>
                    )}
                  </fieldset>
                </li>
              ))}
            </ol>
            <div>
              <button
                type="button"
                className="btn btn-small"
                onClick={() =>
                  update('criteria', [
                    ...form.criteria,
                    {
                      id: nextRowId++,
                      startTimeInSeconds: String(lastStart + 10),
                      acceptableThreshold: form.criteria[form.criteria.length - 1]?.acceptableThreshold ?? String(defaultThreshold),
                      isMinPlayersEnough: false,
                    },
                  ])
                }
              >
                {t('gameModes.addStep')}
              </button>
            </div>
          </section>
        </div>
        <div className="dialog-footer">
          {!isClean(errors) && (
            <p className="field-error footer-message" role="alert">
              {t('validation.fixErrors')}
            </p>
          )}
          <button type="button" className="btn" onClick={onClose} disabled={busy}>
            {t('common.cancel')}
          </button>
          <button type="submit" className="btn btn-primary" disabled={busy}>
            {busy ? t('common.saving') : mode ? t('common.save') : t('common.create')}
          </button>
        </div>
      </form>
    </Dialog>
  )
}
