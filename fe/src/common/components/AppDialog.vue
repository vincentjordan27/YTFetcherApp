<script setup lang="ts">
import { nextTick, onBeforeUnmount, ref, watch } from 'vue'

const props = withDefaults(
  defineProps<{
    open: boolean
    labelledBy: string
    busy?: boolean
    variant?: string
  }>(),
  { busy: false },
)

const emit = defineEmits<{ close: [] }>()
const dialogRef = ref<HTMLElement | null>(null)
let invoker: HTMLElement | null = null

function focusableElements(): HTMLElement[] {
  return Array.from(
    dialogRef.value?.querySelectorAll<HTMLElement>(
      'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])',
    ) ?? [],
  )
}

function requestClose(): void {
  if (!props.busy) emit('close')
}

function handleKeydown(event: KeyboardEvent): void {
  if (event.key === 'Escape') {
    event.preventDefault()
    requestClose()
    return
  }

  if (event.key !== 'Tab') return
  const elements = focusableElements()
  if (elements.length === 0) return

  const first = elements[0]
  const last = elements[elements.length - 1]
  if (event.shiftKey && document.activeElement === first) {
    event.preventDefault()
    last.focus()
  } else if (!event.shiftKey && document.activeElement === last) {
    event.preventDefault()
    first.focus()
  }
}

watch(
  () => props.open,
  async (isOpen, wasOpen) => {
    if (isOpen) {
      invoker = document.activeElement instanceof HTMLElement ? document.activeElement : null
      await nextTick()
      focusableElements()[0]?.focus()
    } else if (wasOpen) {
      invoker?.focus()
      invoker = null
    }
  },
  { immediate: true },
)

onBeforeUnmount(() => invoker?.focus())
</script>

<template>
  <Teleport to="body">
    <div v-if="open" class="app-dialog" @keydown="handleKeydown">
      <button class="app-dialog__backdrop" type="button" aria-label="Close dialog" :disabled="busy" @click="requestClose" />
      <section ref="dialogRef" class="app-dialog__surface" :class="variant ? `app-dialog__surface--${variant}` : ''" role="dialog" aria-modal="true" :aria-labelledby="labelledBy">
        <header v-if="$slots.header" class="app-dialog__header"><slot name="header" /></header>
        <div class="app-dialog__body"><slot /></div>
        <footer class="app-dialog__actions"><slot name="actions" /></footer>
      </section>
    </div>
  </Teleport>
</template>

<style lang="scss" scoped>
@use '@/common/styles/app-dialog';
</style>
