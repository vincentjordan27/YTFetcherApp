<script setup lang="ts">
import { ref } from 'vue'
import AppDataTable from '@/common/components/AppDataTable.vue'
import AppPagination from '@/common/components/AppPagination.vue'
import AppTabs, { type AppTabItem } from '@/common/components/AppTabs.vue'
import ActiveVideoStateRow from '@/app/videos/components/ActiveVideoStateRow.vue'
import VideoRowActionMenu from '@/app/videos/components/VideoRowActionMenu.vue'
import { toAriaSort, toVideoSortAriaLabel } from '@/app/videos/data/video-sort.mapper'
import type {
  ReferenceVideoPagination,
  ReferenceVideoRow,
  ReferenceVideoTableState,
} from '@/app/videos/types/reference-video.types'
import type { VideoSort, VideoSortField } from '@/app/videos/types/video-sort.types'

const props = defineProps<{
  tabs: readonly AppTabItem[]
  searchQuery: string
  rowsPerPage: number
  currentPage: number
  pagination: ReferenceVideoPagination
  rows: readonly ReferenceVideoRow[]
  state: ReferenceVideoTableState
  stateMessage: string
  sort: VideoSort
  sortLabel: string
  removePending: boolean
}>()

const emit = defineEmits<{
  'update:search-query': [value: string]
  'update:rows-per-page': [value: string]
  'update:page': [page: number]
  retry: []
  'open-history': [row: ReferenceVideoRow]
  'open-preview': [row: ReferenceVideoRow]
  'remove-reference': [row: ReferenceVideoRow]
  'set-sort': [field: VideoSortField]
}>()

/** Thumbnail, title, views, likes, published, age, last update, status, row actions. */
const COLUMN_COUNT = 9

const showRows = () => props.state === 'ready'

// Floating thumbnail preview: rendered in a teleport so the table scroll container cannot clip it.
const hoveredRow = ref<ReferenceVideoRow | null>(null)
const hoverStyle = ref<Record<string, string>>({})

function showPreview(row: ReferenceVideoRow, event: MouseEvent): void {
  if (row.thumbnailSrc === null) return

  const rect = (event.currentTarget as HTMLElement).getBoundingClientRect()
  const width = 152
  let left = rect.right + 8
  if (left + width > window.innerWidth - 8) left = rect.left - width - 8

  hoveredRow.value = row
  hoverStyle.value = {
    left: `${Math.max(8, left)}px`,
    top: `${Math.max(8, rect.top + rect.height / 2)}px`,
  }
}

function hidePreview(): void {
  hoveredRow.value = null
}

function updateRowsPerPage(event: Event): void {
  emit('update:rows-per-page', (event.target as HTMLSelectElement).value)
}

/** The channel link is built from the provider channel id, never from the display name. */
function channelUrl(youtubeChannelId: string): string {
  return `https://www.youtube.com/channel/${youtubeChannelId}`
}
</script>

