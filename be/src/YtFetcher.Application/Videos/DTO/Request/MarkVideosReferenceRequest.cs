namespace YtFetcher.Application.Videos.DTO.Request;

/// <summary>
/// Body of the mark-reference action: the ids of the active videos the operator selected. The
/// transport validates presence, the 1-to-100 bound, GUID shape, and duplicates.
/// </summary>
public sealed record MarkVideosReferenceRequest(IReadOnlyList<Guid>? VideoIds);
