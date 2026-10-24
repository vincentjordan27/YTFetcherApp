/** Wire body for the all-or-nothing selected-channel rejection action. */
export interface BulkRejectChannelsBody {
  readonly channelIds: readonly string[]
  readonly reason: string
}
