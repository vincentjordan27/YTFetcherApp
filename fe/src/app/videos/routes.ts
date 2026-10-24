import type { RouteRecordRaw } from 'vue-router'

const videoRoutes: RouteRecordRaw[] = [
  {
    path: '/videos',
    redirect: '/videos/active',
  },
  {
    path: '/videos/active',
    name: 'videos-active',
    component: () => import('@/app/videos/views/ActiveVideosView.vue'),
    meta: { requiresAuthentication: true },
  },
  {
    path: '/videos/reference',
    name: 'videos-reference',
    component: () => import('@/app/videos/views/ReferenceVideosView.vue'),
    meta: { requiresAuthentication: true },
  },
]

export default videoRoutes
