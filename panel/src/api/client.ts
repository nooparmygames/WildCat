// A thin fetch wrapper for the WildCat admin API. It adds the bearer token, turns JSON bodies into
// values, and turns every failed response into an ApiError built from the RFC 7807 problem
// details the API returns. It knows nothing about React so it can be tested on its own.

export interface ProblemDetails {
  type?: string
  title?: string
  status?: number
  detail?: string
  instance?: string
  errors?: unknown
  [extension: string]: unknown
}

export class ApiError extends Error {
  /** HTTP status, or 0 when the server could not be reached. */
  readonly status: number
  readonly title?: string
  readonly detail?: string
  /** Every validation message from the `errors` extension, flattened. */
  readonly errors: string[]
  /** Validation messages by field when the server sent them keyed by field name. */
  readonly fieldErrors: Record<string, string[]>
  readonly problem?: ProblemDetails

  constructor(status: number, problem?: ProblemDetails, fallbackMessage?: string) {
    super(problem?.detail || problem?.title || fallbackMessage || `Request failed with status ${status}`)
    this.name = 'ApiError'
    this.status = status
    this.problem = problem
    this.title = problem?.title || undefined
    this.detail = problem?.detail || undefined
    const { list, byField } = normalizeErrors(problem?.errors)
    this.errors = list
    this.fieldErrors = byField
  }

  get isNetworkError(): boolean {
    return this.status === 0
  }
}

export function isApiError(value: unknown): value is ApiError {
  return value instanceof ApiError
}

/**
 * Accepts the shapes an `errors` extension can take: a list of strings, a list of objects with a
 * message, a map of field name to messages (ASP.NET validation problems), or a single string.
 */
export function normalizeErrors(errors: unknown): { list: string[]; byField: Record<string, string[]> } {
  const list: string[] = []
  const byField: Record<string, string[]> = {}
  const text = (value: unknown): string | undefined => {
    if (typeof value === 'string') return value
    if (value && typeof value === 'object') {
      const record = value as Record<string, unknown>
      for (const name of ['message', 'description', 'detail', 'errorMessage']) {
        if (typeof record[name] === 'string') return record[name]
      }
    }
    return undefined
  }

  if (typeof errors === 'string') {
    list.push(errors)
  } else if (Array.isArray(errors)) {
    for (const entry of errors) {
      const message = text(entry)
      if (message) list.push(message)
    }
  } else if (errors && typeof errors === 'object') {
    for (const [field, value] of Object.entries(errors as Record<string, unknown>)) {
      const messages = (Array.isArray(value) ? value : [value]).map(text).filter((m): m is string => !!m)
      if (messages.length > 0) {
        byField[field] = messages
        list.push(...messages)
      }
    }
  }
  return { list, byField }
}

export interface RequestOptions {
  body?: unknown
  query?: Record<string, string | number | undefined>
  /** Set to false for endpoints that must not carry the token, such as login. */
  auth?: boolean
  signal?: AbortSignal
}

export interface ApiClientOptions {
  baseUrl: string
  getToken: () => string | null
  /** Called with the rejected token when an authenticated request gets a 401 for its token. */
  onUnauthorized?: (token: string) => void
  fetch?: typeof fetch
}

export type HttpMethod = 'GET' | 'POST' | 'PUT' | 'DELETE'

export interface ApiClient {
  request<T>(method: HttpMethod, path: string, options?: RequestOptions): Promise<T>
  get<T>(path: string, options?: RequestOptions): Promise<T>
  post<T>(path: string, body?: unknown, options?: RequestOptions): Promise<T>
  put<T>(path: string, body?: unknown, options?: RequestOptions): Promise<T>
  delete<T>(path: string, options?: RequestOptions): Promise<T>
}

export function buildUrl(baseUrl: string, path: string, query?: RequestOptions['query']): string {
  const base = baseUrl.replace(/\/+$/, '')
  const suffix = path.startsWith('/') ? path : `/${path}`
  let url = base + suffix
  if (query) {
    const search = new URLSearchParams()
    for (const [name, value] of Object.entries(query)) {
      if (value !== undefined && value !== '') search.set(name, String(value))
    }
    const text = search.toString()
    if (text) url += `?${text}`
  }
  return url
}

async function readBody(response: Response): Promise<unknown> {
  if (response.status === 204 || response.status === 205) return undefined
  const text = await response.text()
  if (!text) return undefined
  const type = response.headers.get('content-type') ?? ''
  if (type.includes('json')) {
    try {
      return JSON.parse(text)
    } catch {
      return text
    }
  }
  return text
}

function toProblem(status: number, body: unknown): ProblemDetails | undefined {
  if (body && typeof body === 'object' && !Array.isArray(body)) {
    const record = body as ProblemDetails
    // Older endpoints and proxies sometimes send { message } instead of problem details.
    if (!record.detail && typeof record.message === 'string') {
      return { ...record, detail: record.message, status: record.status ?? status }
    }
    return { ...record, status: record.status ?? status }
  }
  if (typeof body === 'string' && body.trim() && !body.trimStart().startsWith('<')) {
    return { status, detail: body.trim() }
  }
  return undefined
}

/**
 * Tells a rejected token apart from an endpoint that answers 401 for its own reasons (a wrong
 * current password, say). The JWT bearer handler sends a WWW-Authenticate challenge and no
 * problem detail; an endpoint's own 401 carries a detail.
 */
export function isTokenRejection(response: Response, problem: ProblemDetails | undefined): boolean {
  return response.headers.has('www-authenticate') || !problem?.detail
}

export function createApiClient(options: ApiClientOptions): ApiClient {
  const doFetch = options.fetch ?? ((input: RequestInfo | URL, init?: RequestInit) => fetch(input, init))

  async function request<T>(method: HttpMethod, path: string, requestOptions: RequestOptions = {}): Promise<T> {
    const headers: Record<string, string> = { Accept: 'application/json, application/problem+json' }
    const token = requestOptions.auth === false ? null : options.getToken()
    if (token) headers.Authorization = `Bearer ${token}`
    let body: string | undefined
    if (requestOptions.body !== undefined) {
      headers['Content-Type'] = 'application/json'
      body = JSON.stringify(requestOptions.body)
    }

    let response: Response
    try {
      response = await doFetch(buildUrl(options.baseUrl, path, requestOptions.query), {
        method,
        headers,
        body,
        signal: requestOptions.signal,
      })
    } catch (error) {
      if (error instanceof DOMException && error.name === 'AbortError') throw error
      throw new ApiError(0, undefined, error instanceof Error ? error.message : 'Network error')
    }

    const payload = await readBody(response)
    if (!response.ok) {
      const problem = toProblem(response.status, payload)
      if (response.status === 401 && token && isTokenRejection(response, problem)) options.onUnauthorized?.(token)
      throw new ApiError(response.status, problem, response.statusText)
    }
    return payload as T
  }

  return {
    request,
    get: (path, requestOptions) => request('GET', path, requestOptions),
    post: (path, body, requestOptions) => request('POST', path, { ...requestOptions, body }),
    put: (path, body, requestOptions) => request('PUT', path, { ...requestOptions, body }),
    delete: (path, requestOptions) => request('DELETE', path, requestOptions),
  }
}
