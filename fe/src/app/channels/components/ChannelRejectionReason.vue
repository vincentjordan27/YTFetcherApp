<script setup lang="ts">
defineProps<{ title?: string }>()

const emit = defineEmits<{ open: [] }>()
</script>

<template>
  <button type="button" class="channel-rejection-reason" :title="title" @click="emit('open')">
    <strong>{{ title }}</strong>
  </button>
</template>

<style lang="scss" scoped>
@use '@/app/channels/styles/channel-rejection-reason';
</style>
