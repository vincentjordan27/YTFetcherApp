<script setup lang="ts">
import AppSidebar from '@/common/components/AppSidebar.vue'
</script>

<template>
  <div class="admin-shell">
    <AppSidebar />
    <main class="admin-shell__content"><slot /></main>
  </div>
</template>

<style lang="scss" scoped>
@use '@/common/styles/admin-shell';
</style>
