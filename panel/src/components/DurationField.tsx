import { useId } from 'react'
import { t } from '../i18n'
import { durationUnits, splitDuration, type DurationUnit } from '../lib/timespan'

export interface DurationValue {
  amount: string
  unit: DurationUnit
}

export function durationFromSeconds(seconds: number): DurationValue {
  const { amount, unit } = splitDuration(seconds)
  return { amount: String(amount), unit }
}

export function durationToSeconds(value: DurationValue): number {
  const amount = Number(value.amount.trim() || '0')
  return Math.round(amount * durationUnits[value.unit])
}

interface DurationFieldProps {
  label: string
  value: DurationValue
  onChange: (value: DurationValue) => void
  hint?: string
  error?: string
  disabled?: boolean
}

/** A number plus a unit (seconds to days), for TimeSpan fields. */
export function DurationField({ label, value, onChange, hint, error, disabled }: DurationFieldProps) {
  const id = useId()
  const described = [hint ? `${id}-hint` : '', error ? `${id}-error` : ''].filter(Boolean).join(' ') || undefined
  return (
    <fieldset className={`field duration${error ? ' field-invalid' : ''}`} disabled={disabled}>
      <legend>{label}</legend>
      <div className="field-inline">
        <input
          type="number"
          min={0}
          step="any"
          inputMode="decimal"
          aria-label={t('duration.amount', { label })}
          value={value.amount}
          onChange={(event) => onChange({ ...value, amount: event.target.value })}
          aria-invalid={error ? true : undefined}
          aria-describedby={described}
        />
        <select
          aria-label={t('duration.unit', { label })}
          value={value.unit}
          onChange={(event) => onChange({ ...value, unit: event.target.value as DurationUnit })}
        >
          {(Object.keys(durationUnits) as DurationUnit[]).map((unit) => (
            <option key={unit} value={unit}>
              {t(`duration.units.${unit}`)}
            </option>
          ))}
        </select>
      </div>
      {hint && (
        <p className="field-hint" id={`${id}-hint`}>
          {hint}
        </p>
      )}
      {error && (
        <p className="field-error" id={`${id}-error`}>
          {error}
        </p>
      )}
    </fieldset>
  )
}

/** Checks a duration amount: empty counts as zero, negatives and junk are rejected. */
export function checkDuration(value: DurationValue, { positive = false } = {}): string | undefined {
  const text = value.amount.trim()
  const amount = Number(text || '0')
  if (!Number.isFinite(amount) || amount < 0) return t('validation.duration')
  if (positive && amount <= 0) return t('validation.durationPositive')
  return undefined
}
