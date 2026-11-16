using YtFetcher.Core.Videos;

namespace YtFetcher.Application.Videos.DTO.Request;

/// <summary>Changes the status of exactly one video that is still in Reference.</summary>
public sealed record SetVideoReferenceStatusRequest(Guid VideoId, VideoReferenceStatus? ReferenceStatus);
