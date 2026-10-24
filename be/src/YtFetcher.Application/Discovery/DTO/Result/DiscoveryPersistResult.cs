namespace YtFetcher.Application.Discovery.DTO.Result;

/// <summary>Persistence counts for one keyword page.</summary>
public sealed record DiscoveryPersistResult(
    int ChannelsCreated,
    int ChannelsRefreshed,
    int SoftDeletedChannelsIgnored,
    int ProvenanceRowsCreated,
    int DuplicateProvenanceRows);
