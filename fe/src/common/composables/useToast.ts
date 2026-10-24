import { readonly, ref } from 'vue'

export type ToastTone = 'success' | 'failure' | 'info'
export type ToastPosition = 'top-right' | 'bottom-right'

export interface ToastMessage {
  tone: ToastTone
  message: string
  position?: ToastPosition
}

const currentToast = ref<ToastMessage | null>(null)
let dismissTimer: ReturnType<typeof window.setTimeout> | undefined

export function useToast() {
  function dismissToast(): void {
    if (dismissTimer) {
      window.clearTimeout(dismissTimer)
      dismissTimer = undefined
    }

    currentToast.value = null
  }

  function showToast({
    tone,
    message,
    position = 'top-right',
    durationMs = 5000,
  }: ToastMessage & { durationMs?: number }): void {
    dismissToast()
    currentToast.value = { tone, message, position }

    dismissTimer = window.setTimeout(dismissToast, durationMs)
  }

  return {
    currentToast: readonly(currentToast),
    showToast,
    dismissToast,
  }
}
