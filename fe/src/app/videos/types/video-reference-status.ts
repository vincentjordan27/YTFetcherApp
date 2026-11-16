export type VideoReferenceStatus = 'NONE' | 'UNUSED' | 'USED' | 'SKIPPED'

export type ReferenceStatusFilter = 'ALL' | Exclude<VideoReferenceStatus, 'NONE'>
