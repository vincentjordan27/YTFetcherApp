namespace YtFetcher.Application.Videos.DTO.Request;

/// <summary>Sets the current Used/Unused flag of exactly one video that is still in Reference.</summary>
public sealed record SetVideoReferenceUsedRequest(Guid VideoId, bool? IsUsed);
