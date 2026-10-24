using Microsoft.EntityFrameworkCore;
using Npgsql;
using YtFetcher.Application.Abstractions.Repositories;
using YtFetcher.Core.Discovery;
using YtFetcher.Infrastructure.Persistence;

namespace YtFetcher.Infrastructure.Persistence.Repositories;

/// <summary>
/// PostgreSQL discovery keyword store. The duplicate lookup deliberately ignores the soft-delete filter,
/// because a removed keyword keeps blocking its value through the unique case-insensitive index, so the
/// index remains the real concurrency guard and a lost insert race is reported as a duplicate.
/// </summary>
public sealed class DiscoveryKeywordRepository(YtFetcherDbContext dbContext) : IDiscoveryKeywordRepository
{
    /// <summary>PostgreSQL SQLSTATE for a unique-constraint violation.</summary>
    private const string UniqueViolationSqlState = "23505";

    public async Task<IReadOnlyList<DiscoveryKeyword>> GetPageAsync(
        int page,
        int pageSize,
        string? searchText,
        CancellationToken cancellationToken) =>
        await KeywordsForSearch(searchText)
            .OrderByDescending(keyword => keyword.CreatedAt)
            .ThenByDescending(keyword => keyword.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

    public Task<int> CountAsync(string? searchText, CancellationToken cancellationToken) =>
        KeywordsForSearch(searchText).CountAsync(cancellationToken);

    public Task<int> CountActiveAsync(CancellationToken cancellationToken) =>
        dbContext.DiscoveryKeywords
            .AsNoTracking()
            .CountAsync(keyword => !keyword.IsDeleted && keyword.IsActive, cancellationToken);

    public Task<DiscoveryKeyword?> FindAnyByKeywordAsync(
        string keyword,
        CancellationToken cancellationToken)
    {
        // lower(keyword) matches the unique index expression, so the duplicate decision uses the same
        // comparison the database enforces.
        var normalizedKeyword = keyword.Trim().ToLower();

        return dbContext.DiscoveryKeywords
            .AsNoTracking()
            .FirstOrDefaultAsync(
                candidate => candidate.Keyword.ToLower() == normalizedKeyword,
                cancellationToken);
    }

    public Task<DiscoveryKeyword?> GetActiveForUpdateAsync(
        Guid keywordId,
        CancellationToken cancellationToken) =>
        dbContext.DiscoveryKeywords
            .FirstOrDefaultAsync(
                keyword => keyword.Id == keywordId && !keyword.IsDeleted,
                cancellationToken);

    public async Task<bool> TryAddAsync(DiscoveryKeyword keyword, CancellationToken cancellationToken)
    {
        dbContext.DiscoveryKeywords.Add(keyword);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: UniqueViolationSqlState })
        {
            // Only a real unique-constraint violation means another writer created the same keyword first.
            // The entity is detached so the context stays usable for the caller. Every other database
            // failure, such as an outage or a broken query, is not a duplicate and must not be reported as a
            // readable 409.
            dbContext.Entry(keyword).State = EntityState.Detached;
            return false;
        }
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);

    private IQueryable<DiscoveryKeyword> KeywordsForSearch(string? searchText)
    {
        var keywords = dbContext.DiscoveryKeywords
            .AsNoTracking()
            .Where(keyword => !keyword.IsDeleted);

        if (string.IsNullOrEmpty(searchText))
        {
            return keywords;
        }

        // Portable LINQ rather than EF.Functions.ILike so the EF InMemory tests stay usable.
        // ToLower() is used instead of ToLowerInvariant() because the Npgsql provider cannot
        // translate the invariant form.
        var search = searchText.ToLower();

        return keywords.Where(keyword => keyword.Keyword.ToLower().Contains(search));
    }
}
