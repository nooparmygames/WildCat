import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react'
import { Link, useBlocker, useLocation, useNavigate, useParams } from 'react-router'
import { api } from '../../api'
import { isApiError } from '../../api/client'
import { parameterTypes, type EventParameter, type ScriptEvent } from '../../api/types'
import { ErrorAlert, Notice } from '../../components/Alert'
import { ConfirmDialog } from '../../components/ConfirmDialog'
import { TextField } from '../../components/Field'
import { LoadError, Loading, NotFound } from '../../components/LoadState'
import { t } from '../../i18n'
import { useLoader } from '../../lib/useLoader'
import { checkField, identifier, required, unique } from '../../lib/validation'
import { useGame } from './GamePage'
import { ScriptEditor } from './ScriptEditor'
import { methodSignature, methodTemplate } from './scriptEvents'

interface ParamRow extends EventParameter {
  id: number
}

let nextRowId = 1

function toRows(parameters: EventParameter[]): ParamRow[] {
  return parameters.map((p) => ({ ...p, id: nextRowId++ }))
}

/** Loads the event (when editing) and hands it to the form. */
export function EventEditorPage() {
  const game = useGame()
  const { eventName } = useParams()
  const navigate = useNavigate()
  const load = useCallback(() => api.events.list(game.key), [game.key])
  const { data: events, error, reload } = useLoader(load)
  // Events saved on this page, so a newly created one is found before the list reloads.
  const [savedEvents, setSavedEvents] = useState<Record<string, ScriptEvent>>({})

  if (error) return <LoadError error={error} onRetry={reload} />
  if (!events) return <Loading />
  const all = [...events.filter((e) => !savedEvents[e.name]), ...Object.values(savedEvents)]
  const existing = eventName ? all.find((e) => e.name === eventName) : undefined
  if (eventName && !existing) return <NotFound />
  return (
    <EventForm
      key={eventName ?? 'new'}
      gameKey={game.key}
      event={existing}
      takenNames={all.map((e) => e.name)}
      onSaved={(saved, created) => {
        setSavedEvents((current) => ({ ...current, [saved.name]: saved }))
        if (created) navigate(`../${encodeURIComponent(saved.name)}`, { replace: true, relative: 'path', state: { created: true } })
      }}
    />
  )
}

interface EventFormProps {
  gameKey: string
  event?: ScriptEvent
  takenNames: string[]
  /** Called after a successful save; created is true for a new event. */
  onSaved: (event: ScriptEvent, created: boolean) => void
}

