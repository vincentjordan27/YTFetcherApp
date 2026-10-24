import type { RouteRecordRaw } from 'vue-router'

const usageRoutes: RouteRecordRaw[] = [
  {
    path: '/usage',
    name: 'usage',
    component: () => import('@/app/usage/views/UsageView.vue'),
    meta: { requiresAuthentication: true },
  },
]

export default usageRoutes
