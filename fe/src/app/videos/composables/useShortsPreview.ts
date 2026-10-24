import { ref } from 'vue'
import type { ShortsPreviewTarget } from '@/app/videos/types/shorts-preview.types'

/**
 * Shared watch-popup state for the Active and Reference tables: the open flag plus the selected row. It
 * performs no request, because the popup plays the provider id the row already carries. Closing clears the
 * target so the embed is unmounted and playback stops.
 */
export function useShortsPreview() {
  const open = ref(false)
  const target = ref<ShortsPreviewTarget | null>(null)

  function openFor(row: ShortsPreviewTarget): void {
    target.value = row
    open.value = true
  }

  function close(): void {
    open.value = false
    target.value = null
  }

  return { open, target, openFor, close }
}
