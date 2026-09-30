// Small form validation helpers. Form state is kept as strings (what inputs give us); each field
// gets a list of rules and the first failing rule's message is shown next to the field.

import { t } from '../i18n'

/** Returns an error message, or undefined when the value is fine. */
export type Rule = (value: string) => string | undefined

export type FieldErrors<K extends string = string> = Partial<Record<K, string>>

export const minPasswordLength = 8

// Deliberately loose: something@something.something with no spaces. The server has the final say.
const emailPattern = /^[^\s@]+@[^\s@]+\.[^\s@]+$/
const identifierPattern = /^[A-Za-z_][A-Za-z0-9_]*$/
const integerPattern = /^-?\d+$/

export const required =
  (): Rule =>
  (value) =>
    value.trim() === '' ? t('validation.required') : undefined

export const email =
  (): Rule =>
  (value) =>
    value.trim() === '' || emailPattern.test(value.trim()) ? undefined : t('validation.email')

export const minLength =
  (min: number): Rule =>
  (value) =>
    value !== '' && value.length < min ? t('validation.minLength', { min }) : undefined

export const matches =
  (other: string): Rule =>
  (value) =>
    value !== other ? t('validation.passwordMismatch') : undefined

/** A C# identifier, which is what event, parameter and similar names end up as. */
export const identifier =
  (): Rule =>
  (value) =>
    value.trim() === '' || identifierPattern.test(value.trim()) ? undefined : t('validation.identifier')

export const url =
  (): Rule =>
  (value) => {
    const text = value.trim()
    if (text === '') return undefined
    try {
      const parsed = new URL(text)
      return parsed.protocol === 'http:' || parsed.protocol === 'https:' ? undefined : t('validation.url')
    } catch {
      return t('validation.url')
    }
  }

/** A whole number, optionally within [min, max]. Empty values pass; combine with required(). */
export const integer =
  (min?: number, max?: number): Rule =>
  (value) => {
    const text = value.trim()
    if (text === '') return undefined
    if (!integerPattern.test(text)) return t('validation.integer')
    const n = Number(text)
    if (min !== undefined && max !== undefined && (n < min || n > max)) return t('validation.range', { min, max })
    if (min !== undefined && n < min) return t('validation.min', { min })
    if (max !== undefined && n > max) return t('validation.max', { max })
    return undefined
  }

/** Rejects a value that is already in use (compared case-sensitively after trimming). */
export const unique =
  (taken: Iterable<string>): Rule =>
  (value) => {
    const text = value.trim()
    for (const other of taken) if (other === text) return t('validation.duplicate')
    return undefined
  }

export function checkField(value: string, rules: Rule[]): string | undefined {
  for (const rule of rules) {
    const message = rule(value)
    if (message) return message
  }
  return undefined
}

/** Runs the rules for every field and returns the messages of the fields that failed. */
export function validate<K extends string>(values: Record<K, string>, schema: Partial<Record<K, Rule[]>>): FieldErrors<K> {
  const errors: FieldErrors<K> = {}
  for (const field of Object.keys(schema) as K[]) {
    const message = checkField(values[field] ?? '', schema[field] ?? [])
    if (message) errors[field] = message
  }
  return errors
}

export function hasErrors(errors: Record<string, string | undefined>): boolean {
  return Object.values(errors).some((message) => !!message)
}

/** Parses a field that already passed integer(); returns the fallback for an empty field. */
export function toInt(value: string, fallback = 0): number {
  const text = value.trim()
  return text === '' ? fallback : Number.parseInt(text, 10)
}
