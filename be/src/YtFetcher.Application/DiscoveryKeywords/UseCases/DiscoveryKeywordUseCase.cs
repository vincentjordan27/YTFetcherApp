using YtFetcher.Application.Abstractions.Repositories;
using YtFetcher.Application.Abstractions.Services;
using YtFetcher.Application.Abstractions.UseCases;
using YtFetcher.Application.DiscoveryKeywords.DTO.Request;
using YtFetcher.Application.DiscoveryKeywords.DTO.Response;
using YtFetcher.Core.Discovery;

namespace YtFetcher.Application.DiscoveryKeywords.UseCases;

/// <summary>
/// Live management of the discovery keywords the scheduled discovery job selects from. Every operation
/// touches only the keyword row: no channel, evidence, discovery state, video, YouTube call, or
/// discovery run is created or changed here.
/// </summary>
public sealed class DiscoveryKeywordUseCase(
    IDiscoveryKeywordRepository discoveryKeywordRepository,
    ICurrentActor currentActor,
    TimeProvider timeProvider) : IDiscoveryKeywordUseCase
{
    /// <summary>
    /// The exact operator-facing conflict sentence. It deliberately covers the previously removed case,
    /// because a removed keyword keeps blocking its value instead of being revived.
    /// </summary>
    private const string DuplicateKeywordMessage =
        "This keyword already exists or was previously removed.";

    private const string MissingKeywordMessage = "This keyword no longer exists.";

    private const string BlankKeywordMessage = "Enter a keyword.";

    private const string OverlongKeywordMessage = "A keyword cannot exceed 200 characters.";

    public async Task<DiscoveryKeywordsResponse> GetPageAsync(
        DiscoveryKeywordsListRequest request,
        CancellationToken cancellationToken)
    {
        var keywords = await discoveryKeywordRepository.GetPageAsync(
            request.Page,
            request.PageSize,
            request.SearchText,
            cancellationToken);

        var totalCount = await discoveryKeywordRepository.CountAsync(request.SearchText, cancellationToken);
        var activeCount = await discoveryKeywordRepository.CountActiveAsync(cancellationToken);

        return new DiscoveryKeywordsResponse(
            keywords.Select(DiscoveryKeywordDto.ToDto).ToArray(),
            request.Page,
            request.PageSize,
            totalCount,
            activeCount);
    }

    public async Task<DiscoveryKeywordMutationResponse> CreateAsync(
        CreateDiscoveryKeywordRequest request,
        CancellationToken cancellationToken)
    {
        var keywordValue = request.Keyword?.Trim() ?? string.Empty;
        if (keywordValue.Length == 0)
        {
            return Invalid(BlankKeywordMessage);
        }

        if (keywordValue.Length > DiscoveryKeyword.KeywordMaxLength)
        {
            return Invalid(OverlongKeywordMessage);
        }

        var utcNow = timeProvider.GetUtcNow();

        // The duplicate rule spans active, disabled, and soft-deleted rows, so the check deliberately
        // ignores the soft-delete filter. The unique case-insensitive index remains the real guard, and
        // a lost race is reported as the same duplicate conflict.
        var existing = await discoveryKeywordRepository.FindAnyByKeywordAsync(keywordValue, cancellationToken);
        if (existing is not null)
        {
            return Conflict();
        }

        var keyword = DiscoveryKeyword.Create(keywordValue, utcNow, currentActor.UserId);
        var inserted = await discoveryKeywordRepository.TryAddAsync(keyword, cancellationToken);
        if (!inserted)
        {
            return Conflict();
        }

        return new DiscoveryKeywordMutationResponse(
            DiscoveryKeywordMutationStatus.Succeeded,
            DiscoveryKeywordDto.ToDto(keyword),
            null);
    }

    public Task<DiscoveryKeywordMutationResponse> DisableAsync(
        Guid keywordId,
        CancellationToken cancellationToken) =>
        MutateSelectionAsync(keywordId, isEnabling: false, cancellationToken);

    public Task<DiscoveryKeywordMutationResponse> EnableAsync(
        Guid keywordId,
        CancellationToken cancellationToken) =>
        MutateSelectionAsync(keywordId, isEnabling: true, cancellationToken);

    public async Task<DiscoveryKeywordMutationResponse> RemoveAsync(
        Guid keywordId,
        CancellationToken cancellationToken)
    {
        var keyword = await discoveryKeywordRepository.GetActiveForUpdateAsync(keywordId, cancellationToken);
        if (keyword is null)
        {
            return Missing();
        }

        keyword.SoftDelete(timeProvider.GetUtcNow(), deletedByUserId: currentActor.UserId);
        await discoveryKeywordRepository.SaveChangesAsync(cancellationToken);

        return new DiscoveryKeywordMutationResponse(
            DiscoveryKeywordMutationStatus.Succeeded,
            DiscoveryKeywordDto.ToDto(keyword),
            null);
    }

    /// <summary>
    /// Shared enable/disable path. Repeating the requested selection state is a successful no-op, so a
    /// double click cannot produce a confusing failure; the current row is returned either way.
    /// </summary>
    private async Task<DiscoveryKeywordMutationResponse> MutateSelectionAsync(
        Guid keywordId,
        bool isEnabling,
        CancellationToken cancellationToken)
    {
        var keyword = await discoveryKeywordRepository.GetActiveForUpdateAsync(keywordId, cancellationToken);
        if (keyword is null)
        {
            return Missing();
        }

        var utcNow = timeProvider.GetUtcNow();
        var changed = isEnabling
            ? keyword.Enable(utcNow, currentActor.UserId)
            : keyword.Disable(utcNow, currentActor.UserId);
        if (changed)
        {
            await discoveryKeywordRepository.SaveChangesAsync(cancellationToken);
        }

        return new DiscoveryKeywordMutationResponse(
            DiscoveryKeywordMutationStatus.Succeeded,
            DiscoveryKeywordDto.ToDto(keyword),
            null);
    }

    private static DiscoveryKeywordMutationResponse Conflict() =>
        new(DiscoveryKeywordMutationStatus.Conflict, null, DuplicateKeywordMessage);

    private static DiscoveryKeywordMutationResponse Missing() =>
        new(DiscoveryKeywordMutationStatus.NotFound, null, MissingKeywordMessage);

    private static DiscoveryKeywordMutationResponse Invalid(string message) =>
        new(DiscoveryKeywordMutationStatus.ValidationError, null, message);
}
