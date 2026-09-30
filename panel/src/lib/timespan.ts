// .NET TimeSpan values travel as strings in the constant ("c") format that System.Text.Json uses:
// [-][d.]hh:mm:ss[.fffffff]. The panel works in whole seconds.

const TIMESPAN = /^(-)?(?:(\d+)\.)?(\d{1,2}):(\d{1,2})(?::(\d{1,2})(?:\.(\d+))?)?$/

/** Parses a TimeSpan string (or a number of seconds) into whole seconds. Returns 0 for anything else. */
export function parseTimeSpan(value: unknown): number {
  if (typeof value === 'number' && Number.isFinite(value)) return Math.round(value)
  if (typeof value !== 'string') return 0
  const match = TIMESPAN.exec(value.trim())
  if (!match) return 0
  const [, sign, days, hours, minutes, seconds, fraction] = match
  const total =
    Number(days ?? 0) * 86400 +
    Number(hours) * 3600 +
    Number(minutes) * 60 +
    Number(seconds ?? 0) +
    (fraction ? Number(`0.${fraction}`) : 0)
  return Math.round(sign ? -total : total)
}

const pad = (n: number) => String(n).padStart(2, '0')

/** Formats whole seconds as a TimeSpan string, for example 90061 -> "1.01:01:01". */
export function formatTimeSpan(totalSeconds: number): string {
  const sign = totalSeconds < 0 ? '-' : ''
  let rest = Math.abs(Math.round(totalSeconds))
  const days = Math.floor(rest / 86400)
  rest -= days * 86400
  const hours = Math.floor(rest / 3600)
  rest -= hours * 3600
  const minutes = Math.floor(rest / 60)
  const seconds = rest - minutes * 60
  return `${sign}${days > 0 ? `${days}.` : ''}${pad(hours)}:${pad(minutes)}:${pad(seconds)}`
}

export type DurationUnit = 'seconds' | 'minutes' | 'hours' | 'days'

export const durationUnits: Record<DurationUnit, number> = {
  seconds: 1,
  minutes: 60,
  hours: 3600,
  days: 86400,
}

/** Picks the largest unit that represents the value exactly, for showing it in a number field. */
export function splitDuration(totalSeconds: number): { amount: number; unit: DurationUnit } {
  if (totalSeconds === 0) return { amount: 0, unit: 'minutes' }
  for (const unit of ['days', 'hours', 'minutes'] as const) {
    if (totalSeconds % durationUnits[unit] === 0) return { amount: totalSeconds / durationUnits[unit], unit }
  }
  return { amount: totalSeconds, unit: 'seconds' }
}
