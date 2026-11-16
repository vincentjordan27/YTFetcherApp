<script setup lang="ts">
import { onBeforeUnmount, ref, watch } from 'vue'
import type { VideoReferenceStatus } from '@/app/videos/types/video-reference-status'

type ReferenceStatus = Exclude<VideoReferenceStatus, 'NONE'>

const props = withDefaults(defineProps<{ label: string; markUnused?: boolean; skip?: boolean; removable?: boolean; referenceStatus?: ReferenceStatus; busy?: boolean }>(), {
  markUnused: false,
  skip: false,
  removable: false,
  busy: false,
})

const emit = defineEmits<{
  history: []
  'mark-unused': []
  skip: []
  remove: []
  'set-reference-status': [status: ReferenceStatus]
}>()

const PANEL_WIDTH = 210
const PANEL_MARGIN = 8

const open = ref(false)
const triggerRef = ref<HTMLButtonElement | null>(null)
const panelRef = ref<HTMLElement | null>(null)
const panelStyle = ref<Record<string, string>>({})

function toggle(): void {
  if (open.value) {
    close()
    return
  }

  const trigger = triggerRef.value
  if (trigger === null) return

  // The panel is teleported so the table's scroll container can never clip it; its position is measured
  // from the trigger each time it opens.
  const rect = trigger.getBoundingClientRect()
  const left = Math.max(PANEL_MARGIN, Math.min(rect.right - PANEL_WIDTH, window.innerWidth - PANEL_WIDTH - PANEL_MARGIN))

  panelStyle.value = {
    left: `${left}px`,
    top: `${rect.bottom + 4}px`,
    width: `${PANEL_WIDTH}px`,
  }
  open.value = true
}

function close(): void {
  open.value = false
}

function selectHistory(): void {
  close()
  emit('history')
}

function selectMarkUnused(): void {
  close()
  emit('mark-unused')
}

function selectSkip(): void {
  close()
  emit('skip')
}

function selectRemove(): void {
  close()
  emit('remove')
}

function selectReferenceStatus(status: ReferenceStatus): void {
  close()
  emit('set-reference-status', status)
}

function handlePointerDown(event: PointerEvent): void {
  const target = event.target as Node

  if (triggerRef.value?.contains(target) === true || panelRef.value?.contains(target) === true) {
    return
  }

  close()
}

function handleKeydown(event: KeyboardEvent): void {
  if (event.key !== 'Escape') return

  event.preventDefault()
  triggerRef.value?.focus()
  close()
}

watch(open, isOpen => {
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
  <div class="video-row-menu">
    <button
      ref="triggerRef"
      type="button"
      class="video-row-menu__trigger"
      :aria-label="`More actions for ${props.label}`"
      aria-haspopup="menu"
      :aria-expanded="open"
      :disabled="props.busy"
      @click="toggle"
    >
      <svg class="video-row-menu__icon" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
        <path d="M12 8c1.1 0 2-.9 2-2s-.9-2-2-2-2 .9-2 2 .9 2 2 2zm0 2c-1.1 0-2 .9-2 2s.9 2 2 2 2-.9 2-2-.9-2-2-2zm0 6c-1.1 0-2 .9-2 2s.9 2 2 2 2-.9 2-2-.9-2-2-2z" />
      </svg>
    </button>

    <Teleport to="body">
      <div v-if="open" ref="panelRef" class="video-row-menu__panel" role="menu" :style="panelStyle">
        <button v-if="props.markUnused" type="button" class="video-row-menu__item" role="menuitem" @click="selectMarkUnused">
          Save as unused reference
        </button>
        <button v-if="props.skip" type="button" class="video-row-menu__item" role="menuitem" @click="selectSkip">
          Skip
        </button>
        <button type="button" class="video-row-menu__item" role="menuitem" @click="selectHistory">
          History
        </button>
        <button
          v-if="props.referenceStatus === 'UNUSED' || props.referenceStatus === 'USED'"
          type="button"
          class="video-row-menu__item"
          role="menuitem"
          @click="selectReferenceStatus(props.referenceStatus === 'USED' ? 'UNUSED' : 'USED')"
        >
          {{ props.referenceStatus === 'USED' ? 'Mark as unused' : 'Mark as used' }}
        </button>
        <button
          v-if="props.referenceStatus === 'UNUSED' || props.referenceStatus === 'USED'"
          type="button"
          class="video-row-menu__item"
          role="menuitem"
          @click="selectReferenceStatus('SKIPPED')"
        >
          Skip
        </button>
        <button
          v-if="props.referenceStatus === 'SKIPPED'"
          type="button"
          class="video-row-menu__item"
          role="menuitem"
          @click="selectReferenceStatus('UNUSED')"
        >
          Mark as unused
        </button>
        <button
          v-if="props.removable"
          type="button"
          class="video-row-menu__item video-row-menu__item--danger"
          role="menuitem"
          @click="selectRemove"
        >
          Return to active
        </button>
      </div>
    </Teleport>
  </div>
</template>

<style lang="scss" scoped>
@use '@/app/videos/styles/video-row-action-menu';
</style>
