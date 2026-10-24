<script setup lang="ts">
import { computed } from 'vue'
import AppDialog from '@/common/components/AppDialog.vue'
import type { ShortsPreviewTarget } from '@/app/videos/types/shorts-preview.types'

const props = defineProps<{
  open: boolean
  target: ShortsPreviewTarget | null
}>()

const emit = defineEmits<{ close: [] }>()

const dialogTitleId = 'shorts-preview-dialog-title'

/**
 * A YouTube video id is exactly the 11-character identifier shape. Anything else is treated as unusable
 * input: the popup then shows a plain unavailable message and never builds a player URL from it.
 */
const YOUTUBE_ID_PATTERN = /^[A-Za-z0-9_-]{11}$/

const embedUrl = computed<string | null>(() => {
  const id = props.target?.youtubeVideoId ?? ''
  if (!YOUTUBE_ID_PATTERN.test(id)) {
    return null
  }

  // Built only from the provider id, never from a caller-supplied URL.
  return `https://www.youtube.com/embed/${id}?autoplay=1&rel=0`
})

const unavailable = computed(() => props.target !== null && embedUrl.value === null)
</script>

<template>
  <AppDialog
    :open="open"
    :labelled-by="dialogTitleId"
    variant="video-preview"
    @close="emit('close')"
  >
    <template #header>
      <div class="shorts-preview__heading">
        <h2 :id="dialogTitleId" class="shorts-preview__title">Watch Short</h2>
        <p v-if="target" class="shorts-preview__context">
          <span class="shorts-preview__video-title" :title="target.title">{{ target.title }}</span>
          <span class="shorts-preview__channel">{{ target.channelName }}</span>
        </p>
      </div>
    </template>

    <div class="shorts-preview__stage">
      <div v-if="embedUrl" class="shorts-preview__frame">
        <iframe
          class="shorts-preview__player"
          :src="embedUrl"
          :title="target ? `${target.title} — Shorts player` : 'Shorts player'"
          allow="accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture; web-share"
          allowfullscreen
        ></iframe>
      </div>

      <p v-else-if="unavailable" class="shorts-preview__unavailable" role="status">
        This Short cannot be played here. Use the YouTube link in the row instead.
      </p>
    </div>
  </AppDialog>
</template>

<style lang="scss">
@use '@/app/videos/styles/shorts-preview-dialog';
</style>
