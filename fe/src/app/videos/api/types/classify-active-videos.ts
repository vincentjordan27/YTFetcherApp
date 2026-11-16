import type { VideoReferenceStatus } from '@/app/videos/types/video-reference-status'

export interface ClassifyActiveVideosBody {
  readonly videoIds: readonly string[]
  readonly referenceStatus: Exclude<VideoReferenceStatus, 'NONE' | 'USED'>
}
