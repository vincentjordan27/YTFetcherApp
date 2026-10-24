<script setup lang="ts">
import type { BackfillRunTableState } from '@/app/backfill-runs/types/backfill-run.types'

defineProps<{
  colspan: number
  message: string
  state: BackfillRunTableState['state']
}>()

const emit = defineEmits<{ retry: [] }>()
</script>

<template>
  <tr class="backfill-runs-page__state-row">
    <td :colspan="colspan" class="backfill-runs-page__state-cell">
      <span
        class="backfill-runs-page__state-content"
        :class="{ 'backfill-runs-page__state-content--busy': state === 'loading' }"
      >
        <span v-if="state === 'loading'" class="backfill-runs-page__state-indicator" aria-hidden="true" />
        <span class="backfill-runs-page__state-message">{{ message }}</span>
        <button
          v-if="state === 'error'"
          type="button"
          class="backfill-runs-page__btn-refresh"
          @click="emit('retry')"
        >
          Retry
        </button>
      </span>
    </td>
  </tr>
</template>
