using Microsoft.EntityFrameworkCore;
using YtFetcher.Application.Discovery.DTO.Result;
using YtFetcher.Core.Channels;
using YtFetcher.Core.Discovery;
using YtFetcher.Core.Users;
using YtFetcher.Infrastructure.Persistence.Repositories;
using YtFetcher.Infrastructure.Persistence;

namespace YtFetcher.Api.Tests.Discovery;

public class DiscoveryRepositoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 0, 5, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Older = Now.AddDays(-10);

    [Fact]
    public async Task New_channel_and_provenance_are_created_as_pending()
    {
        var databaseName = Guid.NewGuid().ToString();
        await using var context = CreateContext(databaseName);
        var keyword = AddKeyword(context);
        await context.SaveChangesAsync();
        var repository = new DiscoveryRepository(context);

        var result = await repository.PersistKeywordDiscoveryAsync(
            keyword,
            [Candidate("UC-new", "video-1")],
            "next-token",
            Now,
            CancellationToken.None,
            DateOnly.FromDateTime(Now.UtcDateTime));

        Assert.Equal(1, result.ChannelsCreated);
        Assert.Equal(0, result.ChannelsRefreshed);
        Assert.Equal(0, result.SoftDeletedChannelsIgnored);
        Assert.Equal(1, result.ProvenanceRowsCreated);
        Assert.Equal(0, result.DuplicateProvenanceRows);

        var channel = await context.Channels.SingleAsync();
        Assert.Equal("UC-new", channel.YoutubeChannelId);
        Assert.Equal(ChannelStatus.PENDING, channel.Status);
        Assert.Equal(BackfillStatus.PENDING, channel.BackfillStatus);
        Assert.False(channel.IsDeleted);
        Assert.Null(channel.ApprovedAt);
        Assert.Null(channel.RejectedAt);
        Assert.Null(channel.DeletedAt);
        Assert.Equal(Now, channel.DiscoveredAt);
        Assert.Equal(Now, channel.CreatedAt);
        Assert.Equal(Now, channel.ModifiedAt);
        Assert.Equal(User.SystemUserId, channel.CreatedByUserId);
        Assert.Equal(User.SystemUserId, channel.ModifiedByUserId);

        var discovery = await context.ChannelDiscoveries.SingleAsync();
        Assert.Equal(channel.Id, discovery.ChannelId);
        Assert.Equal(keyword.Id, discovery.KeywordId);
        Assert.Equal("video-1", discovery.YoutubeVideoId);
        Assert.Equal("Video video-1", discovery.VideoTitle);
        Assert.Equal(Now, discovery.DiscoveredAt);
        Assert.Equal(User.SystemUserId, discovery.CreatedByUserId);
        Assert.Equal(User.SystemUserId, discovery.ModifiedByUserId);

        var state = await context.DiscoveryStates.SingleAsync();
        Assert.Equal(keyword.Id, state.KeywordId);
        Assert.Equal("next-token", state.RecentPageToken);
        Assert.Equal(Now, state.LastRecentCompletedAt);
        Assert.Equal(Now, state.ModifiedAt);
        Assert.Equal(User.SystemUserId, state.CreatedByUserId);
        Assert.Equal(User.SystemUserId, state.ModifiedByUserId);
    }

    [Fact]
    public async Task Existing_active_channel_is_refreshed_without_changing_review_state()
    {
        var databaseName = Guid.NewGuid().ToString();
        await using var context = CreateContext(databaseName);
        var keyword = AddKeyword(context);
        var channel = new Channel
        {
            Id = Guid.NewGuid(),
            YoutubeChannelId = "UC-existing",
            Name = "Old name",
            ThumbnailUrl = "https://example.com/old.jpg",
            Status = ChannelStatus.APPROVED,
            ApprovedAt = Older,
            ApprovedByUserId = null,
            BackfillStatus = BackfillStatus.COMPLETED,
            IsDeleted = false,
            DiscoveredAt = Older,
            CreatedAt = Older,
            ModifiedAt = Older
        };
        context.Channels.Add(channel);
        await context.SaveChangesAsync();
        var repository = new DiscoveryRepository(context);

        var result = await repository.PersistKeywordDiscoveryAsync(
            keyword,
            [Candidate("UC-existing", "video-1")],
            null,
            Now,
            CancellationToken.None,
            DateOnly.FromDateTime(Now.UtcDateTime));

        var refreshed = await context.Channels.SingleAsync(stored => stored.Id == channel.Id);
        Assert.Equal("Channel UC-existing", refreshed.Name);
        Assert.Equal("https://example.com/UC-existing.jpg", refreshed.ThumbnailUrl);
        Assert.Equal("UU-UC-existing", refreshed.UploadsPlaylistId);
        Assert.Equal(ChannelStatus.APPROVED, refreshed.Status);
        Assert.Equal(Older, refreshed.ApprovedAt);
        Assert.Equal(BackfillStatus.COMPLETED, refreshed.BackfillStatus);
        Assert.False(refreshed.IsDeleted);
        Assert.Equal(Now, refreshed.ModifiedAt);
        Assert.Equal(User.SystemUserId, refreshed.ModifiedByUserId);

        Assert.Equal(1, result.ChannelsRefreshed);
        Assert.Equal(0, result.ChannelsCreated);
    }

    [Fact]
    public async Task Soft_deleted_channel_is_ignored_and_never_revived()
    {
        var databaseName = Guid.NewGuid().ToString();
        await using var context = CreateContext(databaseName);
        var keyword = AddKeyword(context);
        var channel = new Channel
        {
            Id = Guid.NewGuid(),
            YoutubeChannelId = "UC-deleted",
            Name = "Deleted name",
            Status = ChannelStatus.REJECTED,
            RejectedAt = Older,
            BackfillStatus = BackfillStatus.PENDING,
            IsDeleted = true,
            DeletedAt = Older,
            DiscoveredAt = Older,
            CreatedAt = Older,
            ModifiedAt = Older
        };
        context.Channels.Add(channel);
        await context.SaveChangesAsync();
        var repository = new DiscoveryRepository(context);

        var result = await repository.PersistKeywordDiscoveryAsync(
            keyword,
            [Candidate("UC-deleted", "video-1")],
            null,
            Now,
            CancellationToken.None,
            DateOnly.FromDateTime(Now.UtcDateTime));

        Assert.Equal(1, result.SoftDeletedChannelsIgnored);
        Assert.Equal(0, result.ChannelsCreated);
        Assert.Equal(0, result.ProvenanceRowsCreated);

        var stored = await context.Channels.SingleAsync(candidate => candidate.Id == channel.Id);
        Assert.Equal("Deleted name", stored.Name);
        Assert.Equal(ChannelStatus.REJECTED, stored.Status);
        Assert.True(stored.IsDeleted);
        Assert.Equal(Older, stored.ModifiedAt);
        Assert.Empty(context.ChannelDiscoveries);
    }

    [Fact]
    public async Task Duplicate_provenance_is_ignored()
    {
        var databaseName = Guid.NewGuid().ToString();
        await using var context = CreateContext(databaseName);
        var keyword = AddKeyword(context);
        var channel = new Channel
        {
            Id = Guid.NewGuid(),
            YoutubeChannelId = "UC-duplicate",
            Name = "Duplicate",
            Status = ChannelStatus.PENDING,
            BackfillStatus = BackfillStatus.PENDING,
            IsDeleted = false,
            DiscoveredAt = Older,
            CreatedAt = Older,
            ModifiedAt = Older
        };
        context.Channels.Add(channel);
        await context.SaveChangesAsync();
        var repository = new DiscoveryRepository(context);
        var candidates = new[] { Candidate("UC-duplicate", "video-1") };

        await repository.PersistKeywordDiscoveryAsync(keyword, candidates, null, Now, CancellationToken.None, DateOnly.FromDateTime(Now.UtcDateTime));
        var second = await repository.PersistKeywordDiscoveryAsync(keyword, candidates, null, Now, CancellationToken.None, DateOnly.FromDateTime(Now.UtcDateTime));

        Assert.Equal(0, second.ProvenanceRowsCreated);
        Assert.Equal(1, second.DuplicateProvenanceRows);
        Assert.Equal(1, await context.ChannelDiscoveries.CountAsync());
    }

    [Fact]
    public async Task Video_evidence_already_seen_by_another_keyword_is_not_created_again()
    {
        var databaseName = Guid.NewGuid().ToString();
        await using var context = CreateContext(databaseName);
        var firstKeyword = AddKeyword(context);
        var secondKeyword = AddKeyword(context);
        await context.SaveChangesAsync();
        var repository = new DiscoveryRepository(context);
        var candidates = new[] { Candidate("UC-duplicate", "video-1") };

        await repository.PersistKeywordDiscoveryAsync(firstKeyword, candidates, null, Now, CancellationToken.None, DateOnly.FromDateTime(Now.UtcDateTime));
        var second = await repository.PersistKeywordDiscoveryAsync(secondKeyword, candidates, null, Now, CancellationToken.None, DateOnly.FromDateTime(Now.UtcDateTime));

        Assert.Equal(0, second.ProvenanceRowsCreated);
        Assert.Equal(1, second.DuplicateProvenanceRows);
        Assert.Equal(1, await context.ChannelDiscoveries.CountAsync());
    }

    [Fact]
    public async Task Existing_state_advances_recent_fields_and_preserves_history()
    {
        var databaseName = Guid.NewGuid().ToString();
        await using var context = CreateContext(databaseName);
        var keyword = AddKeyword(context);
        var state = new DiscoveryState
        {
            Id = Guid.NewGuid(),
            KeywordId = keyword.Id,
            RecentPageToken = "old-token",
            HistoryPageToken = "history-token",
            HistoryWindowStart = Older,
            HistoryWindowEnd = Older.AddDays(1),
            ModifiedAt = Older
        };
        context.DiscoveryStates.Add(state);
        await context.SaveChangesAsync();
        var repository = new DiscoveryRepository(context);

        await repository.PersistKeywordDiscoveryAsync(
            keyword,
            [Candidate("UC-state", "video-1")],
            "new-token",
            Now,
            CancellationToken.None,
            DateOnly.FromDateTime(Now.UtcDateTime));

        var stored = await context.DiscoveryStates.SingleAsync();
        Assert.Equal("new-token", stored.RecentPageToken);
        Assert.Equal(Now, stored.LastRecentCompletedAt);
        Assert.Equal(Now, stored.ModifiedAt);
        Assert.Equal("history-token", stored.HistoryPageToken);
        Assert.Equal(Older, stored.HistoryWindowStart);
        Assert.Equal(Older.AddDays(1), stored.HistoryWindowEnd);
    }

    private static YtFetcherDbContext CreateContext(string databaseName) =>
        new(new DbContextOptionsBuilder<YtFetcherDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options);

    private static DiscoveryKeyword AddKeyword(YtFetcherDbContext context)
    {
        var keyword = new DiscoveryKeyword
        {
            Id = Guid.NewGuid(),
            Keyword = $"keyword-{Guid.NewGuid():N}",
            IsActive = true,
            IsDeleted = false,
            CreatedAt = Older,
            ModifiedAt = Older
        };

        context.DiscoveryKeywords.Add(keyword);
        return keyword;
    }

    private static YouTubeChannelCandidate Candidate(string channelId, string videoId) => new(
        channelId,
        $"Channel {channelId}",
        $"https://example.com/{channelId}.jpg",
        $"UU-{channelId}",
        videoId,
        $"Video {videoId}",
        $"https://example.com/{videoId}.jpg",
        Older);
}
