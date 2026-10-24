<script setup lang="ts">
import type { ChannelEvidenceView } from '@/app/channels/types/channel-review'
import { computed, ref } from 'vue'

const props = defineProps<{
  channelId: string
  discoveredAt: string
  evidenceCount: number
  items: readonly ChannelEvidenceView[]
  loading: boolean
  failed: boolean
}>()

const firstKeyword = computed(() => props.items[0]?.keyword ?? null)
const loadedThumbnailIds = ref(new Set<string>())

function isThumbnailLoading(videoId: string): boolean {
  return !loadedThumbnailIds.value.has(videoId)
}

function markThumbnailLoaded(videoId: string): void {
  loadedThumbnailIds.value = new Set(loadedThumbnailIds.value).add(videoId)
}
</script>

<template>
  <tr :id="`evidence-${channelId}`" class="pending-channel-evidence">
    <td colspan="7" class="pending-channel-evidence__cell">
      <div class="pending-channel-evidence__panel">
        <p v-if="loading" class="pending-channel-evidence__message">Loading discovery evidence…</p>
        <p v-else-if="failed" class="pending-channel-evidence__message">Couldn't load discovery evidence.</p>
        <p v-else-if="items.length === 0" class="pending-channel-evidence__message">No discovery evidence recorded for this channel.</p>

        <template v-else>
          <div class="pending-channel-evidence__header">
            <div class="pending-channel-evidence__source">
              <svg class="pending-channel-evidence__source-icon" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
                <path d="M17.81 4.47c-.08 0-.16-.02-.23-.06C15.66 3.42 14 3 12.01 3c-1.98 0-3.86.47-5.57 1.41-.24.13-.54.04-.68-.2-.13-.24-.04-.55.2-.68C7.82 2.52 9.86 2 12.01 2c2.13 0 3.99.47 6.03 1.52.25.13.34.43.21.67-.09.18-.26.28-.44.28zM3.5 9.72c-.1 0-.2-.03-.29-.09-.23-.16-.28-.47-.12-.7.99-1.4 2.25-2.5 3.75-3.27C9.98 4.04 14 4.03 17.15 5.65c1.5.77 2.76 1.86 3.75 3.25.16.22.11.54-.12.7-.23.16-.54.11-.7-.12-.9-1.26-2.04-2.25-3.39-2.94-2.87-1.47-6.54-1.47-9.4.01-1.36.7-2.5 1.7-3.4 2.96-.08.14-.23.21-.39.21zm6.25 12.07c-.16 0-.31-.08-.4-.22-.36-.55-.69-1.14-.98-1.76-.02-.05-.04-.1-.05-.15-.14-.4-.26-.81-.36-1.23-.06-.27.11-.54.38-.6.27-.06.54.11.6.38.09.38.2.75.33 1.12.01.04.02.07.04.1.27.58.58 1.13.92 1.65.15.23.09.54-.14.69-.08.06-.18.09-.27.09zm-2.13-3.31c-.22 0-.41-.16-.45-.38-.2-1.15-.24-2.32-.1-3.47.03-.28.28-.47.55-.44.28.03.47.27.44.55-.13 1.04-.09 2.1.09 3.14.05.27-.13.53-.4.58-.05.01-.09.02-.13.02zM12 22c-.11 0-.22-.01-.33-.04-1.55-.36-2.99-1.04-4.2-1.98-.22-.17-.26-.48-.09-.7.17-.22.48-.26.7-.09 1.08.84 2.37 1.45 3.75 1.77.27.06.44.33.38.6-.06.24-.26.4-.49.4zm6.35-1.98c-.13 0-.25-.05-.34-.15-.19-.19-.18-.5.01-.69.75-.73 1.45-1.6 2.03-2.51.15-.24.46-.31.69-.16.24.15.31.46.16.69-.64.99-1.4 1.94-2.22 2.74-.09.08-.21.13-.33.13zM22 12c0 .28-.22.5-.5.5s-.5-.22-.5-.5c0-1.13-.22-2.23-.65-3.28-.11-.26.01-.56.27-.66.26-.11.56.01.66.27.48 1.17.72 2.39.72 3.67zm-3.13-5.06c-.13 0-.26-.05-.35-.15-.19-.2-.19-.51.01-.7.19-.19.5-.19.7 0 .01.01.01.01.02.02.2.19.2.5.01.7-.1.09-.23.13-.39.13z" />
              </svg>
              <span class="pending-channel-evidence__source-text">
                Discovered via
                <span v-if="firstKeyword" class="pending-channel-evidence__keyword">"{{ firstKeyword }}"</span>
                <span class="pending-channel-evidence__dot">•</span>
                {{ discoveredAt }}
              </span>
            </div>
            <span class="pending-channel-evidence__caption">
              Snapshot of {{ evidenceCount }} evidence shorts
            </span>
          </div>

          <div class="pending-channel-evidence__grid">
            <article v-for="item in items" :key="item.id" class="pending-channel-evidence__card">
              <div class="pending-channel-evidence__thumb" :class="{ 'pending-channel-evidence__thumb--loading': item.thumbnailSrc && isThumbnailLoading(item.id) }">
                <img
                  v-if="item.thumbnailSrc"
                  :src="item.thumbnailSrc"
                  :alt="item.title"
                  width="320"
                  height="150"
                  loading="lazy"
                  decoding="async"
                  referrerpolicy="no-referrer"
                  @load="markThumbnailLoaded(item.id)"
                  @error="markThumbnailLoaded(item.id); ($event.target as HTMLImageElement).hidden = true"
                />
              </div>
              <span class="pending-channel-evidence__title">{{ item.title }}</span>
              <div class="pending-channel-evidence__footer">
                <span class="pending-channel-evidence__date">{{ item.publishedAt }}</span>
                <a class="pending-channel-evidence__link" :href="item.youtubeUrl" target="_blank" rel="noopener noreferrer">
                  <span>Open</span>
                  <svg class="pending-channel-evidence__link-icon" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
                    <path d="M6 18L17.17 6.83 18.59 8.25 7.41 19.41 6 18zM17 6h-5V4h7v7h-2V6z" />
                  </svg>
                </a>
              </div>
            </article>
          </div>
        </template>
      </div>
    </td>
  </tr>
</template>

<style lang="scss" scoped>
@use '@/app/channels/styles/pending-channel-evidence';
</style>
