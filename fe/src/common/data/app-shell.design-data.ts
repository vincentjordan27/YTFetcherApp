import type { AppTabItem } from '@/common/components/AppTabs.vue'

export const designAppShell = {
  version: 'V2.4',
  operator: {
    initials: 'ER',
    name: 'Elena Rostova',
    email: 'operator@ytfetcher.local',
    role: 'Ops SuperAdmin',
  },
  navigation: [
    { id: 'videos', label: 'Videos', to: '/videos/active' },
    { id: 'channels', label: 'Channels', to: '/channels/pending' },
    { id: 'keywords', label: 'Keywords', to: '/keywords' },
    { id: 'backfill-runs', label: 'Backfill', to: '/backfill-runs' },
    { id: 'usage', label: 'Usage', to: '/usage' },
  ] satisfies readonly AppTabItem[],
} as const
