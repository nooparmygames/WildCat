import type { ReactNode } from 'react'
import { t } from '../../i18n'

interface AuthCardProps {
  title: string
  intro?: ReactNode
  children: ReactNode
  footer?: ReactNode
}

export function AuthCard({ title, intro, children, footer }: AuthCardProps) {
  return (
    <section className="auth-card" aria-labelledby="auth-title">
      <title>{t('app.pageTitle', { page: title })}</title>
      <h1 id="auth-title">{title}</h1>
      {intro && <p className="muted">{intro}</p>}
      {children}
      {footer && <div className="auth-card-footer">{footer}</div>}
    </section>
  )
}

/** Location state the auth pages pass to the sign-in page. */
export interface LoginState {
  from?: string
  notice?: 'activated' | 'passwordReset'
  email?: string
}
