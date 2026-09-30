// Shapes of the admin API (/api/v1). Enums travel as numbers, matching the C# enum order in
// backend/WildCat.Interfaces. TimeSpan fields travel as "d.hh:mm:ss" strings.

export interface Developer {
  key: string
  email: string
  firstName: string
  lastName: string
  isAdmin: boolean
  secret: string
}

export interface LoginResult {
  token: string
  expiresAt: string
}

/** GameInfo.GameGenre: Action, Arcade, Strategy, Puzzle, Casual, Sports. */
export const genreKeys = ['action', 'arcade', 'strategy', 'puzzle', 'casual', 'sports'] as const
/** GameInfo.GameRegion: domestic (0) and international (1). */
export const regionKeys = ['domestic', 'international'] as const
/** GameInfo.GameAgeRating: everyone, children, teens, adults. */
export const ageRatingKeys = ['everyone', 'children', 'teens', 'adults'] as const

export interface GameInput {
  name: string
  description: string
  studioName: string
  website: string
  genre: number
  region: number
  platform: string
  ageRating: number
}

export interface Game extends GameInput {
  key: string
}

export interface Leaderboard {
  name: string
  key: string
  /** True keeps each player's best score, false keeps the latest one. */
  bestScore: boolean
}

export interface ItemPrice {
  /** Name of the item paid with. */
  item: string
  count: number
}

export interface ItemInput {
  description: string
  /** Seconds; sent as a TimeSpan string. */
  effectTime: number
  stackable: boolean
  maxAllowedToHave: number
  isConsumable: boolean
  automaticallyConsumed: boolean
  isRegenerating: boolean
  regenerationAmount: number
  /** Seconds; sent as a TimeSpan string. */
  regenerationPeriod: number
  metaData: Record<string, string>
  price: ItemPrice[]
}

export interface Item extends ItemInput {
  name: string
}

/** EventParameter.ParamType, in enum order. */
export const parameterTypes = ['Bool', 'Byte', 'Short', 'Int', 'Long', 'Float', 'Double', 'String', 'ByteArray'] as const
export type ParameterTypeName = (typeof parameterTypes)[number]

/** The C# type a script sees for each parameter type. */
export const parameterCSharpTypes: Record<ParameterTypeName, string> = {
  Bool: 'bool',
  Byte: 'byte',
  Short: 'short',
  Int: 'int',
  Long: 'long',
  Float: 'float',
  Double: 'double',
  String: 'string',
  ByteArray: 'byte[]',
}

export interface EventParameter {
  name: string
  /** Index into parameterTypes. */
  type: number
}

export interface ScriptEventInput {
  parameters: EventParameter[]
  script: string
}

export interface ScriptEvent extends ScriptEventInput {
  name: string
}

export interface MatchCriterion {
  isMinPlayersEnough: boolean
  acceptableThreshold: number
  startTimeInSeconds: number
}

export interface GameModeInput {
  minPlayers: number
  maxPlayers: number
  criteria: MatchCriterion[]
}

export interface GameMode extends GameModeInput {
  name: string
}

export interface PublisherDataEntry {
  key: string
  value: string
}

export interface DeveloperSummary {
  key: string
  email: string
  firstName: string
  lastName: string
  isActivated: boolean
}

export interface PagedResult<T> {
  items: T[]
  total: number
}
