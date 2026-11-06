export interface BulkRejectChannelsBody {
  readonly channelIds: readonly string[]
  readonly reason: string
}
