import type { VideoReferenceStatus } from '@/app/videos/types/video-reference-status'

/** Wire body for changing a Reference video's current curation status. */
export interface SetVideoReferenceStatusBody {
  readonly videoId: string
  readonly referenceStatus: Exclude<VideoReferenceStatus, 'NONE'>
}
