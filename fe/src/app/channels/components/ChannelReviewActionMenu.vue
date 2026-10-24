<script setup lang="ts">
import { onBeforeUnmount, ref, watch } from 'vue'

const props = defineProps<{
  label: string
  canApprove: boolean
  canReject: boolean
}>()

const emit = defineEmits<{
  approve: []
  reject: []
}>()

const PANEL_WIDTH = 168
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

function approve(): void {
  close()
  emit('approve')
}

function reject(): void {
  close()
  emit('reject')
}

function handlePointerDown(event: PointerEvent): void {
  const target = event.target as Node

  if (triggerRef.value?.contains(target) === true || panelRef.value?.contains(target) === true) return

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
  <div class="channel-review-action-menu">
    <button
      ref="triggerRef"
      type="button"
      class="channel-review-action-menu__trigger"
      :aria-label="`Review ${props.label}`"
      aria-haspopup="menu"
      :aria-expanded="open"
      @click="toggle"
    >
      <svg class="channel-review-action-menu__icon" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
        <path d="M12 8c1.1 0 2-.9 2-2s-.9-2-2-2-2 .9-2 2 .9 2 2 2zm0 2c-1.1 0-2 .9-2 2s.9 2 2 2 2-.9 2-2-.9-2-2-2zm0 6c-1.1 0-2 .9-2 2s.9 2 2 2 2-.9 2-2-.9-2-2-2z" />
      </svg>
    </button>

    <Teleport to="body">
      <div v-if="open" ref="panelRef" class="channel-review-action-menu__panel" role="menu" :style="panelStyle">
        <button
          v-if="props.canApprove"
          type="button"
          class="channel-review-action-menu__item channel-review-action-menu__item--approve"
          role="menuitem"
          @click="approve"
        >
          Approve
        </button>
        <button
          v-if="props.canReject"
          type="button"
          class="channel-review-action-menu__item channel-review-action-menu__item--reject"
          role="menuitem"
          @click="reject"
        >
          Reject
        </button>
      </div>
    </Teleport>
  </div>
</template>

<style lang="scss" scoped>
@use '@/app/channels/styles/channel-review-action-menu';
</style>
