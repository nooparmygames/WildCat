import { useEffect, useState } from 'react'
import { t } from '../i18n'

async function copyText(text: string): Promise<boolean> {
  try {
    if (navigator.clipboard && window.isSecureContext) {
      await navigator.clipboard.writeText(text)
      return true
    }
  } catch {
    // Fall through to the older approach below.
  }
  // Plain http (a LAN address, for example) has no async clipboard API.
  const area = document.createElement('textarea')
  area.value = text
  area.setAttribute('readonly', '')
  area.style.position = 'fixed'
  area.style.opacity = '0'
  document.body.appendChild(area)
  area.select()
  let ok: boolean
  try {
    ok = document.execCommand('copy')
  } catch {
    ok = false
  }
  area.remove()
  return ok
}

interface CopyButtonProps {
  value: string
  /** What is being copied, for the accessible name, e.g. "game key". */
  what: string
}

export function CopyButton({ value, what }: CopyButtonProps) {
  const [state, setState] = useState<'idle' | 'copied' | 'failed'>('idle')

  useEffect(() => {
    if (state === 'idle') return
    const timer = window.setTimeout(() => setState('idle'), 2000)
    return () => window.clearTimeout(timer)
  }, [state])

  return (
    <>
      <button
        type="button"
        className="btn btn-small"
        aria-label={t('common.copyWhat', { what })}
        onClick={async () => setState((await copyText(value)) ? 'copied' : 'failed')}
        disabled={!value}
      >
        {state === 'copied' ? t('common.copied') : state === 'failed' ? t('common.copyFailedShort') : t('common.copy')}
      </button>
      <span className="visually-hidden" role="status">
        {state === 'copied' ? t('common.copiedWhat', { what }) : state === 'failed' ? t('common.copyFailed') : ''}
      </span>
    </>
  )
}
