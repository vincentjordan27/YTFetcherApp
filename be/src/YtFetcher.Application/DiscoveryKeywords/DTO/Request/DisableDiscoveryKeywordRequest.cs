namespace YtFetcher.Application.DiscoveryKeywords.DTO.Request;

/// <summary>
/// Request data for disabling one discovery keyword. The action takes no input, but a non-GET action
/// still carries its own body type so the transport never reuses another action's contract.
/// </summary>
public sealed record DisableDiscoveryKeywordRequest;
