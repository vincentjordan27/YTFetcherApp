<script setup lang="ts">
import AppDialog from '@/common/components/AppDialog.vue'
import AppPagination from '@/common/components/AppPagination.vue'
import type {
  VideoHistoryPagination,
  VideoHistoryRow,
  VideoHistoryState,
  VideoHistoryTarget,
} from '@/app/videos/types/video-history.types'

const props = defineProps<{
  open: boolean
  target: VideoHistoryTarget | null
  rows: readonly VideoHistoryRow[]
  pagination: VideoHistoryPagination
  currentPage: number
  state: VideoHistoryState
  stateMessage: string
}>()

const emit = defineEmits<{
  close: []
  retry: []
  'update:page': [page: number]
}>()

const dialogTitleId = 'video-history-dialog-title'
</script>

<template>
  <AppDialog
    :open="open"
    :labelled-by="dialogTitleId"
    variant="video-history"
    @close="emit('close')"
  >
    <template #header>
      <div class="video-history-dialog__heading">
        <h2 :id="dialogTitleId" class="video-history-dialog__title">Snapshot history</h2>
        <p v-if="target" class="video-history-dialog__context">
          <span class="video-history-dialog__video-title" :title="target.title">{{ target.title }}</span>
          <span class="video-history-dialog__channel">{{ target.channelName }}</span>
        </p>
      </div>
      <button type="button" class="video-history-dialog__close" aria-label="Close dialog" @click="emit('close')">
        <svg class="video-history-dialog__close-icon" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
          <path d="M19 6.41L17.59 5 12 10.59 6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 12 13.41 17.59 19 19 17.59 13.41 12z" />
        </svg>
      </button>
    </template>

    <p v-if="state === 'loading'" class="video-history-dialog__feedback" role="status">{{ stateMessage }}</p>

    <div v-else-if="state === 'error'" class="video-history-dialog__feedback video-history-dialog__feedback--error" role="alert">
      <span>{{ stateMessage }}</span>
      <button type="button" class="video-history-dialog__retry" @click="emit('retry')">Retry</button>
    </div>

    <p v-else-if="state === 'empty'" class="video-history-dialog__feedback">{{ stateMessage }}</p>

    <table v-else class="video-history-dialog__table">
      <thead>
        <tr>
          <th scope="col">Captured</th>
          <th scope="col" class="video-history-dialog__numeric">Views</th>
          <th scope="col" class="video-history-dialog__numeric">Likes</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="row in props.rows" :key="row.id">
          <td>{{ row.capturedAtLabel }}</td>
          <td class="video-history-dialog__numeric">{{ row.viewsLabel }}</td>
          <td class="video-history-dialog__numeric">{{ row.likesLabel }}</td>
        </tr>
      </tbody>
    </table>

    <template #actions>
      <div class="video-history-dialog__footer">
        <span class="video-history-dialog__summary">{{ pagination.summary }}</span>
        <AppPagination
          v-if="state === 'ready'"
          :page="currentPage"
          :page-count="pagination.pageCount"
          :display-pages="pagination.displayPages"
          @update:page="emit('update:page', $event)"
        />
      </div>
    </template>
  </AppDialog>
</template>

<style lang="scss">
@use '@/app/videos/styles/video-history-dialog';
</style>
