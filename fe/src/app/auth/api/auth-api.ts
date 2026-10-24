import { apiRequest } from '@/common/api/api-client'
import type {
  CreateAuthenticationSessionApiRequest,
  CurrentSessionApiResponse,
} from '@/app/auth/api/types/authentication-api'

/** Reads the current application session. A disabled instance reports disabled and no identity. */
export function getCurrentSession(): Promise<CurrentSessionApiResponse> {
  return apiRequest<CurrentSessionApiResponse>('/api/auth/session', { handleUnauthorized: false })
}

/** Exchanges a Firebase ID token for the backend session cookie. */
export function createAuthenticationSession(firebaseIdToken: string): Promise<CurrentSessionApiResponse> {
  const request: CreateAuthenticationSessionApiRequest = { firebaseIdToken }

  return apiRequest<CurrentSessionApiResponse>('/api/auth/session', {
    method: 'POST',
    body: JSON.stringify(request),
    handleUnauthorized: false,
  })
}

/** Clears the backend session cookie. It is safe to call without a session. */
export function logoutSession(): Promise<void> {
  return apiRequest<void>('/api/auth/logout', {
    method: 'POST',
    body: JSON.stringify({}),
    handleUnauthorized: false,
  })
}
