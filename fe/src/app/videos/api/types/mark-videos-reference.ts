/** Wire contract for the mark-reference action `POST /api/videos/mark-reference`. */
export interface MarkVideosReferenceBody {
  readonly videoIds: readonly string[]
}
