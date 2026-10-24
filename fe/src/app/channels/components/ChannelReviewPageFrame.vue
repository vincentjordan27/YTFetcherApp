<script setup lang="ts">
defineProps<{ title: string; status: 'pending' | 'approved' | 'rejected' }>()
</script>

<template>
  <section :class="`${status}-channels`">
    <h1 :class="`${status}-channels__title`">{{ title }}</h1>
    <div :class="`${status}-channels__container`"><slot /></div>
  </section>
</template>

<style lang="scss" scoped>
@use '@/app/channels/styles/channels';
</style>
