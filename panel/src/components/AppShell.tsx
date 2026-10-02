import { useId, useState } from 'react'
import { NavLink, Outlet, useLocation } from 'react-router'
import { useAuth, useDeveloper } from '../auth/AuthContext'
import { t } from '../i18n'

function Brand() {
  return (
    <span className="brand">
      <svg className="brand-mark" viewBox="0 0 32 32" aria-hidden="true" focusable="false">
        <rect width="32" height="32" rx="7" />
        <path d="M7 9l3 14 3-9 3 9 3-9 3 9 3-14" />
      </svg>
      <span>{t('app.name')}</span>
    </span>
  )
}

/** The signed-in layout: a side bar on wide screens and a collapsible menu on phones. */
export function AppShell() {
  const developer = useDeveloper()
  const { signOut } = useAuth()
  const location = useLocation()
  const navId = useId()
  // The menu closes whenever the route changes; it remembers which page it was opened on.
  const [openedAt, setOpenedAt] = useState<string | null>(null)
  const menuOpen = openedAt === location.pathname

  const displayName = [developer.firstName, developer.lastName].filter(Boolean).join(' ') || developer.email

  return (
    <div className="shell">
      <a className="skip-link" href="#main">
        {t('app.skipToContent')}
      </a>
      <header className="sidebar">
        <div className="sidebar-top">
          <Brand />
          <button
            type="button"
            className="btn btn-ghost menu-toggle"
            aria-expanded={menuOpen}
            aria-controls={navId}
            onClick={() => setOpenedAt(menuOpen ? null : location.pathname)}
          >
            {menuOpen ? t('app.closeMenu') : t('app.menu')}
          </button>
        </div>
        <div id={navId} className={`sidebar-menu${menuOpen ? ' open' : ''}`}>
          <nav aria-label={t('app.mainNavigation')}>
            <ul className="nav-list">
              <li>
                <NavLink to="/games">{t('nav.games')}</NavLink>
              </li>
              <li>
                <NavLink to="/publisher-data">{t('nav.publisherData')}</NavLink>
              </li>
              {developer.isAdmin && (
                <li>
                  <NavLink to="/admin/developers">{t('nav.developers')}</NavLink>
                </li>
              )}
              <li>
                <NavLink to="/account">{t('nav.account')}</NavLink>
              </li>
            </ul>
          </nav>
          <div className="sidebar-user">
            <p className="sidebar-name" title={developer.email}>
              {displayName}
            </p>
            {developer.isAdmin && <p className="badge">{t('nav.adminBadge')}</p>}
            <button type="button" className="btn btn-small" onClick={() => signOut()}>
              {t('nav.signOut')}
            </button>
          </div>
        </div>
      </header>
      <main id="main" className="main" tabIndex={-1}>
        <Outlet />
      </main>
    </div>
  )
}

/** The layout for sign-in, registration and the other pages used without a session. */
export function AuthLayout() {
  return (
    <div className="auth-layout">
      <a className="skip-link" href="#main">
        {t('app.skipToContent')}
      </a>
      <header className="auth-brand">
        <Brand />
      </header>
      <main id="main" className="auth-main" tabIndex={-1}>
        <Outlet />
      </main>
      <footer className="auth-footer muted">{t('app.footer')}</footer>
    </div>
  )
}
