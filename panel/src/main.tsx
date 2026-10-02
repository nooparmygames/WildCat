import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { App } from './App'
import { AuthProvider } from './auth/AuthContext'
import { defaultLocale, setLocale } from './i18n'
import './styles.css'

setLocale(defaultLocale)

const root = document.getElementById('root')
if (!root) throw new Error('The page has no #root element')

createRoot(root).render(
  <StrictMode>
    <AuthProvider>
      <App />
    </AuthProvider>
  </StrictMode>,
)
