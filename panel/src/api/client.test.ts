import { describe, expect, it, vi } from 'vitest'
import { ApiError, buildUrl, createApiClient, normalizeErrors } from './client'

function jsonResponse(status: number, body: unknown, headers: Record<string, string> = {}): Response {
  const contentType = status >= 400 ? 'application/problem+json' : 'application/json'
  return new Response(JSON.stringify(body), { status, headers: { 'content-type': contentType, ...headers } })
}

function setup(respond: (url: string, init: RequestInit) => Response | Promise<Response>, token: string | null = 'token-1') {
  const calls: { url: string; init: RequestInit }[] = []
  const onUnauthorized = vi.fn()
  const client = createApiClient({
    baseUrl: '/api/v1/',
    getToken: () => token,
    onUnauthorized,
    fetch: async (input, init) => {
      const url = String(input)
      calls.push({ url, init: init ?? {} })
      return respond(url, init ?? {})
    },
  })
  return { client, calls, onUnauthorized }
}

async function failure(promise: Promise<unknown>): Promise<ApiError> {
  try {
    await promise
  } catch (error) {
    expect(error).toBeInstanceOf(ApiError)
    return error as ApiError
  }
  throw new Error('Expected the request to fail')
}

describe('api client', () => {
  it('sends JSON with the bearer token and parses the response', async () => {
    const { client, calls } = setup(() => jsonResponse(200, { key: 'abc' }))
    const result = await client.post<{ key: string }>('/games', { name: 'Space Race' })

    expect(result).toEqual({ key: 'abc' })
    expect(calls[0]?.url).toBe('/api/v1/games')
    const headers = calls[0]?.init.headers as Record<string, string>
    expect(headers.Authorization).toBe('Bearer token-1')
    expect(headers['Content-Type']).toBe('application/json')
    expect(calls[0]?.init.body).toBe('{"name":"Space Race"}')
  })

  it('leaves the token off when asked to', async () => {
    const { client, calls } = setup(() => jsonResponse(200, {}))
    await client.post('/auth/login', { email: 'dev@example.com' }, { auth: false })
    expect((calls[0]?.init.headers as Record<string, string>).Authorization).toBeUndefined()
  })

  it('returns undefined for 204 No Content', async () => {
    const { client } = setup(() => new Response(null, { status: 204 }))
    await expect(client.delete('/games/1')).resolves.toBeUndefined()
  })

  it('turns problem details into an ApiError with the detail as its message', async () => {
    const { client } = setup(() =>
      jsonResponse(409, { type: 'about:blank', title: 'Conflict', status: 409, detail: 'That email is already registered.' }),
    )
    const error = await failure(client.post('/auth/register', {}, { auth: false }))
    expect(error.status).toBe(409)
    expect(error.title).toBe('Conflict')
    expect(error.detail).toBe('That email is already registered.')
    expect(error.message).toBe('That email is already registered.')
  })

  it('collects the errors extension as a flat list', async () => {
    const { client } = setup(() =>
      jsonResponse(400, { title: 'The script is not valid', status: 400, errors: ['Script contains invalid type File'] }),
    )
    const error = await failure(client.put('/games/g/events/OnWin', {}))
    expect(error.errors).toEqual(['Script contains invalid type File'])
    expect(error.fieldErrors).toEqual({})
  })

  it('keeps validation errors keyed by field', async () => {
    const { client } = setup(() =>
      jsonResponse(400, { title: 'One or more validation errors occurred.', errors: { Name: ['The name is required.'] } }),
    )
    const error = await failure(client.post('/games', {}))
    expect(error.fieldErrors).toEqual({ Name: ['The name is required.'] })
    expect(error.errors).toEqual(['The name is required.'])
  })

  it('accepts a plain text error body', async () => {
    const { client } = setup(() => new Response('Game not found', { status: 404, headers: { 'content-type': 'text/plain' } }))
    const error = await failure(client.get('/games/x'))
    expect(error.status).toBe(404)
    expect(error.detail).toBe('Game not found')
  })

  it('ignores an HTML error page instead of showing it', async () => {
    const { client } = setup(() => new Response('<html>Bad gateway</html>', { status: 502, headers: { 'content-type': 'text/html' } }))
    const error = await failure(client.get('/games'))
    expect(error.status).toBe(502)
    expect(error.detail).toBeUndefined()
  })

  it('reports a network failure as status 0', async () => {
    const { client } = setup(() => {
      throw new TypeError('Failed to fetch')
    })
    const error = await failure(client.get('/games'))
    expect(error.status).toBe(0)
    expect(error.isNetworkError).toBe(true)
  })

  it('reports a rejected token so the panel can sign out', async () => {
    const { client, onUnauthorized } = setup(
      () => new Response(null, { status: 401, headers: { 'www-authenticate': 'Bearer error="invalid_token"' } }),
    )
    await failure(client.get('/games'))
    expect(onUnauthorized).toHaveBeenCalledWith('token-1')
  })

  it('does not sign out when an endpoint answers 401 with its own reason', async () => {
    const { client, onUnauthorized } = setup(() => jsonResponse(401, { status: 401, detail: 'The current password is not correct.' }))
    const error = await failure(client.post('/auth/change-password', {}))
    expect(error.detail).toBe('The current password is not correct.')
    expect(onUnauthorized).not.toHaveBeenCalled()
  })

  it('does not sign out on 401 from a request without a token', async () => {
    const { client, onUnauthorized } = setup(() => new Response(null, { status: 401 }), null)
    await failure(client.post('/auth/login', {}, { auth: false }))
    expect(onUnauthorized).not.toHaveBeenCalled()
  })
})

describe('buildUrl', () => {
  it('joins the base and path and drops empty query values', () => {
    expect(buildUrl('/api/v1/', 'admin/developers', { search: '', page: 2, pageSize: 25, other: undefined })).toBe(
      '/api/v1/admin/developers?page=2&pageSize=25',
    )
  })
})

describe('normalizeErrors', () => {
  it('handles strings, message objects and nonsense', () => {
    expect(normalizeErrors('Oops').list).toEqual(['Oops'])
    expect(normalizeErrors([{ message: 'A' }, { description: 'B' }, 42]).list).toEqual(['A', 'B'])
    expect(normalizeErrors(null).list).toEqual([])
  })
})
