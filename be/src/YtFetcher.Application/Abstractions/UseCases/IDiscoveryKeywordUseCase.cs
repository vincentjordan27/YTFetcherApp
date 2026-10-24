using YtFetcher.Application.DiscoveryKeywords.DTO.Request;
using YtFetcher.Application.DiscoveryKeywords.DTO.Response;

namespace YtFetcher.Application.Abstractions.UseCases;

/// <summary>
/// Application use-case boundary for live discovery keyword management. These operations never call
/// YouTube and never create discovery state.
/// </summary>
public interface IDiscoveryKeywordUseCase
{
    /// <summary>Reads one page of keywords with the database-wide active keyword count.</summary>
    Task<DiscoveryKeywordsResponse> GetPageAsync(
        DiscoveryKeywordsListRequest request,
        CancellationToken cancellationToken);

    /// <summary>Creates one active keyword, or reports an existing duplicate.</summary>
    Task<DiscoveryKeywordMutationResponse> CreateAsync(
        CreateDiscoveryKeywordRequest request,
        CancellationToken cancellationToken);

    /// <summary>Excludes one existing keyword from future discovery selection.</summary>
    Task<DiscoveryKeywordMutationResponse> DisableAsync(
        Guid keywordId,
        CancellationToken cancellationToken);

    /// <summary>Returns one existing keyword to future discovery selection.</summary>
    Task<DiscoveryKeywordMutationResponse> EnableAsync(
        Guid keywordId,
        CancellationToken cancellationToken);

    /// <summary>Soft deletes one existing keyword. A deleted keyword is never revived.</summary>
    Task<DiscoveryKeywordMutationResponse> RemoveAsync(
        Guid keywordId,
        CancellationToken cancellationToken);
}
