<script setup lang="ts">
import { useRoute, useRouter } from 'vue-router'
import { useAuthentication } from '../composables/useAuthentication'
import AuthFormShell from '../components/AuthFormShell.vue'
import GoogleSignInPanel from '../components/GoogleSignInPanel.vue'

const router = useRouter()
const route = useRoute()
const {
  bootstrapState,
  isSubmitting,
  errorMessage,
  retryBootstrap,
  signInWithGoogle,
} = useAuthentication()

async function handleSignIn(): Promise<void> {
  if (await signInWithGoogle()) {
    const returnTo = route.query.returnTo
    await router.push(typeof returnTo === 'string' && returnTo.startsWith('/') && !returnTo.startsWith('//')
      ? returnTo
      : '/videos')
  }
}
</script>

<template>
  <AuthFormShell>
    <!-- The Google action is offered only once the backend confirmed that authentication is enabled; every
         other state offers a retry instead of a control that could not work. -->
    <GoogleSignInPanel
      :is-submitting="isSubmitting"
      :error-message="errorMessage"
      :unavailable="bootstrapState !== 'enabled'"
      @sign-in="handleSignIn"
      @retry="retryBootstrap"
    />
  </AuthFormShell>
</template>
