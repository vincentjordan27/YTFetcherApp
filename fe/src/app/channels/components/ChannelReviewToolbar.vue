<script setup lang="ts">
import { onBeforeUnmount, ref, watch } from 'vue'
import AppTabs from '@/common/components/AppTabs.vue'
import type { ChannelTab } from '@/app/channels/types/channel-review'

defineProps<{
  tabs: readonly ChannelTab[]
  searchQuery: string
  searchLabel: string
  showAddChannel?: boolean
  selectedCount?: number
  showApproveSelection?: boolean
  showRejectSelection?: boolean
}>()

const emit = defineEmits<{
  'update:searchQuery': [value: string]
  'add-channel': []
  'approve-selection': []
  'reject-selection': []
}>()

const reviewMenuOpen = ref(false)
const reviewMenuRef = ref<HTMLElement | null>(null)

function toggleReviewMenu(): void {
  reviewMenuOpen.value = !reviewMenuOpen.value
}

function closeReviewMenu(): void {
  reviewMenuOpen.value = false
}

function approveSelection(): void {
  closeReviewMenu()
  emit('approve-selection')
}

function rejectSelection(): void {
  closeReviewMenu()
  emit('reject-selection')
}

function handlePointerDown(event: PointerEvent): void {
  if (reviewMenuRef.value?.contains(event.target as Node) !== true) closeReviewMenu()
}

function handleKeydown(event: KeyboardEvent): void {
  if (event.key === 'Escape') closeReviewMenu()
}

watch(reviewMenuOpen, isOpen => {
  if (isOpen) {
    document.addEventListener('pointerdown', handlePointerDown)
    document.addEventListener('keydown', handleKeydown)
    return
  }

  document.removeEventListener('pointerdown', handlePointerDown)
  document.removeEventListener('keydown', handleKeydown)
})

onBeforeUnmount(() => {
  document.removeEventListener('pointerdown', handlePointerDown)
  document.removeEventListener('keydown', handleKeydown)
})
</script>

<template>
  <div class="channel-review-toolbar">
    <AppTabs :items="tabs" class="channel-review-toolbar__tabs" />
    <div
      class="channel-review-toolbar__filters"
      :class="{
        'channel-review-toolbar__filters--with-add': showAddChannel,
        'channel-review-toolbar__filters--with-review': selectedCount && (showApproveSelection || showRejectSelection),
      }"
    >
      <label class="channel-review-toolbar__search">
        <span class="sr-only">{{ searchLabel }}</span>
        <svg class="channel-review-toolbar__search-icon" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
          <path d="M15.5 14h-.79l-.28-.27a6.5 6.5 0 001.48-5.34c-.47-2.78-2.79-5-5.59-5.34a6.505 6.505 0 00-7.27 7.27c.34 2.8 2.56 5.12 5.34 5.59a6.5 6.5 0 005.34-1.48l.27.28v.79l4.25 4.25c.41.41 1.08.41 1.49 0 .41-.41.41-1.08 0-1.49L15.5 14zm-6 0C7.01 14 5 11.99 5 9.5S7.01 5 9.5 5 14 7.01 14 9.5 11.99 14 9.5 14z" />
        </svg>
        <input
          :value="searchQuery"
          class="channel-review-toolbar__search-input"
          type="search"
          placeholder="Filter by name or channel ID..."
          @input="emit('update:searchQuery', ($event.target as HTMLInputElement).value)"
        />
      </label>
      <button
        v-if="showAddChannel"
        type="button"
        class="channel-review-toolbar__add"
        @click="emit('add-channel')"
      >
        <svg class="channel-review-toolbar__add-icon" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
          <path d="M19 13h-6v6h-2v-6H5v-2h6V5h2v6h6v2z" />
        </svg>
        <span>Add channel</span>
      </button>
      <div
        v-if="selectedCount && (showApproveSelection || showRejectSelection)"
        ref="reviewMenuRef"
        class="channel-review-toolbar__review-menu"
      >
        <button
          type="button"
          class="channel-review-toolbar__selection-action"
          aria-haspopup="menu"
          :aria-expanded="reviewMenuOpen"
          @click="toggleReviewMenu"
        >
          <span>Review selected</span>
          <span class="channel-review-toolbar__selection-count">{{ selectedCount }}</span>
          <svg class="channel-review-toolbar__menu-icon" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
            <path d="M7 10l5 5 5-5z" />
          </svg>
        </button>
        <div v-if="reviewMenuOpen" class="channel-review-toolbar__selection-menu" role="menu">
          <button
            v-if="showApproveSelection"
            type="button"
            class="channel-review-toolbar__selection-menu-item channel-review-toolbar__selection-menu-item--approve"
            role="menuitem"
            @click="approveSelection"
          >
            Approve selected
          </button>
          <button
            v-if="showRejectSelection"
            type="button"
            class="channel-review-toolbar__selection-menu-item channel-review-toolbar__selection-menu-item--reject"
            role="menuitem"
            @click="rejectSelection"
          >
            Reject selected
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<style lang="scss" scoped>
@use '@/app/channels/styles/channel-review-toolbar';
</style>
