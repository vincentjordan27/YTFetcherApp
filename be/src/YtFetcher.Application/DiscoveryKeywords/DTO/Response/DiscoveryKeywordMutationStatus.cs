namespace YtFetcher.Application.DiscoveryKeywords.DTO.Response;

/// <summary>Status returned by a discovery keyword mutation.</summary>
public enum DiscoveryKeywordMutationStatus
{
    Succeeded,
    NotFound,
    Conflict,
    ValidationError
}
