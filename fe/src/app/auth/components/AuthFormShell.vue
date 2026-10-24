<script setup lang="ts">
import logoUrl from '@/common/assets/yt-fetcher-logo.png'
import { authCopy } from '../data/auth-copy'
</script>

<template>
  <main class="auth-page auth-page--login">
    <section class="auth-card auth-card--login" aria-labelledby="auth-product-name">
      <header class="auth-card__header auth-card__header--login">
        <img :src="logoUrl" alt="" class="auth-card__logo auth-card__logo--inline" width="32" height="32" />
        <h1 id="auth-product-name" class="auth-card__brand">{{ authCopy.productName }}</h1>
      </header>
      <slot />
    </section>
  </main>
</template>

<style lang="scss" src="../styles/_auth-form-shell.scss"></style>
