export class ApiError extends Error {
  public readonly status: number
  public readonly body: unknown

  public constructor(status: number, body: unknown) {
    super(`Request failed with HTTP ${status}.`)
    this.name = 'ApiError'
    this.status = status
    this.body = body
  }
}

const apiBaseUrl = import.meta.env.VITE_API_BASE_URL?.replace(/\/$/, '') ?? ''
let unauthorizedResponseHandler: (() => void) | null = null

interface ApiRequestInit extends RequestInit {
  readonly handleUnauthorized?: boolean
}

export function setUnauthorizedResponseHandler(handler: (() => void) | null): void {
  unauthorizedResponseHandler = handler
}

export async function apiRequest<T>(path: string, init: ApiRequestInit = {}): Promise<T> {
  const { handleUnauthorized = true, ...requestInit } = init
  const headers = new Headers(requestInit.headers)
  headers.set('Accept', 'application/json')

  if (requestInit.body && !headers.has('Content-Type')) {
    headers.set('Content-Type', 'application/json')
  }

  const response = await fetch(`${apiBaseUrl}${path}`, {
    ...requestInit,
    credentials: 'include',
    headers,
  })

  if (response.status === 204) {
    return undefined as T
  }

  const body = await readResponseBody(response)
  if (response.ok) {
    return body as T
  }

  if (response.status === 401 && handleUnauthorized) {
    unauthorizedResponseHandler?.()
  }

  throw new ApiError(response.status, body)
}

async function readResponseBody(response: Response): Promise<unknown> {
  const contentType = response.headers.get('content-type') ?? ''
  const isJson = contentType.includes('application/json') || contentType.includes('application/problem+json')
  return isJson ? response.json() : response.text()
}
