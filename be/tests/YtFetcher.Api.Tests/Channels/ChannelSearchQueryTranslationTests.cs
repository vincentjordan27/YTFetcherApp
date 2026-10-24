using Microsoft.EntityFrameworkCore;
using YtFetcher.Core.Channels;
using YtFetcher.Infrastructure.Persistence;

namespace YtFetcher.Api.Tests.Channels;

/// <summary>
/// The pending search uses portable LINQ (<c>ToLower</c> plus <c>Contains</c>) instead of
/// <c>EF.Functions.ILike</c> so the existing EF InMemory tests stay usable. This guard proves the
/// same predicate also translates to PostgreSQL SQL, which the local end-to-end check relies on.
/// <c>ToLowerInvariant</c> is deliberately not used: the Npgsql provider cannot translate it and the
/// query fails at execution time.
/// </summary>
public class ChannelSearchQueryTranslationTests
{
    [Fact]
    public void Case_insensitive_name_or_channel_id_predicate_translates_to_postgresql()
    {
        var options = new DbContextOptionsBuilder<YtFetcherDbContext>()
            .UseNpgsql("Host=localhost;Database=translation_only;Username=translation_only;Password=translation_only")
            .Options;
        using var context = new YtFetcherDbContext(options);
        const string search = "kyoto";

        var sql = context.Channels
            .Where(channel => channel.StatusValue == nameof(ChannelStatus.PENDING) && !channel.IsDeleted)
            .Where(channel =>
                channel.Name.ToLower().Contains(search) ||
                channel.YoutubeChannelId.ToLower().Contains(search))
            .ToQueryString();

        // Two lowered comparisons: one for name and one for the YouTube channel id.
        Assert.Equal(2, CountOccurrences(sql, "lower("));
        Assert.Contains("channels", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Pending_evidence_count_projection_translates_to_postgresql()
    {
        var options = new DbContextOptionsBuilder<YtFetcherDbContext>()
            .UseNpgsql("Host=localhost;Database=translation_only;Username=translation_only;Password=translation_only")
            .Options;
        using var context = new YtFetcherDbContext(options);

        var sql = context.Channels
            .Where(channel => channel.StatusValue == nameof(ChannelStatus.PENDING) && !channel.IsDeleted)
            .OrderByDescending(channel => channel.DiscoveredAt)
            .ThenByDescending(channel => channel.Id)
            .Select(channel => new
            {
                channel.Id,
                EvidenceCount = context.ChannelDiscoveries.Count(discovery =>
                    discovery.ChannelId == channel.Id && !discovery.IsDeleted)
            })
            .ToQueryString();

        Assert.Contains("channel_discoveries", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("count(", sql, StringComparison.OrdinalIgnoreCase);
    }

    private static int CountOccurrences(string value, string token)
    {
        var count = 0;
        var index = value.IndexOf(token, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            count++;
            index = value.IndexOf(token, index + token.Length, StringComparison.OrdinalIgnoreCase);
        }

        return count;
    }
}
