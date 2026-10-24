<script setup lang="ts">
import AppDataTable from '@/common/components/AppDataTable.vue'
import ChannelBackfillStatus from '@/app/channels/components/ChannelBackfillStatus.vue'
import ChannelEvidenceSummary from '@/app/channels/components/ChannelEvidenceSummary.vue'
import PendingChannelEvidence from '@/app/channels/components/PendingChannelEvidence.vue'
import ChannelRejectionReason from '@/app/channels/components/ChannelRejectionReason.vue'
import ChannelReviewActionMenu from '@/app/channels/components/ChannelReviewActionMenu.vue'
import type {
  ChannelDialogKind,
  ChannelEvidenceState,
  ChannelReviewRow,
  ChannelReviewVariant,
  ChannelTableState,
} from '@/app/channels/types/channel-review'
import { computed, ref } from 'vue'

const props = withDefaults(
  defineProps<{
    variant: ChannelReviewVariant
    rows: readonly ChannelReviewRow[]
    expandedEvidenceId?: string | null
    state?: ChannelTableState
    stateMessage?: string
    evidence?: ChannelEvidenceState
    reopeningChannelId?: string | null
    selectedIds?: readonly string[]
    selectionEnabled?: boolean
  }>(),
  {
    expandedEvidenceId: null,
    state: 'ready',
    stateMessage: '',
    evidence: undefined,
    reopeningChannelId: null,
    selectedIds: () => [],
    selectionEnabled: false,
  },
)

const emit = defineEmits<{
  'toggle-evidence': [channelId: string]
  'open-action': [kind: ChannelDialogKind, channel: ChannelReviewRow]
  'open-rejection-reason': [channel: ChannelReviewRow]
  'open-backfill-failure': [channel: ChannelReviewRow]
  reopen: [channel: ChannelReviewRow]
  'toggle-selection': [channelId: string]
  'toggle-visible-selection': []
  retry: []
}>()

const showRows = computed(() => props.state === 'ready')
const selectedIdSet = computed(() => new Set(props.selectedIds))
const allVisibleSelected = computed(
  () => props.rows.length > 0 && props.rows.every(channel => selectedIdSet.value.has(channel.id)),
)
const loadedAvatarIds = ref(new Set<string>())
const failedAvatarIds = ref(new Set<string>())

function isAvatarLoading(channelId: string): boolean {
  return !loadedAvatarIds.value.has(channelId)
}

function markAvatarLoaded(channelId: string): void {
  loadedAvatarIds.value = new Set(loadedAvatarIds.value).add(channelId)
}

function markAvatarFailed(channelId: string): void {
  failedAvatarIds.value = new Set(failedAvatarIds.value).add(channelId)
  markAvatarLoaded(channelId)
}

function hasAvatarFailed(channelId: string): boolean {
  return failedAvatarIds.value.has(channelId)
}

function isSelected(channelId: string): boolean {
  return selectedIdSet.value.has(channelId)
}

function columnCount(): number {
  const base = props.variant === 'pending' ? 7 : props.variant === 'approved' ? 7 : 6
  return props.selectionEnabled ? base + 1 : base
}
</script>

