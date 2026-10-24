/** One keyword ledger row as the approved table renders it. */
export interface KeywordListItem {
  id: string
  term: string
  active: boolean
  /** Already localized for display, for example `12 Jun 2026, 09:15`. */
  createdAt: string
  /** Already localized for display. */
  updatedAt: string
}

/** The keyword dialog that is open, or `null` when none is. */
export type KeywordDialogKind = 'add' | 'disable' | 'remove' | null

export interface KeywordPagination {
  readonly summary: string
  readonly pageCount: number
  readonly displayPages: readonly (number | 'ellipsis')[]
}

/** The single state the table body renders, so loading, empty, and failure are mutually exclusive. */
export type KeywordTableState =
  | { readonly state: 'loading'; readonly message: string }
  | { readonly state: 'error'; readonly message: string }
  | { readonly state: 'empty'; readonly message: string }
  | { readonly state: 'ready'; readonly message: '' }
