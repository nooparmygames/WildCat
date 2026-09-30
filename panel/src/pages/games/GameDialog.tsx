import { useState, type FormEvent } from 'react'
import { api } from '../../api'
import { ageRatingKeys, genreKeys, regionKeys, type Game, type GameInput } from '../../api/types'
import { ErrorAlert } from '../../components/Alert'
import { Dialog } from '../../components/Dialog'
import { SelectField, TextAreaField, TextField } from '../../components/Field'
import { t } from '../../i18n'
import { hasErrors, required, url, validate, type FieldErrors } from '../../lib/validation'

type Fields = 'name' | 'description' | 'studioName' | 'website' | 'genre' | 'region' | 'platform' | 'ageRating'

// Built on each render (not at import time) so they follow the current language.
const genreOptions = () => genreKeys.map((key, index) => ({ value: String(index), label: t(`games.genres.${key}`) }))
const regionOptions = () => regionKeys.map((key, index) => ({ value: String(index), label: t(`games.regions.${key}`) }))
const ageRatingOptions = () =>
  ageRatingKeys.map((key, index) => ({ value: String(index), label: t(`games.ageRatings.${key}`) }))

export function genreLabel(genre: number): string {
  const key = genreKeys[genre]
  return key ? t(`games.genres.${key}`) : ''
}

interface GameDialogProps {
  /** The game to edit; leave out to create one. */
  game?: Game
  onClose: () => void
  /** Called with the saved game after a successful create or update. */
  onSaved: (game: Game) => void
}

export function GameDialog({ game, onClose, onSaved }: GameDialogProps) {
  const [values, setValues] = useState<Record<Fields, string>>({
    name: game?.name ?? '',
    description: game?.description ?? '',
    studioName: game?.studioName ?? '',
    website: game?.website ?? '',
    genre: String(game?.genre ?? 0),
    region: String(game?.region ?? 0),
    platform: game?.platform ?? '',
    ageRating: String(game?.ageRating ?? 0),
  })
  const [errors, setErrors] = useState<FieldErrors<Fields>>({})
  const [error, setError] = useState<unknown>(null)
  const [busy, setBusy] = useState(false)

  const set = (field: Fields) => (value: string) => setValues((v) => ({ ...v, [field]: value }))

  async function submit(event: FormEvent) {
    event.preventDefault()
    const found = validate(values, { name: [required()], website: [url()] })
    setErrors(found)
    if (hasErrors(found)) return
    const input: GameInput = {
      name: values.name.trim(),
      description: values.description.trim(),
      studioName: values.studioName.trim(),
      website: values.website.trim(),
      genre: Number(values.genre),
      region: Number(values.region),
      platform: values.platform.trim(),
      ageRating: Number(values.ageRating),
    }
    setBusy(true)
    setError(null)
    try {
      if (game) {
        await api.games.update(game.key, input)
        onSaved({ ...input, key: game.key })
      } else {
        const key = await api.games.create(input)
        onSaved({ ...input, key })
      }
    } catch (e) {
      setError(e)
      setBusy(false)
    }
  }

  return (
    <Dialog title={game ? t('games.editTitle', { name: game.name }) : t('games.createTitle')} onClose={onClose} busy={busy}>
      <form onSubmit={submit} noValidate>
        <div className="dialog-body form">
          <ErrorAlert error={error} />
          <TextField
            label={t('games.fields.name')}
            value={values.name}
            onChange={set('name')}
            error={errors.name}
            required
            maxLength={200}
          />
          <TextAreaField
            label={t('games.fields.description')}
            value={values.description}
            onChange={set('description')}
            rows={3}
          />
          <div className="form-grid">
            <TextField label={t('games.fields.studioName')} value={values.studioName} onChange={set('studioName')} />
            <TextField
              label={t('games.fields.website')}
              type="url"
              inputMode="url"
              placeholder="https://"
              value={values.website}
              onChange={set('website')}
              error={errors.website}
            />
            <SelectField label={t('games.fields.genre')} value={values.genre} onChange={set('genre')} options={genreOptions()} />
            <TextField
              label={t('games.fields.platform')}
              value={values.platform}
              onChange={set('platform')}
              hint={t('games.fields.platformHint')}
            />
            <SelectField
              label={t('games.fields.region')}
              value={values.region}
              onChange={set('region')}
              options={regionOptions()}
            />
            <SelectField
              label={t('games.fields.ageRating')}
              value={values.ageRating}
              onChange={set('ageRating')}
              options={ageRatingOptions()}
            />
          </div>
        </div>
        <div className="dialog-footer">
          <button type="button" className="btn" onClick={onClose} disabled={busy}>
            {t('common.cancel')}
          </button>
          <button type="submit" className="btn btn-primary" disabled={busy}>
            {busy ? t('common.saving') : game ? t('common.save') : t('games.create')}
          </button>
        </div>
      </form>
    </Dialog>
  )
}