<template>
  <section class="channel-review-table" :class="`channel-review-table--${variant}`" :aria-label="`${variant} channels`">
    <AppDataTable :ariaLabel="`${variant} channels`">
      <template #head>
        <tr>
          <th v-if="selectionEnabled" class="channel-review-table__select-column" scope="col">
            <input
              type="checkbox"
              class="channel-review-table__select-checkbox"
              aria-label="Select all visible channels"
              :checked="allVisibleSelected"
              @change="emit('toggle-visible-selection')"
            />
          </th>
          <th v-if="variant === 'pending'" class="channel-review-table__expand-column" scope="col">
            <span class="sr-only">Discovery evidence</span>
          </th>
          <th scope="col">Channel</th>
          <th scope="col">Status</th>
          <th scope="col">Discovery date</th>
          <th v-if="variant === 'pending'" scope="col">Backfill status</th>
          <th v-else-if="variant === 'approved'" scope="col">Approved date</th>
          <th v-else scope="col">Rejected date</th>
          <th v-if="variant === 'pending'" scope="col">Evidence Shorts</th>
          <template v-else-if="variant === 'approved'">
            <th scope="col">Backfill status</th>
            <th scope="col">Last upload check</th>
          </template>
          <th v-else class="channel-review-table__rejection-reason-column" scope="col">Rejection Reason</th>
          <th class="channel-review-table__actions-column" scope="col">Actions</th>
        </tr>
      </template>

      <tr v-if="!showRows" class="channel-review-state-row">
        <td :colspan="columnCount()" class="channel-review-state-row__cell">
          <span
            class="channel-review-state-row__content"
            :class="{ 'channel-review-state-row__content--busy': state === 'loading' }"
          >
            <span v-if="state === 'loading'" class="channel-review-state-row__indicator" aria-hidden="true" />
            <span class="channel-review-state-row__message">{{ stateMessage }}</span>
            <button
              v-if="state === 'error'"
              type="button"
              class="channel-review-state-row__action"
              @click="emit('retry')"
            >
              Retry
            </button>
          </span>
        </td>
      </tr>

      <template v-for="channel in showRows ? rows : []" :key="channel.id">
        <tr
          class="channel-review-table__row"
          :class="{
            'channel-review-table__row--expandable': variant === 'pending' || variant === 'rejected',
            'channel-review-table__row--expanded': expandedEvidenceId === channel.id,
            'channel-review-table__row--selected': isSelected(channel.id),
          }"
          @click="variant === 'pending' ? emit('toggle-evidence', channel.id) : variant === 'rejected' && emit('open-rejection-reason', channel)"
        >
          <td v-if="selectionEnabled" class="channel-review-table__select-cell" @click.stop>
            <input
              type="checkbox"
              class="channel-review-table__select-checkbox"
              :aria-label="`Select ${channel.name}`"
              :checked="isSelected(channel.id)"
              @change="emit('toggle-selection', channel.id)"
            />
          </td>
          <td v-if="variant === 'pending'" class="channel-review-table__expand-cell">
            <button
              type="button"
              class="channel-review-table__disclosure"
              :aria-expanded="expandedEvidenceId === channel.id"
              :aria-controls="`evidence-${channel.id}`"
              :title="expandedEvidenceId === channel.id ? 'Collapse evidence' : 'Expand evidence'"
              @click.stop="emit('toggle-evidence', channel.id)"
            >
              <svg class="channel-review-table__disclosure-icon" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
                <path
                  v-if="expandedEvidenceId === channel.id"
                  d="M7.41 8.59L12 13.17l4.59-4.58L18 10l-6 6-6-6 1.41-1.41z"
                />
                <path v-else d="M8.59 16.59L13.17 12 8.59 7.41 10 6l6 6-6 6-1.41-1.41z" />
              </svg>
            </button>
          </td>
          <td>
            <div class="channel-review-table__identity">
              <span class="channel-review-table__avatar" :class="{ 'channel-review-table__avatar--loading': channel.avatarSrc && isAvatarLoading(channel.id) }">
                <img
                  v-if="channel.avatarSrc"
                  :src="channel.avatarSrc"
                  :alt="`${channel.name} thumbnail`"
                  width="40"
                  height="40"
                  loading="lazy"
                  decoding="async"
                  referrerpolicy="no-referrer"
                  @load="markAvatarLoaded(channel.id)"
                  @error="markAvatarFailed(channel.id); ($event.target as HTMLImageElement).hidden = true"
                />
                <span v-if="hasAvatarFailed(channel.id)" class="channel-review-table__avatar-fallback" aria-hidden="true">
                  {{ channel.name.slice(0, 1).toUpperCase() }}
                </span>
              </span>
              <span class="channel-review-table__identity-copy">
                <span class="channel-review-table__name-line">
                  <a :href="channel.youtubeUrl" target="_blank" rel="noopener noreferrer" class="channel-review-table__name" :title="`Open ${channel.name} on YouTube`" @click.stop>
                    <span>{{ channel.name }}</span>
                    <svg class="channel-review-table__name-icon" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
                      <path d="M19 19H5V5h7V3H5c-1.11 0-2 .9-2 2v14c0 1.1.89 2 2 2h14c1.1 0 2-.9 2-2v-7h-2v7zM14 3v2h3.59l-9.83 9.83 1.41 1.41L19 6.41V10h2V3h-7z" />
                    </svg>
                  </a>
                  <span v-if="variant === 'rejected'" class="channel-review-table__excluded">Excluded</span>
                </span>
                <span class="channel-review-table__handle">{{ channel.handle }}</span>
              </span>
            </div>
          </td>
          <td><span class="channel-review-table__status" :class="`channel-review-table__status--${variant}`"><span />{{ channel.statusLabel }}</span></td>
          <td class="channel-review-table__date channel-review-table__date--muted">{{ channel.discoveredAt }}</td>
          <td v-if="variant === 'pending'"><ChannelBackfillStatus :label="channel.backfillLabel" tone="pending" /></td>
          <td v-else-if="variant === 'approved'" class="channel-review-table__date">{{ channel.approvedAt }}</td>
          <td v-else class="channel-review-table__date">{{ channel.rejectedAt }}</td>
          <td v-if="variant === 'pending'">
            <ChannelEvidenceSummary :count="channel.evidenceCount ?? 0" :active="expandedEvidenceId === channel.id" />
          </td>
          <template v-else-if="variant === 'approved'">
            <td>
              <ChannelBackfillStatus
                :label="channel.backfillLabel"
                :tone="channel.backfillTone"
                @open-failure="emit('open-backfill-failure', channel)"
              />
            </td>
            <td class="channel-review-table__date channel-review-table__date--muted">{{ channel.lastUploadCheck }}</td>
          </template>
          <td v-else class="channel-review-table__rejection-reason-cell">
            <ChannelRejectionReason :title="channel.reasonTitle" @open="emit('open-rejection-reason', channel)" @click.stop />
          </td>
          <td class="channel-review-table__actions-cell">
            <div class="channel-review-table__actions">
              <ChannelReviewActionMenu
                v-if="variant === 'pending' || variant === 'approved'"
                :label="channel.name"
                :can-approve="variant === 'pending'"
                can-reject
                @click.stop
                @approve="emit('open-action', 'approve', channel)"
                @reject="emit('open-action', 'reject', channel)"
              />
              <button
                v-if="variant === 'rejected'"
                type="button"
                class="channel-review-table__action channel-review-table__action--reopen"
                :disabled="reopeningChannelId === channel.id"
                @click.stop="emit('reopen', channel)"
              >
                <span>{{ reopeningChannelId === channel.id ? 'Reopening...' : 'Reopen' }}</span>
              </button>
            </div>
          </td>
        </tr>
        <PendingChannelEvidence
          v-if="variant === 'pending' && expandedEvidenceId === channel.id"
          :channel-id="channel.id"
          :discovered-at="channel.discoveredAt"
          :evidence-count="channel.evidenceCount ?? 0"
          :items="evidence?.items[channel.id] ?? []"
          :loading="evidence?.loadingId === channel.id"
          :failed="evidence?.failed[channel.id] === true"
        />
      </template>
    </AppDataTable>
  </section>
</template>

<style lang="scss" scoped>
@use '@/app/channels/styles/channel-review-table';
</style>
