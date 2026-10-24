namespace YtFetcher.Application.DiscoveryKeywords.DTO.Response;

/// <summary>Application result of a discovery keyword mutation, interpreted by the transport.</summary>
public sealed record DiscoveryKeywordMutationResponse(
    DiscoveryKeywordMutationStatus Status,
    DiscoveryKeywordDto? Keyword,
    string? Message);
