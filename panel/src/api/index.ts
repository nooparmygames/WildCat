// Typed functions for every admin API endpoint the panel uses. Responses pass through small
// normalizers so a null string or a missing list from the server never reaches a form field.

import { currentToken } from '../auth/sessionStore'
import { formatTimeSpan, parseTimeSpan } from '../lib/timespan'
import { createApiClient } from './client'
import {
  ageRatingKeys,
  genreKeys,
  parameterTypes,
  regionKeys,
  type Developer,
  type DeveloperSummary,
  type Game,
  type GameInput,
  type GameMode,
  type GameModeInput,
  type Item,
  type ItemInput,
  type Leaderboard,
  type LoginResult,
  type MatchCriterion,
  type PagedResult,
  type PublisherDataEntry,
  type ScriptEvent,
  type ScriptEventInput,
} from './types'

export { ApiError, isApiError } from './client'

let unauthorizedHandler: ((token: string) => void) | null = null

/** The auth provider registers itself here so any 401 on an authenticated call signs the user out. */
export function setUnauthorizedHandler(handler: ((token: string) => void) | null): void {
  unauthorizedHandler = handler
}

export const apiBaseUrl: string = import.meta.env.VITE_API_BASE || '/api/v1'

export const client = createApiClient({
  baseUrl: apiBaseUrl,
  getToken: currentToken,
  onUnauthorized: (token) => unauthorizedHandler?.(token),
})

// ---- normalizers ----------------------------------------------------------------------------

type Raw = Record<string, unknown>

const asRecord = (value: unknown): Raw => (value && typeof value === 'object' ? (value as Raw) : {})
const asList = (value: unknown): unknown[] => (Array.isArray(value) ? value : [])
const str = (value: unknown): string => (typeof value === 'string' ? value : value == null ? '' : String(value))
const bool = (value: unknown): boolean => value === true || value === 'true'

function num(value: unknown, fallback = 0): number {
  if (typeof value === 'number' && Number.isFinite(value)) return value
  if (typeof value === 'string' && value.trim() !== '' && Number.isFinite(Number(value))) return Number(value)
  return fallback
}

/** Enum values normally arrive as numbers; string names (C# or panel spelling) are accepted too. */
function enumIndex(value: unknown, names: readonly string[], aliases: Record<string, number> = {}): number {
  if (typeof value === 'number') return value
  if (typeof value === 'string') {
    const lower = value.trim().toLowerCase()
    if (/^\d+$/.test(lower)) return Number(lower)
    const index = names.findIndex((name) => name.toLowerCase() === lower)
    if (index >= 0) return index
    if (lower in aliases) return aliases[lower] ?? 0
  }
  return 0
}

export function toDeveloper(value: unknown): Developer {
  const raw = asRecord(value)
  return {
    key: str(raw.key),
    email: str(raw.email),
    firstName: str(raw.firstName),
    lastName: str(raw.lastName),
    isAdmin: bool(raw.isAdmin) || raw.role === 'admin',
    secret: str(raw.secret),
  }
}

export function toGame(value: unknown): Game {
  const raw = asRecord(value)
  return {
    key: str(raw.key),
    name: str(raw.name),
    description: str(raw.description),
    studioName: str(raw.studioName),
    website: str(raw.website),
    genre: enumIndex(raw.genre, genreKeys),
    region: enumIndex(raw.region, regionKeys, { iran: 0, oversea: 1, foreign: 1 }),
    platform: str(raw.platform),
    ageRating: enumIndex(raw.ageRating, ageRatingKeys, { kids: 1, mature: 3 }),
  }
}

export function toLeaderboard(value: unknown): Leaderboard {
  const raw = asRecord(value)
  return { name: str(raw.name), key: str(raw.key), bestScore: bool(raw.bestScore) }
}

function toMetaData(value: unknown): Record<string, string> {
  const result: Record<string, string> = {}
  if (Array.isArray(value)) {
    for (const entry of value) {
      const raw = asRecord(entry)
      const name = str(raw.name ?? raw.key)
      if (name) result[name] = str(raw.value)
    }
  } else {
    for (const [name, entry] of Object.entries(asRecord(value))) result[name] = str(entry)
  }
  return result
}

