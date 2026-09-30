import { useCallback } from 'react'
import { Link, NavLink, Outlet, useOutletContext, useParams } from 'react-router'
import { api } from '../../api'
import type { Game } from '../../api/types'
import { CopyButton } from '../../components/CopyButton'
import { LoadError, Loading } from '../../components/LoadState'
import { t } from '../../i18n'
import { useLoader } from '../../lib/useLoader'
import { genreLabel } from '../games/GameDialog'

export interface GameContext {
  game: Game
}

/** For the tab pages under /games/:gameKey. */
export function useGame(): Game {
  return useOutletContext<GameContext>().game
}

const tabs = [
  { path: 'leaderboards', label: 'game.tabs.leaderboards' },
  { path: 'items', label: 'game.tabs.items' },
  { path: 'events', label: 'game.tabs.events' },
  { path: 'game-modes', label: 'game.tabs.gameModes' },
] as const

export function GamePage() {
  const { gameKey = '' } = useParams()
  const load = useCallback(() => api.games.get(gameKey), [gameKey])
  const { data: game, error, reload } = useLoader(load)

  if (error) return <LoadError error={error} onRetry={reload} pageLevel />
  if (!game) return <Loading />

  return (
    <>
      <title>{t('app.pageTitle', { page: game.name || game.key })}</title>
      <p className="breadcrumb">
        <Link to="/games">{t('game.back')}</Link>
      </p>
      <div className="page-header">
        <div className="page-header-text">
          <h1>{game.name || game.key}</h1>
          <p className="muted game-meta">
            {[genreLabel(game.genre), game.platform, game.studioName].filter(Boolean).join(' / ')}
          </p>
          <p className="game-key">
            <span className="muted">{t('game.keyLabel')}</span> <code className="key">{game.key}</code>{' '}
            <CopyButton value={game.key} what={t('game.keyLabel')} />
          </p>
        </div>
      </div>
      <nav className="tabs" aria-label={t('game.sections')}>
        {tabs.map((tab) => (
          <NavLink key={tab.path} to={tab.path} className="tab">
            {t(tab.label)}
          </NavLink>
        ))}
      </nav>
      <div className="tab-panel">
        <Outlet context={{ game } satisfies GameContext} />
      </div>
    </>
  )
}
