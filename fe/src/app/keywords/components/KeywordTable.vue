<script setup lang="ts">
import AppDataTable from '@/common/components/AppDataTable.vue'
import AppPagination from '@/common/components/AppPagination.vue'
import type {
  KeywordListItem,
  KeywordPagination,
  KeywordTableState,
} from '@/app/keywords/types/keyword.types'

const emit = defineEmits<{
  'update:searchQuery': [value: string]
  'update:itemsPerPage': [value: string]
  'update:page': [value: number]
  add: []
  refresh: []
  disable: [keyword: KeywordListItem]
  enable: [keyword: KeywordListItem]
  remove: [keyword: KeywordListItem]
}>()

const props = defineProps<{
  keywords: readonly KeywordListItem[]
  activeCountLabel: string
  searchQuery: string
  itemsPerPage: string
  page: number
  pagination: KeywordPagination
  tableState: KeywordTableState
  pendingKeywordId: string | null
}>()

function isPending(keyword: KeywordListItem): boolean {
  return props.pendingKeywordId === keyword.id
}
</script>

<template>
  <section class="keywords-page__ledger" aria-label="Discovery keywords">
    <div class="keywords-page__toolbar">
      <div class="keywords-page__search">
        <svg class="keywords-page__search-icon" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
          <path d="M15.5 14h-.79l-.28-.27a6.5 6.5 0 001.48-5.34c-.47-2.78-2.79-5-5.59-5.34a6.505 6.505 0 00-7.27 7.27c.34 2.8 2.56 5.12 5.34 5.59a6.5 6.5 0 005.34-1.48l.27.28v.79l4.25 4.25c.41.41 1.08.41 1.49 0 .41-.41.41-1.08 0-1.49L15.5 14zm-6 0C7.01 14 5 11.99 5 9.5S7.01 5 9.5 5 14 7.01 14 9.5 11.99 14 9.5 14z" />
        </svg>
        <input
          :value="searchQuery"
          class="keywords-page__search-input"
          type="search"
          placeholder="Search keywords..."
          aria-label="Search keywords"
          @input="emit('update:searchQuery', ($event.target as HTMLInputElement).value)"
        />
      </div>

      <div class="keywords-page__toolbar-actions">
        <span class="keywords-page__active-count">{{ activeCountLabel }}</span>
        <div class="keywords-page__divider" />
        <div class="keywords-page__icon-actions">
          <button
            type="button"
            class="keywords-page__icon-btn"
            title="Refresh table"
            aria-label="Refresh table"
            @click="emit('refresh')"
          >
            <svg class="keyword-icon" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M12 4V1L8 5l4 4V6c3.31 0 6 2.69 6 6 0 1.01-.25 1.97-.7 2.8l1.46 1.46A7.93 7.93 0 0020 12c0-4.42-3.58-8-8-8zm0 14c-3.31 0-6-2.69-6-6 0-1.01.25-1.97.7-2.8L5.24 7.74A7.93 7.93 0 004 12c0 4.42 3.58 8 8 8v3l4-4-4-4v3z" /></svg>
          </button>
        </div>
        <div class="keywords-page__divider" />
        <button type="button" class="keywords-page__btn-add" @click="emit('add')">
          <svg class="keyword-icon" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M19 13h-6v6h-2v-6H5v-2h6V5h2v6h6v2z" /></svg>
          <span>Add keyword</span>
        </button>
      </div>
    </div>

    <AppDataTable ariaLabel="Discovery keywords">
      <template #head>
        <tr>
          <th scope="col">Keyword</th>
          <th class="keywords-page__col-status" scope="col">Status</th>
          <th class="keywords-page__col-date" scope="col">Created date</th>
          <th class="keywords-page__col-date" scope="col">Updated date</th>
          <th class="keywords-page__col-actions" scope="col">Actions</th>
        </tr>
      </template>

      <tr v-if="tableState.state !== 'ready'">
        <td class="keywords-page__cell-state" colspan="5">
          <span class="keywords-page__state-message">{{ tableState.message }}</span>
          <button
            v-if="tableState.state === 'error'"
            type="button"
            class="keywords-page__btn-toggle"
            @click="emit('refresh')"
          >
            Try again
          </button>
        </td>
      </tr>

      <tr
        v-for="keyword in keywords"
        v-else
        :key="keyword.id"
        class="keywords-page__row"
        :class="{ 'keywords-page__row--disabled': !keyword.active }"
      >
        <td class="keywords-page__cell-keyword">
          <svg class="keywords-page__tag-icon" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M20 10V8h-4V4h-2v4h-4V4H8v4H4v2h4v4H4v2h4v4h2v-4h4v4h2v-4h4v-2h-4v-4h4zm-6 4h-4v-4h4v4z" /></svg>
          <span class="keywords-page__keyword" :class="{ 'keywords-page__keyword--disabled': !keyword.active }">{{ keyword.term }}</span>
        </td>
        <td>
          <span class="keywords-page__status-pill" :class="keyword.active ? 'keywords-page__status-pill--active' : 'keywords-page__status-pill--disabled'">
            <span class="keywords-page__status-dot" />{{ keyword.active ? 'Active' : 'Disabled' }}
          </span>
        </td>
        <td class="keywords-page__cell-date">{{ keyword.createdAt }}</td>
        <td class="keywords-page__cell-date">{{ keyword.updatedAt }}</td>
        <td class="keywords-page__cell-actions">
          <div class="keywords-page__actions">
            <button
              v-if="keyword.active"
              type="button"
              class="keywords-page__btn-toggle"
              :disabled="isPending(keyword)"
              @click="emit('disable', keyword)"
            >
              <svg class="keyword-icon-sm keywords-page__toggle-on" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M17 7H7c-2.76 0-5 2.24-5 5s2.24 5 5 5h10c2.76 0 5-2.24 5-5s-2.24-5-5-5zm0 8c-1.66 0-3-1.34-3-3s1.34-3 3-3 3 1.34 3 3-1.34 3-3 3z" /></svg>
              <span>Disable</span>
            </button>
            <button
              v-else
              type="button"
              class="keywords-page__btn-toggle"
              :disabled="isPending(keyword)"
              @click="emit('enable', keyword)"
            >
              <svg class="keyword-icon-sm" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M17 7H7c-2.76 0-5 2.24-5 5s2.24 5 5 5h10c2.76 0 5-2.24 5-5s-2.24-5-5-5zM7 15c-1.66 0-3-1.34-3-3s1.34-3 3-3 3 1.34 3 3-1.34 3-3 3z" /></svg>
              <span>Enable</span>
            </button>
            <button
              type="button"
              class="keywords-page__btn-delete"
              :disabled="isPending(keyword)"
              @click="emit('remove', keyword)"
            >
              <svg class="keyword-icon-sm" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M6 19c0 1.1.9 2 2 2h8c1.1 0 2-.9 2-2V7H6v12zM19 4h-3.5l-1-1h-5l-1 1H5v2h14V4z" /></svg>
              <span>Remove keyword</span>
            </button>
          </div>
        </td>
      </tr>
    </AppDataTable>

    <div class="keywords-page__pagination">
      <div class="keywords-page__pagination-meta">
        <span class="keywords-page__pagination-summary">{{ pagination.summary }}</span>
        <label class="keywords-page__page-size" for="keywords-items-per-page">
          <span>Items per page:</span>
          <select id="keywords-items-per-page" :value="itemsPerPage" class="keywords-page__page-size-select" @change="emit('update:itemsPerPage', ($event.target as HTMLSelectElement).value)">
            <option value="10">10</option><option value="25">25</option><option value="50">50</option><option value="100">100</option>
          </select>
        </label>
      </div>
      <AppPagination :page="page" :page-count="pagination.pageCount" :display-pages="pagination.displayPages" @update:page="emit('update:page', $event)" />
    </div>
  </section>
</template>

<style lang="scss" scoped>
@use '../styles/keyword-table';
</style>
