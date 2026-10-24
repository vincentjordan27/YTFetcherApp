import type { RouteRecordRaw } from 'vue-router'

const backfillRunRoutes: RouteRecordRaw[] = [
  {
    path: '/backfill-runs',
    name: 'backfill-runs',
    component: () => import('@/app/backfill-runs/views/BackfillRunsView.vue'),
    meta: { requiresAuthentication: true },
  },
]

export default backfillRunRoutes
