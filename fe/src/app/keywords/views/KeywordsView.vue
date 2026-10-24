<script setup lang="ts">
import AdminShell from '@/common/layouts/AdminShell.vue'
import KeywordDialog from '@/app/keywords/components/KeywordDialog.vue'
import KeywordTable from '@/app/keywords/components/KeywordTable.vue'
import { useKeywordList } from '@/app/keywords/composables/useKeywordList'
import '@/app/keywords/styles/keywords.scss'

const keywordList = useKeywordList()
</script>

<template>
  <AdminShell>
    <div class="keywords-page">
      <h1 class="keywords-page__title">Discovery keywords</h1>
      <div class="keywords-page__main">
        <div class="keywords-page__container">
          <KeywordTable
            :keywords="keywordList.rows.value"
            :active-count-label="keywordList.activeCountLabel.value"
            :search-query="keywordList.searchInput.value"
            :items-per-page="String(keywordList.itemsPerPage.value)"
            :page="keywordList.currentPage.value"
            :pagination="keywordList.pagination.value"
            :table-state="keywordList.tableState.value"
            :pending-keyword-id="keywordList.pendingKeywordId.value"
            @update:search-query="keywordList.updateSearchInput"
            @update:items-per-page="keywordList.setItemsPerPage"
            @update:page="keywordList.goToPage"
            @add="keywordList.openAddDialog"
            @refresh="keywordList.refresh"
            @disable="keywordList.requestDisable"
            @enable="keywordList.enableKeyword"
            @remove="keywordList.requestRemove"
          />

          <KeywordDialog
            :kind="keywordList.dialogKind.value"
            :keyword="keywordList.dialogKeyword.value"
            :term="keywordList.newKeywordTerm.value"
            :is-submitting="keywordList.isMutating.value"
            :mutation-error="keywordList.mutationError.value"
            :can-submit="keywordList.canSubmitKeyword.value"
            @update:term="keywordList.updateNewKeywordTerm"
            @close="keywordList.closeDialog"
            @add="keywordList.confirmAdd"
            @disable="keywordList.confirmDisable"
            @remove="keywordList.confirmRemove"
          />
        </div>
      </div>
    </div>
  </AdminShell>
</template>
