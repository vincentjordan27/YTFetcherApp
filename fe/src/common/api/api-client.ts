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
  /** Auth endpoints opt out because their 401 is an intentional sign-in result, not an expired app session. */
  readonly handleUnauthorized?: boolean
}

/** Registers the one application-level reaction to a protected API request losing its session. */
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
    // The API runs on its own origin and the session travels in its HttpOnly cookie, so every request opts
    // into credentialed cross-origin calls.
    credentials: 'include',
    headers,
  })

  if (response.status === 204) {
    return undefined as T
  }

  const body = await readResponseBody(response)
  if (!response.ok) {
    if (response.status === 401 && handleUnauthorized) {
      unauthorizedResponseHandler?.()
    }

    throw new ApiError(response.status, body)
  }

  return body as T
}

async function readResponseBody(response: Response): Promise<unknown> {
  const contentType = response.headers.get('content-type') ?? ''
  // Problem responses use application/problem+json; both JSON media types carry a readable message.
  const isJson = contentType.includes('application/json') || contentType.includes('application/problem+json')
  return isJson ? response.json() : response.text()
}
