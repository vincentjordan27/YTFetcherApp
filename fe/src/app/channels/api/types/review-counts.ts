/** Wire contract for the navigation summary `GET /api/channels/review-counts`. */

export interface ReviewCountsResponse {
  readonly pending: number
  readonly approved: number
  readonly rejected: number
}
