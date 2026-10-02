import { afterEach, describe, expect, it } from 'vitest'
import en from './en.json'
import { getLocale, interpolate, lookup, registerCatalog, setLocale, t, translate, type Catalog } from './index'

afterEach(() => setLocale('en'))

describe('i18n', () => {
  it('looks up nested keys', () => {
    expect(t('games.title')).toBe('Games')
    expect(lookup(en, 'games.genres.puzzle')).toBe('Puzzle')
    expect(lookup(en, 'games.genres')).toBeUndefined()
    expect(lookup(en, 'games.title.more')).toBeUndefined()
  })

  it('fills placeholders and leaves unknown ones alone', () => {
    expect(t('common.pageOf', { page: 2, pages: 5 })).toBe('Page 2 of 5')
    expect(interpolate('Hello {name}, {missing}', { name: 'Ada' })).toBe('Hello Ada, {missing}')
    expect(interpolate('No params {here}')).toBe('No params {here}')
  })

  it('falls back to English for keys a catalog lacks, and to the key itself last', () => {
    const partial: Catalog = { games: { title: 'Spiele' } }
    expect(translate(partial, 'games.title')).toBe('Spiele')
    expect(translate(partial, 'games.new')).toBe('New game')
    expect(translate(partial, 'no.such.key')).toBe('no.such.key')
  })

  it('switches catalogs and falls back to English for unknown locales', () => {
    registerCatalog('xx', { nav: { games: 'Gamez' } })
    setLocale('xx')
    expect(getLocale()).toBe('xx')
    expect(t('nav.games')).toBe('Gamez')
    expect(t('nav.account')).toBe('Account')

    setLocale('does-not-exist')
    expect(getLocale()).toBe('en')
    expect(t('nav.games')).toBe('Games')
  })

  it('keeps every English string plain ASCII', () => {
    const text = JSON.stringify(en)
    expect([...text].every((c) => c.charCodeAt(0) < 128)).toBe(true)
  })
})
