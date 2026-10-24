using YtFetcher.Core.Discovery;

namespace YtFetcher.Application.Abstractions.Repositories;

/// <summary>
/// Persistence boundary for discovery keyword management. Reads return Core entities or scalars; the
/// Application use case maps them to response data. Every normal read excludes soft-deleted rows, and
/// the duplicate check deliberately does not, because a removed keyword must still block its keyword.
/// </summary>
public interface IDiscoveryKeywordRepository
{
    /// <summary>
    /// Fetches one page of non-deleted keywords, newest first, applying the query's page, page size, and
    /// optional case-insensitive substring search.
    /// </summary>
    Task<IReadOnlyList<DiscoveryKeyword>> GetPageAsync(
        int page,
        int pageSize,
        string? searchText,
        CancellationToken cancellationToken);

    /// <summary>Counts the non-deleted keywords that match the optional search text.</summary>
    Task<int> CountAsync(string? searchText, CancellationToken cancellationToken);

    /// <summary>
    /// Counts every non-deleted active keyword in one query. The count is database-wide and is
    /// independent of the current page and search text.
    /// </summary>
    Task<int> CountActiveAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Finds a keyword by its persisted value, ignoring case and ignoring the soft-delete filter, so the
    /// duplicate policy can refuse an active, disabled, or previously removed keyword.
    /// </summary>
    Task<DiscoveryKeyword?> FindAnyByKeywordAsync(string keyword, CancellationToken cancellationToken);

    /// <summary>Fetches a non-deleted tracked keyword so a domain mutation can be persisted.</summary>
    Task<DiscoveryKeyword?> GetActiveForUpdateAsync(Guid keywordId, CancellationToken cancellationToken);

    /// <summary>
    /// Inserts <paramref name="keyword"/> in one short transaction. The unique case-insensitive keyword
    /// constraint lets exactly one of two simultaneous creates commit; the loser receives
    /// <c>false</c> and must treat the keyword as an existing duplicate.
    /// </summary>
    Task<bool> TryAddAsync(DiscoveryKeyword keyword, CancellationToken cancellationToken);

    /// <summary>Persists changes already made to a tracked keyword entity.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
