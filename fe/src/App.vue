<script setup lang="ts">
import { RouterView } from 'vue-router'
import AppLoading from '@/common/components/AppLoading.vue'
import AppToast from '@/common/components/AppToast.vue'
</script>

<template>
  <RouterView v-slot="{ Component }">
    <Suspense>
      <component :is="Component" v-if="Component" />
      <template #fallback>
        <AppLoading />
      </template>
    </Suspense>
  </RouterView>
  <AppToast />
</template>
