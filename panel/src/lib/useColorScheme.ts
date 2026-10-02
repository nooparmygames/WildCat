import { useSyncExternalStore } from 'react'

const query = '(prefers-color-scheme: dark)'

function subscribe(onChange: () => void): () => void {
  if (typeof window === 'undefined' || !window.matchMedia) return () => {}
  const media = window.matchMedia(query)
  media.addEventListener('change', onChange)
  return () => media.removeEventListener('change', onChange)
}

function snapshot(): 'light' | 'dark' {
  if (typeof window === 'undefined' || !window.matchMedia) return 'light'
  return window.matchMedia(query).matches ? 'dark' : 'light'
}

/** Follows the operating system's light or dark preference. */
export function useColorScheme(): 'light' | 'dark' {
  return useSyncExternalStore(subscribe, snapshot, () => 'light')
}
