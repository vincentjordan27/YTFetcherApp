<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import AppDialog from '@/common/components/AppDialog.vue'
import { getGeneralSettings, updateGeneralSettings, type GeneralSettings } from '@/app/settings/api/general-settings-api'

const props = defineProps<{ open: boolean }>()
const emit = defineEmits<{ close: [] }>()
const settings = reactive<GeneralSettings>({ queriesDailyQuotaLimit: 10000, videoBatchGetStatsDailyQuotaLimit: 10000, searchQueriesDailyQuotaLimit: 100, discoveryMaxPagesPerKeywordPerRun: 1, maxDailyApprovedChannels: 100, backfillPageLimit: 3, backfillDispatchBatchSize: 10, minimumSubscriberCount: null })
const loading = ref(false)
const saving = ref(false)
const error = ref('')
const invalid = computed(() =>
  !Number.isInteger(settings.queriesDailyQuotaLimit) || settings.queriesDailyQuotaLimit < 1 ||
  !Number.isInteger(settings.videoBatchGetStatsDailyQuotaLimit) || settings.videoBatchGetStatsDailyQuotaLimit < 1 ||
  !Number.isInteger(settings.searchQueriesDailyQuotaLimit) || settings.searchQueriesDailyQuotaLimit < 1 ||
  !Number.isInteger(settings.discoveryMaxPagesPerKeywordPerRun) || settings.discoveryMaxPagesPerKeywordPerRun < 0 ||
  !Number.isInteger(settings.maxDailyApprovedChannels) || settings.maxDailyApprovedChannels < 1 ||
  !Number.isInteger(settings.backfillPageLimit) || settings.backfillPageLimit < 0 ||
  !Number.isInteger(settings.backfillDispatchBatchSize) || settings.backfillDispatchBatchSize < 1 ||
  (settings.minimumSubscriberCount !== null && (!Number.isInteger(settings.minimumSubscriberCount) || settings.minimumSubscriberCount < 0)),
)

watch(() => props.open, async open => {
  if (!open) return
  loading.value = true; error.value = ''
  try { Object.assign(settings, await getGeneralSettings()) } catch { error.value = 'Unable to load settings.' } finally { loading.value = false }
})

async function save() {
  if (invalid.value) { error.value = 'Check the limits: quota and batch values must be at least 1; page limits and subscriber count cannot be negative.'; return }
  saving.value = true; error.value = ''
  try { Object.assign(settings, await updateGeneralSettings(settings)); emit('close') } catch { error.value = 'Unable to save settings.' } finally { saving.value = false }
}
</script>
<template>
  <AppDialog :open="open" labelled-by="general-settings-title" variant="settings" :busy="saving" @close="emit('close')">
    <template #header>
      <div class="settings-dialog__header">
        <h2 id="general-settings-title" class="settings-dialog__title">General Settings</h2>
      </div>
    </template>
    <p v-if="loading" class="settings-dialog__loading">Loading settings…</p>
    <div v-else class="settings-dialog__fields">
      <section class="settings-dialog__section">
        <h3>YouTube quota</h3>
        <div class="settings-dialog__grid settings-dialog__grid--automation">
          <label v-for="field in [['queriesDailyQuotaLimit','Query quota'],['videoBatchGetStatsDailyQuotaLimit','Video-stat quota'],['searchQueriesDailyQuotaLimit','Search quota']]" :key="field[0]">{{ field[1] }}<input v-model.number="settings[field[0] as keyof GeneralSettings]" type="number" min="1" required></label>
        </div>
      </section>
      <section class="settings-dialog__section">
        <h3>Automation</h3>
        <div class="settings-dialog__grid">
          <label>Discovery pages<input v-model.number="settings.discoveryMaxPagesPerKeywordPerRun" type="number" min="0" required><span>0 = no limit</span></label>
          <label>Daily approved channels<input v-model.number="settings.maxDailyApprovedChannels" type="number" min="1" required></label>
          <label>Backfill page limit<input v-model.number="settings.backfillPageLimit" type="number" min="0" required><span>0 = no limit</span></label>
          <label>Backfill batch size<input v-model.number="settings.backfillDispatchBatchSize" type="number" min="1" required></label>
          <label>Minimum subscriber count<input :value="settings.minimumSubscriberCount ?? ''" type="number" min="0" placeholder="Disabled" @input="settings.minimumSubscriberCount = ($event.target as HTMLInputElement).value === '' ? null : Number(($event.target as HTMLInputElement).value)"><span>Leave empty to disable</span></label>
        </div>
      </section>
      <p v-if="error" class="settings-dialog__error">{{ error }}</p>
    </div>
    <template #actions><button class="settings-dialog__secondary" type="button" @click="emit('close')">Cancel</button><button class="settings-dialog__primary" type="button" :disabled="saving || invalid" @click="save">{{ saving ? 'Saving…' : 'Save changes' }}</button></template>
  </AppDialog>
</template>

<style lang="scss">
@use '@/app/settings/styles/general-settings-dialog';
</style>
