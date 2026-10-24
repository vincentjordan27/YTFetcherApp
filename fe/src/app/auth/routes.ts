import type { RouteRecordRaw } from 'vue-router'

const authRoutes: RouteRecordRaw[] = [
  {
    path: '/login',
    name: 'auth-login',
    component: () => import('@/app/auth/views/LoginView.vue'),
    meta: { requiresAuthentication: false },
  },
]

export default authRoutes