export function toItem(value: unknown): Item {
  const raw = asRecord(value)
  return {
    name: str(raw.name),
    description: str(raw.description),
    effectTime: parseTimeSpan(raw.effectTime),
    stackable: bool(raw.stackable),
    maxAllowedToHave: num(raw.maxAllowedToHave),
    isConsumable: bool(raw.isConsumable),
    automaticallyConsumed: bool(raw.automaticallyConsumed),
    isRegenerating: bool(raw.isRegenerating),
    regenerationAmount: num(raw.regenerationAmount),
    regenerationPeriod: parseTimeSpan(raw.regenerationPeriod),
    metaData: toMetaData(raw.metaData),
    price: asList(raw.price).map((entry) => {
      const p = asRecord(entry)
      return { item: str(p.item), count: num(p.count) }
    }),
  }
}

const parameterTypeNames = parameterTypes.map((name) => name.toLowerCase())

export function toScriptEvent(value: unknown): ScriptEvent {
  const raw = asRecord(value)
  return {
    name: str(raw.name),
    script: str(raw.script),
    parameters: asList(raw.parameters).map((entry) => {
      const p = asRecord(entry)
      const type = typeof p.type === 'string' ? p.type.replace(/Param$/i, '') : p.type
      return { name: str(p.name), type: enumIndex(type, parameterTypeNames) }
    }),
  }
}

function toCriterion(value: unknown): MatchCriterion {
  const raw = asRecord(value)
  return {
    isMinPlayersEnough: bool(raw.isMinPlayersEnough),
    acceptableThreshold: num(raw.acceptableThreshold),
    startTimeInSeconds: num(raw.startTimeInSeconds),
  }
}

export function toGameMode(value: unknown): GameMode {
  const raw = asRecord(value)
  return {
    name: str(raw.name),
    minPlayers: num(raw.minPlayers, 2),
    maxPlayers: num(raw.maxPlayers, 2),
    criteria: asList(raw.criteria ?? raw.criterias).map(toCriterion),
  }
}

function toPublisherData(value: unknown): PublisherDataEntry {
  const raw = asRecord(value)
  return { key: str(raw.key), value: str(raw.value) }
}

function toDeveloperSummary(value: unknown): DeveloperSummary {
  const raw = asRecord(value)
  return {
    key: str(raw.key),
    email: str(raw.email),
    firstName: str(raw.firstName),
    lastName: str(raw.lastName),
    isActivated: bool(raw.isActivated),
  }
}

/** Reads the exp claim of a JWT, used only when the login response has no expiresAt. */
export function tokenExpiry(token: string): string {
  try {
    const payload = token.split('.')[1]
    if (!payload) return ''
    const json = atob(payload.replace(/-/g, '+').replace(/_/g, '/'))
    const exp = num(asRecord(JSON.parse(json)).exp, NaN)
    return Number.isFinite(exp) ? new Date(exp * 1000).toISOString() : ''
  } catch {
    return ''
  }
}

/** The new secret may come back as a bare string or as { secret }. */
function pickString(value: unknown, field: string): string {
  return typeof value === 'string' ? value : str(asRecord(value)[field])
}

const seg = encodeURIComponent

// ---- endpoints -------------------------------------------------------------------------------

