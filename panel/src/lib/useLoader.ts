import { useCallback, useEffect, useState } from 'react'

export interface LoaderState<T> {
  data: T | undefined
  error: unknown
  loading: boolean
  reload: () => void
}

interface Settled<T> {
  load: () => Promise<T>
  version: number
  data?: T
  error?: unknown
}

export interface LoaderOptions {
  /** Keep showing the last data while a different `load` runs, e.g. when paging. */
  keepPrevious?: boolean
}

/**
 * Runs `load` when it changes (wrap it in useCallback) and on reload(). While a reload is in
 * flight the previous data stays visible so lists do not flash.
 */
export function useLoader<T>(load: () => Promise<T>, { keepPrevious = false }: LoaderOptions = {}): LoaderState<T> {
  const [version, setVersion] = useState(0)
  const [settled, setSettled] = useState<Settled<T> | null>(null)

  useEffect(() => {
    let active = true
    load().then(
      (data) => {
        if (active) setSettled({ load, version, data })
      },
      (error: unknown) => {
        if (active) setSettled((previous) => ({ load, version, data: previous?.load === load ? previous.data : undefined, error }))
      },
    )
    return () => {
      active = false
    }
  }, [load, version])

  const reload = useCallback(() => setVersion((v) => v + 1), [])
  const current = settled !== null && settled.load === load && settled.version === version
  const showData = settled !== null && (settled.load === load || keepPrevious)

  return {
    data: showData ? settled.data : undefined,
    error: current ? settled.error : undefined,
    loading: !current,
    reload,
  }
}