function EventForm({ gameKey, event, takenNames, onSaved }: EventFormProps) {
  const location = useLocation()
  const [name, setName] = useState(event?.name ?? '')
  const [rows, setRows] = useState<ParamRow[]>(() => toRows(event?.parameters ?? []))
  const [script, setScript] = useState(event?.script ?? '')
  const [dirty, setDirtyState] = useState(false)
  // The blocker reads this during navigation, which can happen before a re-render.
  const dirtyRef = useRef(false)
  const setDirty = (value: boolean) => {
    dirtyRef.current = value
    setDirtyState(value)
  }
  const [nameError, setNameError] = useState<string | undefined>()
  const [rowErrors, setRowErrors] = useState<Record<number, string>>({})
  const [error, setError] = useState<unknown>(null)
  const [busy, setBusy] = useState(false)
  // A newly created event arrives here with { created: true } after the page moves to its own URL.
  const [saved, setSaved] = useState(() => (location.state as { created?: boolean } | null)?.created === true)

  const blocker = useBlocker(
    ({ currentLocation, nextLocation }) => dirtyRef.current && currentLocation.pathname !== nextLocation.pathname,
  )

  // Warn before closing the tab with unsaved changes too.
  useEffect(() => {
    if (!dirty) return
    const warn = (e: BeforeUnloadEvent) => e.preventDefault()
    window.addEventListener('beforeunload', warn)
    return () => window.removeEventListener('beforeunload', warn)
  }, [dirty])

  const touch = () => {
    setDirty(true)
    setSaved(false)
  }
  const changeRows = (next: ParamRow[]) => {
    setRows(next)
    touch()
  }
  const updateRow = (id: number, change: Partial<ParamRow>) => changeRows(rows.map((r) => (r.id === id ? { ...r, ...change } : r)))
  const moveRow = (index: number, delta: number) => {
    const next = [...rows]
    const [row] = next.splice(index, 1)
    if (!row) return
    next.splice(index + delta, 0, row)
    changeRows(next)
  }

  const eventNameForSignature = event?.name ?? name.trim()
  const parameters: EventParameter[] = rows.map(({ name: n, type }) => ({ name: n.trim(), type }))

  function checkForm(): boolean {
    const nameMessage = event
      ? undefined
      : checkField(name, [required(), identifier(), unique(takenNames)])
    setNameError(nameMessage)
    const found: Record<number, string> = {}
    const seen = new Set<string>()
    for (const row of rows) {
      const message =
        checkField(row.name, [required(), identifier()]) ?? (seen.has(row.name.trim()) ? t('validation.duplicate') : undefined)
      if (message) found[row.id] = message
      seen.add(row.name.trim())
    }
    setRowErrors(found)
    return !nameMessage && Object.keys(found).length === 0
  }

  async function submit(e: FormEvent) {
    e.preventDefault()
    if (!checkForm()) return
    const target = event?.name ?? name.trim()
    setBusy(true)
    setError(null)
    try {
      await api.events.save(gameKey, target, { parameters, script })
      setDirty(false)
      setSaved(true)
      onSaved({ name: target, parameters, script }, !event)
    } catch (err) {
      setError(err)
    } finally {
      setBusy(false)
    }
  }

  const insertTemplate = () => {
    const template = methodTemplate(eventNameForSignature, parameters)
    setScript((current) => (current.trim() === '' ? template : `${current.replace(/\s*$/, '')}\n\n${template}`))
    touch()
  }

  const scriptInvalid = isApiError(error) && error.status === 400

  return (
    <form className="form editor" onSubmit={submit} noValidate>
      <p className="breadcrumb">
        <Link to=".." relative="path">
          {t('events.backToList')}
        </Link>
      </p>
      <div className="page-header">
        <div className="page-header-text">
          <h2>{event ? t('events.editTitle', { name: event.name }) : t('events.createTitle')}</h2>
        </div>
      </div>

      {event ? (
        <TextField label={t('events.fields.name')} value={name} onChange={() => {}} readOnly hint={t('events.fields.nameFixed')} />
      ) : (
        <TextField
          label={t('events.fields.name')}
          value={name}
          onChange={(v) => {
            setName(v)
            touch()
          }}
          hint={t('events.fields.nameHint')}
          error={nameError}
          required
          autoComplete="off"
          spellCheck={false}
        />
      )}

      <fieldset className="form-section">
        <legend>{t('events.fields.parameters')}</legend>
        <p className="field-hint">{t('events.fields.parametersHint')}</p>
        {rows.length > 0 && (
          <ol className="row-list">
            {rows.map((row, index) => (
              <li key={row.id} className="row-item">
                <div className="row-fields">
                  <TextField
                    label={t('events.fields.parameterName', { n: index + 1 })}
                    value={row.name}
                    onChange={(v) => updateRow(row.id, { name: v })}
                    error={rowErrors[row.id]}
                    autoComplete="off"
                    spellCheck={false}
                  />
                  <div className="field">
                    <label htmlFor={`param-type-${row.id}`}>{t('events.fields.parameterType', { n: index + 1 })}</label>
                    <select
                      id={`param-type-${row.id}`}
                      value={String(row.type)}
                      onChange={(e) => updateRow(row.id, { type: Number(e.target.value) })}
                    >
                      {parameterTypes.map((type, typeIndex) => (
                        <option key={type} value={String(typeIndex)}>
                          {t(`events.types.${type}`)}
                        </option>
                      ))}
                    </select>
                  </div>
                </div>
                <div className="button-row row-buttons">
                  <button
                    type="button"
                    className="btn btn-small btn-ghost"
                    onClick={() => moveRow(index, -1)}
                    disabled={index === 0}
                    aria-label={t('events.moveUp', { n: index + 1 })}
                  >
                    {t('common.moveUp')}
                  </button>
                  <button
                    type="button"
                    className="btn btn-small btn-ghost"
                    onClick={() => moveRow(index, 1)}
                    disabled={index === rows.length - 1}
                    aria-label={t('events.moveDown', { n: index + 1 })}
                  >
                    {t('common.moveDown')}
                  </button>
                  <button
                    type="button"
                    className="btn btn-small btn-ghost"
                    onClick={() => changeRows(rows.filter((r) => r.id !== row.id))}
                    aria-label={t('events.removeParameter', { n: index + 1 })}
                  >
                    {t('common.remove')}
                  </button>
                </div>
              </li>
            ))}
          </ol>
        )}
        <div>
          <button
            type="button"
            className="btn btn-small"
            onClick={() => changeRows([...rows, { id: nextRowId++, name: '', type: 3 }])}
          >
            {t('events.addParameter')}
          </button>
        </div>
      </fieldset>

      <section className="form-section" aria-labelledby="signature-title">
        <h3 id="signature-title" className="section-title">
          {t('events.signatureTitle')}
        </h3>
        <p className="field-hint">{t('events.signatureHint')}</p>
        <pre className="signature">
          <code>{methodSignature(eventNameForSignature, parameters)}</code>
        </pre>
        <div>
          <button type="button" className="btn btn-small" onClick={insertTemplate}>
            {t('events.insertTemplate')}
          </button>
        </div>
      </section>

      <ScriptEditor
        label={t('events.fields.script')}
        value={script}
        onChange={(v) => {
          setScript(v)
          touch()
        }}
        invalid={scriptInvalid}
      />

      <ErrorAlert error={error} title={scriptInvalid ? t('events.invalidTitle') : undefined} />
      {saved && <Notice tone="success">{t('events.saved')}</Notice>}

      <div className="form-actions">
        <Link className="btn" to=".." relative="path">
          {t('common.cancel')}
        </Link>
        <button type="submit" className="btn btn-primary" disabled={busy}>
          {busy ? t('events.saving') : t('events.save')}
        </button>
      </div>

      {blocker.state === 'blocked' && (
        <ConfirmDialog
          title={t('events.leaveTitle')}
          confirmLabel={t('events.leaveConfirm')}
          danger
          onClose={() => blocker.reset()}
          onConfirm={async () => blocker.proceed()}
        >
          <p>{t('events.leaveBody')}</p>
        </ConfirmDialog>
      )}
    </form>
  )
}
