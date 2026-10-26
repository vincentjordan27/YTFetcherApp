import type { AppTabItem } from '@/common/components/AppTabs.vue'

export const designAppShell = {
  version: 'V1.0',
  navigation: [
    { id: 'videos', label: 'Videos', to: '/videos/active' },
    { id: 'channels', label: 'Channels', to: '/channels/pending' },
    { id: 'keywords', label: 'Keywords', to: '/keywords' },
    { id: 'backfill-runs', label: 'Backfill', to: '/backfill-runs' },
    { id: 'usage', label: 'Usage', to: '/usage' },
  ] satisfies readonly AppTabItem[],
} as const
