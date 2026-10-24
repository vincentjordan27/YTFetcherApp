import { createRouter, createWebHistory } from 'vue-router'
import { useAuthentication } from '@/app/auth/composables/useAuthentication'
import { setUnauthorizedResponseHandler } from '@/common/api/api-client'
import { routes } from './routes'

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes,
})

let handlingUnauthorizedResponse = false

setUnauthorizedResponseHandler(() => {
  if (handlingUnauthorizedResponse) {
    return
  }

  handlingUnauthorizedResponse = true
  const authentication = useAuthentication()
  authentication.handleUnauthorizedResponse()
  const currentRoute = router.currentRoute.value
  const returnTo = currentRoute.fullPath.startsWith('/') && !currentRoute.fullPath.startsWith('//')
    ? currentRoute.fullPath
    : '/videos'

  void router.replace({ name: 'auth-login', query: { returnTo } }).finally(() => {
    handlingUnauthorizedResponse = false
  })
})

/**
 * The single authentication guard. It resolves the deployment mode before a route renders, so enabled
 * deployments never show protected data behind a guessed anonymous state and disabled deployments keep
 * working exactly as before. Route protection stays server-side: this guard only decides what to render.
 */
router.beforeEach(async (to) => {
  const authentication = useAuthentication()

  await authentication.bootstrap()

  if (authentication.bootstrapState.value === 'failed') {
    // Fail closed: the mode is unknown, so only the login screen may render.
    return to.meta.requiresAuthentication === true ? { name: 'auth-login' } : true
  }

  if (!authentication.authenticationEnabled.value) {
    // Intentionally anonymous instance: there is nothing to sign in to, so the login route is not reachable.
    return to.name === 'auth-login' ? { path: '/videos' } : true
  }

  if (!authentication.isAuthenticated.value) {
    return to.meta.requiresAuthentication === true ? { name: 'auth-login' } : true
  }

  return to.name === 'auth-login' ? { path: '/videos' } : true
})

export default router
