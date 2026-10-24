<script setup lang="ts">
defineProps<{ ariaLabel: string }>()
</script>

<template>
  <div class="app-data-table__scroll">
    <table class="app-data-table" :aria-label="ariaLabel">
      <thead>
        <slot name="head" />
      </thead>
      <tbody>
        <slot />
      </tbody>
    </table>
  </div>
</template>

<style lang="scss" scoped>
@use '@/common/styles/app-data-table';
</style>
