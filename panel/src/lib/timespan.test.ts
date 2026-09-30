import { describe, expect, it } from 'vitest'
import { formatTimeSpan, parseTimeSpan, splitDuration } from './timespan'

describe('TimeSpan helpers', () => {
  it('parses the .NET constant format', () => {
    expect(parseTimeSpan('00:00:00')).toBe(0)
    expect(parseTimeSpan('00:05:00')).toBe(300)
    expect(parseTimeSpan('1.02:03:04')).toBe(93784)
    expect(parseTimeSpan('00:00:01.5000000')).toBe(2)
    expect(parseTimeSpan('-00:01:00')).toBe(-60)
    expect(parseTimeSpan(90)).toBe(90)
    expect(parseTimeSpan('nonsense')).toBe(0)
    expect(parseTimeSpan(null)).toBe(0)
  })

  it('formats seconds the way System.Text.Json reads them', () => {
    expect(formatTimeSpan(0)).toBe('00:00:00')
    expect(formatTimeSpan(300)).toBe('00:05:00')
    expect(formatTimeSpan(93784)).toBe('1.02:03:04')
    expect(formatTimeSpan(-60)).toBe('-00:01:00')
  })

  it('round trips', () => {
    for (const seconds of [0, 59, 3600, 86399, 86400, 1234567]) {
      expect(parseTimeSpan(formatTimeSpan(seconds))).toBe(seconds)
    }
  })

  it('picks the largest exact unit for display', () => {
    expect(splitDuration(0)).toEqual({ amount: 0, unit: 'minutes' })
    expect(splitDuration(172800)).toEqual({ amount: 2, unit: 'days' })
    expect(splitDuration(5400)).toEqual({ amount: 90, unit: 'minutes' })
    expect(splitDuration(7200)).toEqual({ amount: 2, unit: 'hours' })
    expect(splitDuration(61)).toEqual({ amount: 61, unit: 'seconds' })
  })
})
