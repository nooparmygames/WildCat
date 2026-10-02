import en from './en.json'

// A deliberately small translation helper. Every string a person reads lives in a JSON catalog
// under src/i18n; English is the default and the fallback for any key another catalog lacks.
// To add a language, add its JSON file with the same keys and register it in `catalogs`.

export interface Catalog {
  [key: string]: string | Catalog
}

export type Params = Record<string, string | number>

type Join<Prefix extends string, Key extends string> = Prefix extends '' ? Key : `${Prefix}.${Key}`

/** Every dotted path to a string in a catalog, for example "games.title". */
export type KeyPaths<T, Prefix extends string = ''> = {
  [K in keyof T & string]: T[K] extends string ? Join<Prefix, K> : KeyPaths<T[K], Join<Prefix, K>>
}[keyof T & string]

export type MessageKey = KeyPaths<typeof en>

const catalogs: Record<string, Catalog> = { en }

export const defaultLocale = 'en'
let current: Catalog = en
let currentLocale = defaultLocale

/** Looks up a dotted key in a catalog. Returns undefined when the key is missing or not a string. */
export function lookup(catalog: Catalog, key: string): string | undefined {
  let node: string | Catalog | undefined = catalog
  for (const part of key.split('.')) {
    if (node === undefined || typeof node === 'string') return undefined
    node = node[part]
  }
  return typeof node === 'string' ? node : undefined
}

/** Replaces {name} placeholders. Placeholders without a value are left as they are. */
export function interpolate(template: string, params?: Params): string {
  if (!params) return template
  return template.replace(/\{(\w+)\}/g, (match, name: string) =>
    Object.prototype.hasOwnProperty.call(params, name) ? String(params[name]) : match,
  )
}

/** Translates a key with the given catalog, falling back to English and then to the key itself. */
export function translate(catalog: Catalog, key: string, params?: Params): string {
  const template = lookup(catalog, key) ?? lookup(en, key) ?? key
  return interpolate(template, params)
}

export function t(key: MessageKey, params?: Params): string {
  return translate(current, key, params)
}

export function availableLocales(): string[] {
  return Object.keys(catalogs)
}

export function getLocale(): string {
  return currentLocale
}

/** Switches the catalog used by t(). Unknown locales fall back to English. */
export function setLocale(locale: string): void {
  const catalog = catalogs[locale]
  currentLocale = catalog ? locale : defaultLocale
  current = catalog ?? en
  if (typeof document !== 'undefined') {
    document.documentElement.lang = currentLocale
  }
}

/** Registers a catalog at run time; mainly useful for tests and future lazy-loaded languages. */
export function registerCatalog(locale: string, catalog: Catalog): void {
  catalogs[locale] = catalog
}
