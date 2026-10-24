using YtFetcher.Application.Abstractions.Repositories;
using YtFetcher.Core.Discovery;

namespace YtFetcher.Api.Tests.DiscoveryKeywords;

/// <summary>
/// In-memory discovery keyword store that mirrors the real repository's filtering rules: normal reads
/// exclude soft-deleted rows, and the duplicate lookup deliberately does not, because the unique
/// case-insensitive index also covers removed keywords.
/// </summary>
public sealed class InMemoryDiscoveryKeywordRepository : IDiscoveryKeywordRepository
{
    private readonly List<DiscoveryKeyword> keywords = [];

    public IReadOnlyList<DiscoveryKeyword> All => keywords;

    public void Seed(params DiscoveryKeyword[] seededKeywords) => keywords.AddRange(seededKeywords);

    public Task<IReadOnlyList<DiscoveryKeyword>> GetPageAsync(
        int page,
        int pageSize,
        string? searchText,
        CancellationToken cancellationToken)
    {
        var matches = keywords
            .Where(keyword => !keyword.IsDeleted && MatchesSearch(keyword, searchText))
            .OrderByDescending(keyword => keyword.CreatedAt)
            .ThenByDescending(keyword => keyword.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return Task.FromResult<IReadOnlyList<DiscoveryKeyword>>(matches);
    }

    public Task<int> CountAsync(string? searchText, CancellationToken cancellationToken) =>
        Task.FromResult(keywords.Count(keyword => !keyword.IsDeleted && MatchesSearch(keyword, searchText)));

    public Task<int> CountActiveAsync(CancellationToken cancellationToken) =>
        Task.FromResult(keywords.Count(keyword => !keyword.IsDeleted && keyword.IsActive));

    public Task<DiscoveryKeyword?> FindAnyByKeywordAsync(string keyword, CancellationToken cancellationToken) =>
        Task.FromResult(keywords.FirstOrDefault(candidate => candidate.MatchesKeyword(keyword)));

    public Task<DiscoveryKeyword?> GetActiveForUpdateAsync(Guid keywordId, CancellationToken cancellationToken) =>
        Task.FromResult(keywords.FirstOrDefault(keyword => keyword.Id == keywordId && !keyword.IsDeleted));

    public Task<bool> TryAddAsync(DiscoveryKeyword keyword, CancellationToken cancellationToken)
    {
        if (keywords.Any(existing => existing.MatchesKeyword(keyword.Keyword)))
        {
            return Task.FromResult(false);
        }

        keywords.Add(keyword);
        return Task.FromResult(true);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static bool MatchesSearch(DiscoveryKeyword keyword, string? searchText) =>
        string.IsNullOrWhiteSpace(searchText) ||
        keyword.Keyword.Contains(searchText.Trim(), StringComparison.OrdinalIgnoreCase);
}
