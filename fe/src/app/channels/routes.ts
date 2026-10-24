import type { RouteRecordRaw } from 'vue-router'

const channelRoutes: RouteRecordRaw[] = [
  {
    path: '/channels',
    redirect: '/channels/pending',
  },
  {
    path: '/channels/pending',
    name: 'channels-pending',
    component: () => import('@/app/channels/views/PendingChannelsView.vue'),
    meta: { requiresAuthentication: true },
  },
  {
    path: '/channels/approved',
    name: 'channels-approved',
    component: () => import('@/app/channels/views/ApprovedChannelsView.vue'),
    meta: { requiresAuthentication: true },
  },
  {
    path: '/channels/rejected',
    name: 'channels-rejected',
    component: () => import('@/app/channels/views/RejectedChannelsView.vue'),
    meta: { requiresAuthentication: true },
  },
]

export default channelRoutes
