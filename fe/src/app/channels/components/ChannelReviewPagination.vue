<script setup lang="ts">
import AppPagination from '@/common/components/AppPagination.vue'
import type { ChannelPaginationDesign } from '@/app/channels/types/channel-review'

defineProps<{ pagination: ChannelPaginationDesign; page: number; rowsPerPage: string }>()
const emit = defineEmits<{ 'update:page': [value: number]; 'update:rowsPerPage': [value: string] }>()
</script>

<template>
  <footer class="channel-review-pagination">
    <div class="channel-review-pagination__meta">
      <span>{{ pagination.summary }}</span>
      <label>
        <span>Rows per page:</span>
        <select :value="rowsPerPage" @change="emit('update:rowsPerPage', ($event.target as HTMLSelectElement).value)">
          <option v-if="![10, 25, 50, 100].includes(Number(rowsPerPage))" :value="rowsPerPage">{{ rowsPerPage }}</option>
          <option value="10">10</option><option value="25">25</option><option value="50">50</option><option value="100">100</option>
        </select>
      </label>
    </div>
    <AppPagination :page="page" :page-count="pagination.pageCount" :display-pages="pagination.displayPages" @update:page="emit('update:page', $event)" />
  </footer>
</template>

<style lang="scss" scoped>
@use '@/app/channels/styles/channel-review-pagination';
</style>
