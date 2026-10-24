using YtFetcher.Core.Discovery;

namespace YtFetcher.Application.DiscoveryKeywords.DTO.Response;

/// <summary>One discovery keyword row as shown by the keyword page and by a keyword mutation.</summary>
public sealed record DiscoveryKeywordDto(
    Guid Id,
    string Keyword,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset ModifiedAt)
{
    public static DiscoveryKeywordDto ToDto(DiscoveryKeyword keyword) => new(
        keyword.Id,
        keyword.Keyword,
        keyword.IsActive,
        keyword.CreatedAt,
        keyword.ModifiedAt);
}