export const api = {
  auth: {
    register: (email: string, password: string) =>
      client.post<void>('/auth/register', { email, password }, { auth: false }),
    activate: (email: string, code: string) => client.post<void>('/auth/activate', { email, code }, { auth: false }),
    async login(email: string, password: string): Promise<LoginResult> {
      const raw = asRecord(await client.post<unknown>('/auth/login', { email, password }, { auth: false }))
      const token = str(raw.token)
      return { token, expiresAt: str(raw.expiresAt) || tokenExpiry(token) }
    },
    requestPasswordReset: (email: string) =>
      client.post<void>('/auth/request-password-reset', { email }, { auth: false }),
    resetPassword: (email: string, code: string, newPassword: string) =>
      client.post<void>('/auth/reset-password', { email, code, newPassword }, { auth: false }),
    changePassword: (oldPassword: string, newPassword: string) =>
      client.post<void>('/auth/change-password', { oldPassword, newPassword }),
  },

  me: {
    get: async (): Promise<Developer> => toDeveloper(await client.get<unknown>('/me')),
    update: (firstName: string, lastName: string) => client.put<void>('/me', { firstName, lastName }),
    regenerateSecret: async (): Promise<string> => pickString(await client.post<unknown>('/me/secret'), 'secret'),
  },

  games: {
    list: async (): Promise<Game[]> => asList(await client.get<unknown>('/games')).map(toGame),
    get: async (gameKey: string): Promise<Game> => toGame(await client.get<unknown>(`/games/${seg(gameKey)}`)),
    create: async (game: GameInput): Promise<string> => pickString(await client.post<unknown>('/games', game), 'key'),
    update: (gameKey: string, game: GameInput) => client.put<void>(`/games/${seg(gameKey)}`, game),
    remove: (gameKey: string) => client.delete<void>(`/games/${seg(gameKey)}`),
  },

  leaderboards: {
    list: async (gameKey: string): Promise<Leaderboard[]> =>
      asList(await client.get<unknown>(`/games/${seg(gameKey)}/leaderboards`)).map(toLeaderboard),
    create: (gameKey: string, name: string, bestScore: boolean) =>
      client.post<void>(`/games/${seg(gameKey)}/leaderboards`, { name, bestScore }),
    remove: (gameKey: string, name: string) =>
      client.delete<void>(`/games/${seg(gameKey)}/leaderboards/${seg(name)}`),
  },

  items: {
    list: async (gameKey: string): Promise<Item[]> =>
      asList(await client.get<unknown>(`/games/${seg(gameKey)}/items`)).map(toItem),
    save: (gameKey: string, name: string, item: ItemInput) =>
      client.put<void>(`/games/${seg(gameKey)}/items/${seg(name)}`, {
        ...item,
        effectTime: formatTimeSpan(item.effectTime),
        regenerationPeriod: formatTimeSpan(item.regenerationPeriod),
      }),
    remove: (gameKey: string, name: string) => client.delete<void>(`/games/${seg(gameKey)}/items/${seg(name)}`),
  },

  events: {
    list: async (gameKey: string): Promise<ScriptEvent[]> =>
      asList(await client.get<unknown>(`/games/${seg(gameKey)}/events`)).map(toScriptEvent),
    save: (gameKey: string, name: string, event: ScriptEventInput) =>
      client.put<void>(`/games/${seg(gameKey)}/events/${seg(name)}`, event),
    remove: (gameKey: string, name: string) => client.delete<void>(`/games/${seg(gameKey)}/events/${seg(name)}`),
  },

  gameModes: {
    list: async (gameKey: string): Promise<GameMode[]> =>
      asList(await client.get<unknown>(`/games/${seg(gameKey)}/game-modes`)).map(toGameMode),
    save: (gameKey: string, name: string, mode: GameModeInput) =>
      client.put<void>(`/games/${seg(gameKey)}/game-modes/${seg(name)}`, mode),
    remove: (gameKey: string, name: string) =>
      client.delete<void>(`/games/${seg(gameKey)}/game-modes/${seg(name)}`),
  },

  publisherData: {
    list: async (): Promise<PublisherDataEntry[]> =>
      asList(await client.get<unknown>('/publisher-data')).map(toPublisherData),
    save: (key: string, value: string) => client.put<void>(`/publisher-data/${seg(key)}`, { value }),
    remove: (key: string) => client.delete<void>(`/publisher-data/${seg(key)}`),
  },

  admin: {
    async developers(query: { search?: string; page: number; pageSize: number }): Promise<PagedResult<DeveloperSummary>> {
      const raw = asRecord(await client.get<unknown>('/admin/developers', { query }))
      return { items: asList(raw.items).map(toDeveloperSummary), total: num(raw.total) }
    },
  },
}
