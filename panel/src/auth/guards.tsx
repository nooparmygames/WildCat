import { Navigate, Outlet, useLocation } from 'react-router'
import { LoadError, Loading, NoPermission } from '../components/LoadState'
import { t } from '../i18n'
import { useAuth } from './AuthContext'

export interface FromState {
  from?: string
}

/** Routes for signed-in developers. Without a session it sends the visitor to the sign-in page. */
export function RequireAuth() {
  const { session, developer, profileError, reloadProfile, signOut } = useAuth()
  const location = useLocation()

  if (!session) {
    const from = location.pathname + location.search
    return <Navigate to="/login" replace state={{ from } satisfies FromState} />
  }
  if (profileError) {
    return (
      <main id="main" className="centered-page">
        <LoadError error={profileError} onRetry={reloadProfile} />
        <button type="button" className="btn btn-ghost" onClick={() => signOut()}>
          {t('nav.signOut')}
        </button>
      </main>
    )
  }
  if (!developer) {
    return (
      <main id="main" className="centered-page">
        <Loading />
      </main>
    )
  }
  return <Outlet />
}

/** Admin-only routes. The server enforces this too; the guard just avoids a page of errors. */
export function RequireAdmin() {
  const { developer } = useAuth()
  return developer?.isAdmin ? <Outlet /> : <NoPermission />
}

/** Sign-in and registration pages: a signed-in developer goes straight to the games list. */
export function GuestOnly() {
  const { session } = useAuth()
  const location = useLocation()
  if (session) {
    const from = (location.state as FromState | null)?.from
    const safe = from && from.startsWith('/') && !from.startsWith('//') ? from : '/games'
    return <Navigate to={safe} replace />
  }
  return <Outlet />
}
