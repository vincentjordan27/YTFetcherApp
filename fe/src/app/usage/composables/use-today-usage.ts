import { computed, onMounted, ref } from 'vue'
import { getTodayUsage } from '@/app/usage/api/get-today-usage'
import { toTodayUsage } from '@/app/usage/data/map-today-usage'
import type { TodayUsage, TodayUsageState } from '@/app/usage/types/today-usage'

const LOADING_MESSAGE = "Loading today's usage…"
const FAILURE_MESSAGE = "Couldn't load today's usage."

/**
 * Page-entry state for the live Usage page: one read of `GET /api/usage/today` and explicit
 * loading or failure feedback. A missing ledger is a valid zero-usage snapshot because the API
 * still returns the current configured limits.
 */
export function useTodayUsage() {
  const usage = ref<TodayUsage | null>(null)
  const loading = ref(false)
  const requestFailed = ref(false)

  const state = computed<TodayUsageState>(() => {
    if (loading.value) return 'loading'
    if (requestFailed.value) return 'error'
    return 'ready'
  })

  const stateMessage = computed(() => {
    if (state.value === 'loading') return LOADING_MESSAGE
    if (state.value === 'error') return FAILURE_MESSAGE
    return ''
  })

  onMounted(() => {
    void load()
  })

  async function load(): Promise<void> {
    loading.value = true
    requestFailed.value = false

    try {
      const response = await getTodayUsage()
      usage.value = toTodayUsage(response)
    } catch {
      // A failed request must never look like an empty ledger.
      usage.value = null
      requestFailed.value = true
    } finally {
      loading.value = false
    }
  }

  function retry(): void {
    void load()
  }

  return { usage, state, stateMessage, retry }
}
