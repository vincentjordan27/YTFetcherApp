/** Wire contract for the manual add action `POST /api/channels/manual-approve`. */

/** JSON body of the manual add action. The handle is validated by the server. */
export interface ManualApproveChannelBody {
  readonly channelReference: string
}
