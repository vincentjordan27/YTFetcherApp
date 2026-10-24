import type { RouteRecordRaw } from 'vue-router'
import authRoutes from '@/app/auth/routes'
import backfillRunRoutes from '@/app/backfill-runs/routes'
import channelRoutes from '@/app/channels/routes'
import keywordRoutes from '@/app/keywords/routes'
import usageRoutes from '@/app/usage/routes'
import videoRoutes from '@/app/videos/routes'
import NotFoundView from '@/common/views/NotFoundView.vue'

declare module 'vue-router' {
  interface RouteMeta {
    /**
     * True when the route renders protected application data. The authentication guard sends such a route to
     * the login screen while an enabled deployment has no session.
     */
    requiresAuthentication?: boolean
  }
}

export const routes: RouteRecordRaw[] = [
  {
    path: '/',
    redirect: '/videos',
  },
  ...authRoutes,
  ...backfillRunRoutes,
  ...channelRoutes,
  ...keywordRoutes,
  ...usageRoutes,
  ...videoRoutes,
  {
    path: '/:pathMatch(.*)*',
    name: 'not-found',
    component: NotFoundView,
  },
]
