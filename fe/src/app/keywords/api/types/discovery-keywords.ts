/** Wire contracts for the discovery keyword endpoints under `/api/discovery-keywords`. */

/** Caller-facing paging input for the list endpoint function. */
export interface DiscoveryKeywordsRequest {
  readonly page: number
  readonly pageSize: number
  readonly query?: string
}

/** JSON body of the paged list action. Filters and sorting can grow here without changing the route. */
export interface DiscoveryKeywordsListBody {
  readonly page: number
  readonly pageSize: number
  readonly searchText: string | null
}

export interface DiscoveryKeywordsResponse {
  readonly items: readonly DiscoveryKeywordItem[]
  readonly page: number
  readonly pageSize: number
  /** Rows matching the current search, used for the pagination range. */
  readonly totalCount: number
  /** Non-deleted active keywords across the whole table, independent of the current page and search. */
  readonly activeCount: number
}

/** One keyword row as returned by the list and by every mutation. */
export interface DiscoveryKeywordItem {
  readonly id: string
  readonly keyword: string
  readonly isActive: boolean
  /** ISO 8601 timestamp with an offset. */
  readonly createdAt: string
  /** ISO 8601 timestamp with an offset. */
  readonly modifiedAt: string
}

export interface CreateDiscoveryKeywordBody {
  readonly keyword: string
}

/** Readable field errors of a `400 application/problem+json` response. */
export interface KeywordValidationProblem {
  readonly errors?: Readonly<Record<string, readonly string[]>>
}

/** Readable message of a `404` or `409 application/problem+json` response. */
export interface KeywordProblem {
  readonly detail?: string
}
