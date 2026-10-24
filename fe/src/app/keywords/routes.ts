import type { RouteRecordRaw } from 'vue-router'

const keywordRoutes: RouteRecordRaw[] = [
  {
    path: '/keywords',
    name: 'keywords',
    component: () => import('@/app/keywords/views/KeywordsView.vue'),
    meta: { requiresAuthentication: true },
  },
]

export default keywordRoutes
