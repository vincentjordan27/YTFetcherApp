using Microsoft.EntityFrameworkCore;
using YtFetcher.Core.Channels;
using YtFetcher.Core.Discovery;
using YtFetcher.Infrastructure.Persistence;
using YtFetcher.Infrastructure.Persistence.Repositories;

namespace YtFetcher.Api.Tests.Channels;

/// <summary>
/// The evidence and keyword reads must return Core entities only, ordered newest first, capped, and
/// resolved with set-based queries.
/// </summary>
public class ChannelEvidenceRepositoryTests
{
    private static readonly DateTimeOffset Base = new(2026, 9, 20, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Evidence_returns_core_entities_newest_first_with_a_stable_id_tiebreak_and_respects_the_limit()
    {
        await using var context = CreateContext();
        var channel = AddChannel(context);
        var keyword = AddKeyword(context, "sakura city", isActive: true, isDeleted: false);
        await context.SaveChangesAsync();

        var pinnedNewest = AddEvidence(context, channel, keyword, "video-newest", Base.AddDays(5));
        var lowerId = AddEvidence(context, channel, keyword, "video-tie-low", Base.AddDays(3));
        var higherId = AddEvidence(context, channel, keyword, "video-tie-high", Base.AddDays(3));
        lowerId.Id = Guid.Parse("00000000-0000-0000-0000-000000000001");
        higherId.Id = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        AddEvidence(context, channel, keyword, "video-oldest", Base.AddDays(1));
        await context.SaveChangesAsync();

        var repository = new ChannelRepository(context);

        var rows = await repository.GetEvidenceAsync(channel.Id, limit: 3, CancellationToken.None);

        Assert.Equal(3, rows.Count);
        Assert.All(rows, row => Assert.IsType<ChannelDiscovery>(row, exactMatch: false));
        Assert.Equal(
            [pinnedNewest.Id, higherId.Id, lowerId.Id],
            rows.Select(row => row.Id));
    }

    [Fact]
    public async Task Evidence_excludes_soft_deleted_rows()
    {
        await using var context = CreateContext();
        var channel = AddChannel(context);
        var keyword = AddKeyword(context, "sakura city", isActive: true, isDeleted: false);
        await context.SaveChangesAsync();

        var kept = AddEvidence(context, channel, keyword, "video-kept", Base.AddDays(2));
        var removed = AddEvidence(context, channel, keyword, "video-removed", Base.AddDays(3));
        removed.IsDeleted = true;
        await context.SaveChangesAsync();

        var repository = new ChannelRepository(context);

        var rows = await repository.GetEvidenceAsync(channel.Id, limit: 5, CancellationToken.None);

        var row = Assert.Single(rows);
        Assert.Equal(kept.Id, row.Id);
    }

    [Fact]
    public async Task Evidence_returns_nothing_for_a_channel_without_evidence()
    {
        await using var context = CreateContext();
        var channel = AddChannel(context);
        await context.SaveChangesAsync();

        var repository = new ChannelRepository(context);

        var rows = await repository.GetEvidenceAsync(channel.Id, limit: 5, CancellationToken.None);

        Assert.Empty(rows);
    }

    [Fact]
    public async Task Keyword_lookup_returns_only_active_non_deleted_rows()
    {
        await using var context = CreateContext();
        var active = AddKeyword(context, "sakura city", isActive: true, isDeleted: false);
        var inactive = AddKeyword(context, "kota sakura", isActive: false, isDeleted: false);
        var deleted = AddKeyword(context, "sakura simulator school", isActive: true, isDeleted: true);
        await context.SaveChangesAsync();

        var repository = new ChannelRepository(context);

        var keywords = await repository.GetDiscoveryKeywordsByIdsAsync(
            [active.Id, inactive.Id, deleted.Id],
            CancellationToken.None);

        var keyword = Assert.Single(keywords.Values);
        Assert.Equal(active.Id, keyword.Id);
        Assert.Equal("sakura city", keyword.Keyword);
    }

    [Fact]
    public async Task Keyword_lookup_returns_an_empty_dictionary_for_empty_input()
    {
        await using var context = CreateContext();
        AddKeyword(context, "sakura city", isActive: true, isDeleted: false);
        await context.SaveChangesAsync();

        var repository = new ChannelRepository(context);

        var keywords = await repository.GetDiscoveryKeywordsByIdsAsync([], CancellationToken.None);

        Assert.Empty(keywords);
    }

    private static YtFetcherDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<YtFetcherDbContext>()
            .UseInMemoryDatabase($"yt-fetcher-evidence-{Guid.NewGuid():N}")
            .Options);

    private static Channel AddChannel(YtFetcherDbContext context)
    {
        var channel = new Channel
        {
            Id = Guid.NewGuid(),
            YoutubeChannelId = $"UC-{Guid.NewGuid():N}",
            Name = "Evidence channel",
            Status = ChannelStatus.PENDING,
            BackfillStatus = BackfillStatus.PENDING,
            IsDeleted = false,
            DiscoveredAt = Base,
            CreatedAt = Base,
            ModifiedAt = Base
        };

        context.Channels.Add(channel);
        return channel;
    }

    private static DiscoveryKeyword AddKeyword(
        YtFetcherDbContext context,
        string value,
        bool isActive,
        bool isDeleted)
    {
        var keyword = new DiscoveryKeyword
        {
            Id = Guid.NewGuid(),
            Keyword = value,
            IsActive = isActive,
            IsDeleted = isDeleted,
            CreatedAt = Base,
            ModifiedAt = Base
        };

        context.DiscoveryKeywords.Add(keyword);
        return keyword;
    }

    private static ChannelDiscovery AddEvidence(
        YtFetcherDbContext context,
        Channel channel,
        DiscoveryKeyword keyword,
        string youtubeVideoId,
        DateTimeOffset publishedAt)
    {
        var discovery = new ChannelDiscovery
        {
            Id = Guid.NewGuid(),
            ChannelId = channel.Id,
            KeywordId = keyword.Id,
            YoutubeVideoId = youtubeVideoId,
            VideoTitle = $"Video {youtubeVideoId}",
            VideoThumbnailUrl = $"https://example.com/{youtubeVideoId}.jpg",
            VideoPublishedAt = publishedAt,
            DiscoveredAt = Base,
            IsDeleted = false,
            CreatedAt = Base,
            ModifiedAt = Base
        };

        context.ChannelDiscoveries.Add(discovery);
        return discovery;
    }
}
