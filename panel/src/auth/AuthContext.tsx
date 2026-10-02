import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { api, setUnauthorizedHandler } from '../api'
import type { Developer } from '../api/types'
import { clearSession, currentToken, readSession, writeSession, type StoredSession } from './sessionStore'

export type SignOutReason = 'expired' | 'signedOut'

interface Profile {
  session: StoredSession
  developer?: Developer
  error?: unknown
}

export interface AuthValue {
  session: StoredSession | null
  /** The signed-in developer, once /me has loaded. */
  developer: Developer | null
  profileError: unknown
  /** Why the last session ended, so the sign-in page can say so. */
  signOutReason: SignOutReason | null
  signIn(email: string, password: string): Promise<void>
  signOut(reason?: SignOutReason): void
  reloadProfile(): void
  updateDeveloper(changes: Partial<Developer>): void
}

const AuthContext = createContext<AuthValue | null>(null)

// The longest delay setTimeout accepts; later expiries are checked again when the page reloads.
const maxTimerDelay = 2 ** 31 - 1

export function AuthProvider({ children }: { children: ReactNode }) {
  const [session, setSession] = useState<StoredSession | null>(readSession)
  const [profile, setProfile] = useState<Profile | null>(null)
  const [profileVersion, setProfileVersion] = useState(0)
  const [signOutReason, setSignOutReason] = useState<SignOutReason | null>(null)

  const signOut = useCallback((reason: SignOutReason = 'signedOut') => {
    clearSession()
    setSession(null)
    setProfile(null)
    setSignOutReason(reason)
  }, [])

  // Any 401 on a request made with the current token ends the session.
  useEffect(() => {
    setUnauthorizedHandler((token) => {
      if (token === currentToken()) signOut('expired')
    })
    return () => setUnauthorizedHandler(null)
  }, [signOut])

  // Load the developer profile for the current session.
  useEffect(() => {
    if (!session) return
    let active = true
    api.me.get().then(
      (developer) => {
        if (active) setProfile({ session, developer })
      },
      (error: unknown) => {
        if (active) setProfile({ session, error })
      },
    )
    return () => {
      active = false
    }
  }, [session, profileVersion])

  // Sign out when the token expires.
  useEffect(() => {
    if (!session) return
    const remaining = Date.parse(session.expiresAt) - Date.now()
    if (!Number.isFinite(remaining) || remaining > maxTimerDelay) return
    const timer = window.setTimeout(() => signOut('expired'), Math.max(0, remaining))
    return () => window.clearTimeout(timer)
  }, [session, signOut])

  const signIn = useCallback(async (email: string, password: string) => {
    const result = await api.auth.login(email, password)
    writeSession(result)
    setSignOutReason(null)
    setSession(result)
  }, [])

  const reloadProfile = useCallback(() => {
    // Drop a failed profile so the guard shows the loading state while it retries.
    setProfile((current) => (current?.error ? null : current))
    setProfileVersion((v) => v + 1)
  }, [])

  const updateDeveloper = useCallback((changes: Partial<Developer>) => {
    setProfile((current) =>
      current?.developer ? { ...current, developer: { ...current.developer, ...changes } } : current,
    )
  }, [])

  const value = useMemo<AuthValue>(() => {
    const current = profile && profile.session === session ? profile : null
    return {
      session,
      developer: current?.developer ?? null,
      profileError: current?.error ?? null,
      signOutReason,
      signIn,
      signOut,
      reloadProfile,
      updateDeveloper,
    }
  }, [session, profile, signOutReason, signIn, signOut, reloadProfile, updateDeveloper])

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export function useAuth(): AuthValue {
  const value = useContext(AuthContext)
  if (!value) throw new Error('useAuth must be used inside AuthProvider')
  return value
}

/** For pages behind RequireAuth, where the profile is always loaded. */
export function useDeveloper(): Developer {
  const { developer } = useAuth()
  if (!developer) throw new Error('useDeveloper must be used behind RequireAuth')
  return developer
}
