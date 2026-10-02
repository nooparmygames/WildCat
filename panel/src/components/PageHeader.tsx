import type { ReactNode } from 'react'
import { t } from '../i18n'

interface PageHeaderProps {
  title: ReactNode
  intro?: ReactNode
  actions?: ReactNode
  /** Heading level; tabs inside a page use h2. */
  level?: 1 | 2
}

export function PageHeader({ title, intro, actions, level = 1 }: PageHeaderProps) {
  const Heading = level === 1 ? 'h1' : 'h2'
  return (
    <div className="page-header">
      {level === 1 && typeof title === 'string' && <title>{t('app.pageTitle', { page: title })}</title>}
      <div className="page-header-text">
        <Heading>{title}</Heading>
        {intro && <p className="muted">{intro}</p>}
      </div>
      {actions && <div className="page-header-actions">{actions}</div>}
    </div>
  )
}
