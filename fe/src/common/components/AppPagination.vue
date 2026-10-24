<script setup lang="ts">
const props = defineProps<{
  page: number
  pageCount: number
  displayPages: readonly (number | 'ellipsis')[]
}>()

const emit = defineEmits<{ 'update:page': [page: number] }>()

function goToPage(page: number): void {
  if (page >= 1 && page <= props.pageCount && page !== props.page) {
    emit('update:page', page)
  }
}
</script>

<template>
  <nav class="app-pagination" aria-label="Pagination">
    <button type="button" class="app-pagination__button" :disabled="page <= 1" aria-label="Previous page" @click="goToPage(page - 1)">
      &lt;
    </button>
    <template v-for="item in displayPages" :key="String(item)">
      <span v-if="item === 'ellipsis'" class="app-pagination__ellipsis" aria-hidden="true">...</span>
      <button
        v-else
        type="button"
        class="app-pagination__button"
        :class="{ 'app-pagination__button--active': item === page }"
        :aria-current="item === page ? 'page' : undefined"
        @click="goToPage(item)"
      >
        {{ item }}
      </button>
    </template>
    <button type="button" class="app-pagination__button" :disabled="page >= pageCount" aria-label="Next page" @click="goToPage(page + 1)">
      &gt;
    </button>
  </nav>
</template>

<style lang="scss" scoped>
@use '@/common/styles/app-pagination';
</style>
