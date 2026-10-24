/**
 * The minimum a video row must expose for the in-app watch popup: the provider id that is played plus the
 * already-loaded display context. The popup never asks the API for anything.
 */
export interface ShortsPreviewTarget {
  readonly youtubeVideoId: string
  readonly title: string
  readonly channelName: string
}
