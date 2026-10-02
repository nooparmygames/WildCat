import { describe, expect, it } from 'vitest'
import { toDeveloper, toGame, toGameMode, toItem, toScriptEvent, tokenExpiry } from './index'

describe('response normalizers', () => {
  it('fills missing strings and reads enum names as well as numbers', () => {
    expect(toGame({ key: 'g1', name: 'Space Race', description: null, genre: 3, region: 'Oversea', ageRating: 'Teens' })).toEqual({
      key: 'g1',
      name: 'Space Race',
      description: '',
      studioName: '',
      website: '',
      genre: 3,
      region: 1,
      platform: '',
      ageRating: 2,
    })
  })

  it('reads parameter types by index or by C# enum name', () => {
    const event = toScriptEvent({
      name: 'OnWin',
      script: null,
      parameters: [
        { name: 'level', type: 3 },
        { name: 'mode', type: 'StringParam' },
        { name: 'data', type: 'ByteArray' },
      ],
    })
    expect(event.script).toBe('')
    expect(event.parameters.map((p) => p.type)).toEqual([3, 7, 8])
  })

  it('reads TimeSpan strings and metadata in either shape', () => {
    const item = toItem({
      name: 'Energy',
      effectTime: '00:30:00',
      regenerationPeriod: '1.00:00:00',
      metaData: [{ name: 'color', value: 'blue' }],
      price: [{ item: 'Gold', count: '5' }],
    })
    expect(item.effectTime).toBe(1800)
    expect(item.regenerationPeriod).toBe(86400)
    expect(item.metaData).toEqual({ color: 'blue' })
    expect(item.price).toEqual([{ item: 'Gold', count: 5 }])
    expect(toItem({ metaData: { a: 'b' } }).metaData).toEqual({ a: 'b' })
  })

  it('accepts the legacy criterias spelling for game modes', () => {
    const mode = toGameMode({ name: 'Duel', minPlayers: 2, maxPlayers: 2, criterias: [{ startTimeInSeconds: 0, acceptableThreshold: 50 }] })
    expect(mode.criteria).toEqual([{ startTimeInSeconds: 0, acceptableThreshold: 50, isMinPlayersEnough: false }])
  })

  it('treats the admin role as isAdmin', () => {
    expect(toDeveloper({ email: 'a@example.com', role: 'admin' }).isAdmin).toBe(true)
    expect(toDeveloper({ email: 'a@example.com', isAdmin: false }).isAdmin).toBe(false)
  })

  it('reads the expiry from a JWT when the login response has none', () => {
    const payload = btoa(JSON.stringify({ sub: 'x', exp: 1893456000 })).replace(/=+$/, '')
    expect(tokenExpiry(`header.${payload}.signature`)).toBe('2030-01-01T00:00:00.000Z')
    expect(tokenExpiry('not-a-jwt')).toBe('')
  })
})
