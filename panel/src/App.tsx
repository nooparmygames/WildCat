import { lazy, Suspense } from 'react'
import { createBrowserRouter, Navigate, useRouteError } from 'react-router'
import { RouterProvider } from 'react-router/dom'
import { GuestOnly, RequireAdmin, RequireAuth } from './auth/guards'
import { AppShell, AuthLayout } from './components/AppShell'
import { Loading, NotFound } from './components/LoadState'
import { t } from './i18n'
import { AccountPage } from './pages/AccountPage'
import { AdminDevelopersPage } from './pages/AdminDevelopersPage'
import { ActivatePage } from './pages/auth/ActivatePage'
import { ForgotPasswordPage } from './pages/auth/ForgotPasswordPage'
import { LoginPage } from './pages/auth/LoginPage'
import { RegisterPage } from './pages/auth/RegisterPage'
import { ResetPasswordPage } from './pages/auth/ResetPasswordPage'
import { EventsTab } from './pages/game/EventsTab'
import { GameModesTab } from './pages/game/GameModesTab'
import { GamePage } from './pages/game/GamePage'
import { ItemsTab } from './pages/game/ItemsTab'
import { LeaderboardsTab } from './pages/game/LeaderboardsTab'
import { GamesPage } from './pages/games/GamesPage'
import { PublisherDataPage } from './pages/PublisherDataPage'

// The script editor pulls in CodeMirror, so it loads only when someone opens it.
const EventEditorPage = lazy(() => import('./pages/game/EventEditorPage').then((m) => ({ default: m.EventEditorPage })))

function RouteError() {
  const error = useRouteError()
  console.error(error)
  return (
    <main id="main" className="centered-page">
      <section className="empty-page" aria-labelledby="crash-title">
        <h1 id="crash-title">{t('crash.title')}</h1>
        <p>{t('crash.body')}</p>
        <a className="btn" href={import.meta.env.BASE_URL}>
          {t('crash.reload')}
        </a>
      </section>
    </main>
  )
}

// The panel is served under /panel/ (Vite's base), so the router works below that path.
const basename = import.meta.env.BASE_URL.replace(/\/+$/, '') || '/'

const router = createBrowserRouter(
  [
    {
      errorElement: <RouteError />,
      children: [
        { index: true, element: <Navigate to="/games" replace /> },
        {
          element: <AuthLayout />,
          children: [
            {
              element: <GuestOnly />,
              children: [
                { path: 'login', element: <LoginPage /> },
                { path: 'register', element: <RegisterPage /> },
                { path: 'forgot-password', element: <ForgotPasswordPage /> },
              ],
            },
            { path: 'activate', element: <ActivatePage /> },
            { path: 'reset-password', element: <ResetPasswordPage /> },
          ],
        },
        {
          element: <RequireAuth />,
          children: [
            {
              element: <AppShell />,
              children: [
                { path: 'games', element: <GamesPage /> },
                {
                  path: 'games/:gameKey',
                  element: <GamePage />,
                  children: [
                    { index: true, element: <Navigate to="leaderboards" replace /> },
                    { path: 'leaderboards', element: <LeaderboardsTab /> },
                    { path: 'items', element: <ItemsTab /> },
                    { path: 'events', element: <EventsTab /> },
                    {
                      path: 'events/new',
                      element: (
                        <Suspense fallback={<Loading />}>
                          <EventEditorPage />
                        </Suspense>
                      ),
                    },
                    {
                      path: 'events/:eventName',
                      element: (
                        <Suspense fallback={<Loading />}>
                          <EventEditorPage />
                        </Suspense>
                      ),
                    },
                    { path: 'game-modes', element: <GameModesTab /> },
                    { path: '*', element: <NotFound /> },
                  ],
                },
                { path: 'publisher-data', element: <PublisherDataPage /> },
                { path: 'account', element: <AccountPage /> },
                {
                  element: <RequireAdmin />,
                  children: [{ path: 'admin/developers', element: <AdminDevelopersPage /> }],
                },
                { path: '*', element: <NotFound /> },
              ],
            },
          ],
        },
      ],
    },
  ],
  { basename },
)

export function App() {
  return <RouterProvider router={router} />
}
