import { computed, readonly, ref } from 'vue'
import {
  createAuthenticationSession,
  getCurrentSession,
  logoutSession,
} from '@/app/auth/api/auth-api'
import type { AuthenticationIdentityApi } from '@/app/auth/api/types/authentication-api'
import { authCopy } from '@/app/auth/data/auth-copy'
import type { AuthenticationBootstrap } from '@/app/auth/types/authentication.types'
import { ApiError } from '@/common/api/api-client'

/**
 * Application-wide authentication state. There is exactly one bootstrap, one identity, and one in-flight
 * submit for the whole application, so the router guard, the login view, and the shell always agree and the
 * backend mode stays authoritative.
 */
const bootstrapState = ref<AuthenticationBootstrap>('pending')
const identity = ref<AuthenticationIdentityApi | null>(null)
const isSubmitting = ref(false)
const errorMessage = ref<string | null>(null)
let bootstrapRequest: Promise<void> | null = null

export function useAuthentication() {
  const authenticationEnabled = computed(() => bootstrapState.value === 'enabled')
  const isAuthenticated = computed(() => identity.value !== null)

  function bootstrap(): Promise<void> {
    if (bootstrapState.value === 'enabled' || bootstrapState.value === 'disabled') {
      return Promise.resolve()
    }

    // Concurrent callers (guard plus view) share one bootstrap instead of racing two requests.
    bootstrapRequest ??= runBootstrap()

    return bootstrapRequest
  }

  function retryBootstrap(): void {
    bootstrapState.value = 'pending'
    void bootstrap()
  }

  async function signInWithGoogle(): Promise<boolean> {
    if (isSubmitting.value || !authenticationEnabled.value) {
      return false
    }

    isSubmitting.value = true
    errorMessage.value = null

    try {
      const firebaseIdToken = await requestGoogleIdToken()
      const session = await createAuthenticationSession(firebaseIdToken)

      identity.value = session.identity ?? null
      return identity.value !== null
    } catch (caught) {
      errorMessage.value = signInErrorMessage(caught)
      return false
    } finally {
      isSubmitting.value = false
    }
  }

  async function logout(): Promise<void> {
    try {
      await logoutSession()
    } catch {
      // Clearing the backend cookie is idempotent server-side, so a failed call must not trap the operator.
    }

    await signOutFirebaseClient()
    identity.value = null
  }

  async function runBootstrap(): Promise<void> {
    try {
      const session = await getCurrentSession()
      if (!session.authenticationEnabled) {
        identity.value = null
        bootstrapState.value = 'disabled'
        return
      }

      identity.value = session.authenticated ? session.identity ?? null : null
      bootstrapState.value = 'enabled'
    } catch {
      // Fail closed: an unreadable mode must never be presented as an anonymous deployment.
      identity.value = null
      bootstrapState.value = 'failed'
    } finally {
      bootstrapRequest = null
    }
  }

  function handleUnauthorizedResponse(): void {
    // A protected endpoint returned 401, which is proof the active server now requires a session. This covers
    // an Auth:Enabled false -> true redeploy without polling or retrying the rejected operation.
    identity.value = null
    bootstrapState.value = 'enabled'
  }

  return {
    bootstrapState: readonly(bootstrapState),
    authenticationEnabled,
    identity: readonly(identity),
    isAuthenticated,
    isSubmitting: readonly(isSubmitting),
    errorMessage: readonly(errorMessage),
    bootstrap,
    retryBootstrap,
    handleUnauthorizedResponse,
    signInWithGoogle,
    logout,
  }
}

/**
 * Obtains a Firebase ID token through the Google popup. Firebase is imported on demand, so a deployment with
 * authentication disabled never downloads, initializes, or calls Firebase.
 */
async function requestGoogleIdToken(): Promise<string> {
  const apiKey = import.meta.env.VITE_FIREBASE_API_KEY?.trim()
  const authDomain = import.meta.env.VITE_FIREBASE_AUTH_DOMAIN?.trim()
  const projectId = import.meta.env.VITE_FIREBASE_PROJECT_ID?.trim()
  const appId = import.meta.env.VITE_FIREBASE_APP_ID?.trim()

  if (!apiKey || !authDomain || !projectId || !appId) {
    throw new Error('Firebase browser configuration is not available.')
  }

  const [{ getApp, getApps, initializeApp }, { GoogleAuthProvider, getAuth, signInWithPopup }] = await Promise.all([
    import('firebase/app'),
    import('firebase/auth'),
  ])

  const app = getApps().length > 0
    ? getApp()
    : initializeApp({
      apiKey,
      authDomain,
      projectId,
      appId,
    })

  const credential = await signInWithPopup(getAuth(app), new GoogleAuthProvider())

  return credential.user.getIdToken()
}

/** Best-effort Firebase client sign-out after the backend session is already gone. */
async function signOutFirebaseClient(): Promise<void> {
  try {
    const [{ getApps }, { getAuth, signOut }] = await Promise.all([import('firebase/app'), import('firebase/auth')])

    if (getApps().length === 0) {
      return
    }

    await signOut(getAuth())
  } catch {
    // The backend cookie is the application session; the provider client sign-out is only housekeeping.
  }
}

/** The single user-readable mapping for a failed sign-in. */
function signInErrorMessage(caught: unknown): string {
  if (caught instanceof ApiError) {
    const detail = readProblemDetail(caught.body)

    // Backend 4xx denials are already written for an operator, so they are shown as received.
    if (detail !== null) {
      return detail
    }
  }

  return authCopy.errors.signInFailed
}

function readProblemDetail(body: unknown): string | null {
  if (typeof body !== 'object' || body === null) {
    return null
  }

  const detail: unknown = (body as { detail?: unknown }).detail

  return typeof detail === 'string' && detail.trim().length > 0 ? detail : null
}
