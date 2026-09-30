// The sign-in token lives in sessionStorage: it survives reloads of the tab but not closing it,
// and it is not shared with other tabs.

import type { LoginResult } from '../api/types'

const storageKey = 'wildcat.panel.session'

export type StoredSession = LoginResult

// Used only when sessionStorage is blocked, so signing in still works until the page reloads.
let memoryToken: string | null = null

function storage(): Storage | null {
  try {
    return typeof sessionStorage === 'undefined' ? null : sessionStorage
  } catch {
    return null
  }
}

export function isExpired(session: StoredSession, now = Date.now()): boolean {
  const expires = Date.parse(session.expiresAt)
  return Number.isFinite(expires) && expires <= now
}

export function readSession(): StoredSession | null {
  try {
    const raw = storage()?.getItem(storageKey)
    if (!raw) return null
    const value = JSON.parse(raw) as Partial<StoredSession>
    if (typeof value.token !== 'string' || !value.token) return null
    const session: StoredSession = { token: value.token, expiresAt: String(value.expiresAt ?? '') }
    if (isExpired(session)) {
      clearSession()
      return null
    }
    return session
  } catch {
    return null
  }
}

export function writeSession(session: StoredSession): void {
  try {
    storage()?.setItem(storageKey, JSON.stringify(session))
  } catch {
    // Storage can be full or blocked; the session then lasts until the page reloads.
  }
  memoryToken = session.token
}

export function clearSession(): void {
  try {
    storage()?.removeItem(storageKey)
  } catch {
    // Nothing to clean up when storage is blocked.
  }
  memoryToken = null
}

export function currentToken(): string | null {
  return readSession()?.token ?? memoryToken
}
