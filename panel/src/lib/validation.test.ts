import { describe, expect, it } from 'vitest'
import { checkField, email, hasErrors, identifier, integer, matches, minLength, required, unique, url, validate } from './validation'

describe('validation rules', () => {
  it('required rejects blank values', () => {
    expect(required()('')).toBe('Please fill this in.')
    expect(required()('   ')).toBe('Please fill this in.')
    expect(required()('x')).toBeUndefined()
  })

  it('email accepts ordinary addresses and leaves empty values to required()', () => {
    expect(email()('dev@example.com')).toBeUndefined()
    expect(email()('')).toBeUndefined()
    expect(email()('dev@example')).toBeDefined()
    expect(email()('not an email')).toBeDefined()
  })

  it('minLength and matches check passwords', () => {
    expect(minLength(8)('short')).toBe('Use at least 8 characters.')
    expect(minLength(8)('long enough')).toBeUndefined()
    expect(matches('secret-1')('secret-2')).toBeDefined()
    expect(matches('secret-1')('secret-1')).toBeUndefined()
  })

  it('integer checks whole numbers and ranges', () => {
    expect(integer()('12')).toBeUndefined()
    expect(integer()('1.5')).toBe('Enter a whole number.')
    expect(integer()('abc')).toBe('Enter a whole number.')
    expect(integer(2, 1000)('1')).toBe('Enter a whole number from 2 to 1000.')
    expect(integer(2, 1000)('1001')).toBeDefined()
    expect(integer(2, 1000)('500')).toBeUndefined()
    expect(integer(0)('-1')).toBe('Enter 0 or more.')
    expect(integer()('')).toBeUndefined()
  })

  it('identifier matches what C# accepts as a simple name', () => {
    expect(identifier()('OnLevelDone')).toBeUndefined()
    expect(identifier()('_private2')).toBeUndefined()
    expect(identifier()('2fast')).toBeDefined()
    expect(identifier()('has space')).toBeDefined()
  })

  it('url wants an http or https address', () => {
    expect(url()('https://example.com')).toBeUndefined()
    expect(url()('')).toBeUndefined()
    expect(url()('example.com')).toBeDefined()
    expect(url()('ftp://example.com')).toBeDefined()
  })

  it('unique compares trimmed values', () => {
    expect(unique(['Gold', 'Gems'])(' Gold ')).toBe('This name is already taken.')
    expect(unique(['Gold'])('gold')).toBeUndefined()
  })

  it('checkField returns the first failing rule', () => {
    expect(checkField('', [required(), email()])).toBe('Please fill this in.')
    expect(checkField('x', [required(), email()])).toBe('Enter an email address like name@example.com.')
  })

  it('validate collects messages per field', () => {
    const errors = validate(
      { email: 'dev@example.com', password: 'short', confirm: 'other' },
      { email: [required(), email()], password: [minLength(8)], confirm: [matches('short')] },
    )
    expect(errors).toEqual({ password: 'Use at least 8 characters.', confirm: 'The two passwords do not match.' })
    expect(hasErrors(errors)).toBe(true)
    expect(hasErrors({})).toBe(false)
  })
})