<template>
  <div class="videos-page">
    <div class="videos-main">
      <div class="videos-container">
        <div class="toolbar">
          <AppTabs :items="tabs" class="toolbar__tabs" />
          <div class="toolbar__controls toolbar__controls--reference">
            <label class="toolbar__search">
              <span class="sr-only">Filter by title or channel</span>
              <svg class="toolbar__search-icon" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
                <path d="M15.5 14h-.79l-.28-.27A6.471 6.471 0 0016 9.5 6.5 6.5 0 109.5 16c1.61 0 3.09-.59 4.23-1.57l.27.28v.79l5 4.99L20.49 19l-4.99-5zm-6 0C7.01 14 5 11.99 5 9.5S7.01 5 9.5 5 14 7.01 14 9.5 11.99 14 9.5 14z" />
              </svg>
              <input
                :value="searchQuery"
                type="search"
                placeholder="Filter by title or channel..."
                class="toolbar__search-input"
                @input="emit('update:search-query', ($event.target as HTMLInputElement).value)"
              />
            </label>
            <div class="toolbar__rank">
              <svg class="toolbar__rank-icon" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
                <path d="M10 18h4v-2h-4v2zM3 6v2h18V6H3zm3 7h12v-2H6v2z" />
              </svg>
              <span>Ranked by:</span><span class="toolbar__rank-value">{{ sortLabel }}</span>
            </div>
          </div>
        </div>

        <section class="table-panel" aria-label="Reference videos">
          <AppDataTable ariaLabel="Reference videos">
            <template #head>
              <tr>
                <th class="video-table__thumbnail-heading">Thumbnail</th>
                <th class="video-table__title-heading" :aria-sort="toAriaSort(props.sort, 'title')">
                  <button type="button" class="video-table__sort" :aria-label="toVideoSortAriaLabel('title')" @click="emit('set-sort', 'title')">
                    Video title &amp; Channel
                    <span v-if="props.sort.field === 'title'" class="video-table__sort-icon" :class="{ 'video-table__sort-icon--desc': props.sort.direction === 'desc' }" aria-hidden="true"></span>
                  </button>
                </th>
                <th class="video-table__numeric-heading" :aria-sort="toAriaSort(props.sort, 'views')">
                  <button type="button" class="video-table__sort" :aria-label="toVideoSortAriaLabel('views')" @click="emit('set-sort', 'views')">
                    Current views
                    <span v-if="props.sort.field === 'views'" class="video-table__sort-icon" :class="{ 'video-table__sort-icon--desc': props.sort.direction === 'desc' }" aria-hidden="true"></span>
                  </button>
                </th>
                <th class="video-table__numeric-heading" :aria-sort="toAriaSort(props.sort, 'likes')">
                  <button type="button" class="video-table__sort" :aria-label="toVideoSortAriaLabel('likes')" @click="emit('set-sort', 'likes')">
                    Likes
                    <span v-if="props.sort.field === 'likes'" class="video-table__sort-icon" :class="{ 'video-table__sort-icon--desc': props.sort.direction === 'desc' }" aria-hidden="true"></span>
                  </button>
                </th>
                <th :aria-sort="toAriaSort(props.sort, 'publishedAt')">
                  <button type="button" class="video-table__sort" :aria-label="toVideoSortAriaLabel('publishedAt')" @click="emit('set-sort', 'publishedAt')">
                    Published
                    <span v-if="props.sort.field === 'publishedAt'" class="video-table__sort-icon" :class="{ 'video-table__sort-icon--desc': props.sort.direction === 'desc' }" aria-hidden="true"></span>
                  </button>
                </th>
                <th>Age</th>
                <th :aria-sort="toAriaSort(props.sort, 'lastUpdatedAt')">
                  <button type="button" class="video-table__sort" :aria-label="toVideoSortAriaLabel('lastUpdatedAt')" @click="emit('set-sort', 'lastUpdatedAt')">
                    Last update
                    <span v-if="props.sort.field === 'lastUpdatedAt'" class="video-table__sort-icon" :class="{ 'video-table__sort-icon--desc': props.sort.direction === 'desc' }" aria-hidden="true"></span>
                  </button>
                </th>
                <th>Status</th>
                <th class="video-table__action-heading"><span class="sr-only">Actions</span></th>
              </tr>
            </template>

            <ActiveVideoStateRow
              v-if="!showRows()"
              :colspan="COLUMN_COUNT"
              :message="stateMessage"
              :state="state"
              @retry="emit('retry')"
            />

            <tr v-for="row in showRows() ? rows : []" :key="row.id" class="row--reference">
              <td class="video-table__thumbnail-cell">
                <button
                  type="button"
                  class="thumb-button"
                  :aria-label="`Play ${row.title}`"
                  @click="emit('open-preview', row)"
                  @mouseenter="showPreview(row, $event)"
                  @mouseleave="hidePreview"
                >
                  <div class="thumb-box">
                  <div class="thumb-wrapper" :class="{ 'thumb-wrapper--empty': row.thumbnailSrc === null, 'thumb-wrapper--dimmed': true }">
                    <div
                      v-if="row.thumbnailSrc"
                      class="thumb-blur-bg"
                      :style="{ backgroundImage: `url('${row.thumbnailSrc}')` }"
                    ></div>
                    <img
                      v-if="row.thumbnailSrc"
                      :src="row.thumbnailSrc"
                      :alt="row.title"
                      class="thumb-shorts-img"
                      :width="28"
                      :height="48"
                      loading="lazy"
                      decoding="async"
                    />
                    <span class="thumb-badge thumb-badge--shorts">
                      <svg viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M15 2L3 14h7v8l11-12h-6z" /></svg>
                      {{ row.durationLabel }}
                    </span>
                  </div>
                  </div>
                </button>
              </td>
              <td>
                <div class="video-cell">
                  <div class="video-cell__title-row">
                    <button type="button" class="video-cell__title" :title="row.title" @click="emit('open-preview', row)">{{ row.title }}</button>
                    <a
                      :href="`https://www.youtube.com/watch?v=${row.youtubeVideoId}`"
                      target="_blank"
                      rel="noopener noreferrer"
                      class="video-cell__yt-link"
                      title="Watch on YouTube"
                    >
                      <svg class="app-icon" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M19 19H5V5h7V3H5c-1.11 0-2 .9-2 2v14c0 1.1.89 2 2 2h14c1.1 0 2-.9 2-2v-7h-2v7zM14 3v2h3.59l-9.83 9.83 1.41 1.41L19 6.41V10h2V3h-7z" /></svg>
                    </a>
                  </div>
                  <div class="video-cell__meta-row">
                    <a
                      v-if="row.youtubeChannelId"
                      :href="channelUrl(row.youtubeChannelId)"
                      target="_blank"
                      rel="noopener noreferrer"
                      class="video-cell__channel-name"
                    >{{ row.channelName }}</a>
                    <span v-else class="video-cell__channel-name">{{ row.channelName }}</span>
                    <span class="video-cell__dot">•</span>
                    <span class="video-cell__id">ID: {{ row.youtubeVideoId }}</span>
                  </div>
                </div>
              </td>
              <td class="video-table__numeric-cell">{{ row.viewsLabel }}</td>
              <td class="video-table__likes-cell">{{ row.likesLabel }}</td>
              <td class="video-table__muted-cell">{{ row.publishedLabel }}</td>
              <td class="video-table__muted-cell">{{ row.ageLabel }}</td>
              <td class="video-table__updated-cell">{{ row.lastStatsFetchedLabel }}</td>
              <td><span class="status-badge status-badge--reference">Reference</span></td>
              <td class="video-table__action-cell">
                <VideoRowActionMenu
                  :label="row.title"
                  removable
                  :busy="removePending"
                  @history="emit('open-history', row)"
                  @remove="emit('remove-reference', row)"
                />
              </td>
            </tr>
          </AppDataTable>

          <footer class="pagination-bar">
            <div class="pagination-bar__info-group">
              <div class="pagination-bar__count">{{ pagination.summary }}</div>
              <div class="pagination-bar__rows-per-page">
                <label for="reference-rows-per-page" class="pagination-bar__rows-label">Rows per page:</label>
                <div class="pagination-bar__select-wrap">
                  <select
                    id="reference-rows-per-page"
                    :value="String(rowsPerPage)"
                    class="pagination-bar__select"
                    @change="updateRowsPerPage"
                  >
                    <option v-if="![10, 25, 50, 100].includes(rowsPerPage)" :value="rowsPerPage">{{ rowsPerPage }}</option>
                    <option value="10">10</option>
                    <option value="25">25</option>
                    <option value="50">50</option>
                    <option value="100">100</option>
                  </select>
                  <svg class="pagination-bar__select-arrow" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M16.59 8.59L12 13.17 7.41 8.59 6 10l6 6 6-6z" /></svg>
                </div>
              </div>
            </div>
            <AppPagination
              :page="currentPage"
              :page-count="pagination.pageCount"
              :display-pages="pagination.displayPages"
              @update:page="emit('update:page', $event)"
            />
          </footer>
        </section>
      </div>
    </div>

    <Teleport to="body">
      <div v-if="hoveredRow" class="thumb-hover-card thumb-hover-card--shorts" :style="hoverStyle">
        <div class="thumb-hover-card__img-box">
          <img
            :src="hoveredRow.thumbnailSrc ?? ''"
            :alt="`${hoveredRow.title} preview`"
            class="thumb-hover-card__img"
            :width="128"
            :height="224"
            decoding="async"
          />
        </div>
        <div class="thumb-hover-card__meta">
          <span class="thumb-hover-card__type thumb-hover-card__type--shorts">
            <svg viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M15 2L3 14h7v8l11-12h-6z" /></svg>
            Shorts (9:16)
          </span>
          <span class="thumb-hover-card__duration">{{ hoveredRow.durationLabel }}</span>
        </div>
      </div>
    </Teleport>
  </div>
</template>

<style lang="scss" scoped>
@use '@/app/videos/styles/videos';
@use '@/app/videos/styles/video-table';
@use '@/app/videos/styles/active-video-table';
</style>
