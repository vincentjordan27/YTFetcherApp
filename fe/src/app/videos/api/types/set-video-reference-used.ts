/** Wire body for setting the current Used/Unused state of one Reference video. */
export interface SetVideoReferenceUsedBody {
  readonly videoId: string
  readonly isUsed: boolean
}
