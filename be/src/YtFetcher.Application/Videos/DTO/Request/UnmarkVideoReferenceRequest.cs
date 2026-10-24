namespace YtFetcher.Application.Videos.DTO.Request;

/// <summary>
/// Body of the remove-reference action. It carries exactly one video because the action is per row.
/// </summary>
public sealed record UnmarkVideoReferenceRequest(Guid VideoId);
