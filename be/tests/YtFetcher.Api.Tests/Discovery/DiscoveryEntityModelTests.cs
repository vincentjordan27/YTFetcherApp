using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using YtFetcher.Core.Discovery;
using YtFetcher.Infrastructure.Persistence;

namespace YtFetcher.Api.Tests.Discovery;

public class DiscoveryEntityModelTests
{
    private static readonly IModel Model = BuildModel();

    private static IModel BuildModel()
    {
        var options = new DbContextOptionsBuilder<YtFetcherDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=model_only;Password=model_only")
            .Options;

        using var context = new YtFetcherDbContext(options);
        return context.Model;
    }

    private static IEntityType Entity<T>() =>
        Model.FindEntityType(typeof(T))
        ?? throw new InvalidOperationException($"Entity {typeof(T).Name} is not mapped.");

    [Fact]
    public void Discovery_keyword_maps_to_existing_table_and_columns()
    {
        var entity = Entity<DiscoveryKeyword>();

        Assert.Equal("discovery_keywords", entity.GetTableName());
        Assert.Equal("id", Assert.Single(entity.FindPrimaryKey()!.Properties).GetColumnName());

        var keyword = entity.FindProperty(nameof(DiscoveryKeyword.Keyword))!;
        Assert.Equal("keyword", keyword.GetColumnName());
        Assert.Equal(200, keyword.GetMaxLength());
        Assert.False(keyword.IsNullable);

        var isActive = entity.FindProperty(nameof(DiscoveryKeyword.IsActive))!;
        Assert.Equal("is_active", isActive.GetColumnName());

        var isDeleted = entity.FindProperty(nameof(DiscoveryKeyword.IsDeleted))!;
        Assert.Equal("is_deleted", isDeleted.GetColumnName());

        Assert.True(entity.FindProperty(nameof(DiscoveryKeyword.CreatedByUserId))!.IsNullable);
        Assert.True(entity.FindProperty(nameof(DiscoveryKeyword.DeletedAt))!.IsNullable);
        Assert.True(entity.FindProperty(nameof(DiscoveryKeyword.DeletedByUserId))!.IsNullable);
        Assert.False(entity.FindProperty(nameof(DiscoveryKeyword.CreatedAt))!.IsNullable);
        Assert.False(entity.FindProperty(nameof(DiscoveryKeyword.ModifiedAt))!.IsNullable);

    }

    [Fact]
    public void Discovery_state_maps_to_existing_table_with_unique_keyword()
    {
        var entity = Entity<DiscoveryState>();

        Assert.Equal("discovery_states", entity.GetTableName());
        Assert.Equal("id", Assert.Single(entity.FindPrimaryKey()!.Properties).GetColumnName());

        var keywordId = entity.FindProperty(nameof(DiscoveryState.KeywordId))!;
        Assert.Equal("keyword_id", keywordId.GetColumnName());
        Assert.False(keywordId.IsNullable);

        Assert.True(entity.FindProperty(nameof(DiscoveryState.LastRecentCompletedAt))!.IsNullable);
        Assert.True(entity.FindProperty(nameof(DiscoveryState.RecentWindowStart))!.IsNullable);
        Assert.True(entity.FindProperty(nameof(DiscoveryState.RecentWindowEnd))!.IsNullable);
        Assert.True(entity.FindProperty(nameof(DiscoveryState.RecentPageToken))!.IsNullable);
        Assert.True(entity.FindProperty(nameof(DiscoveryState.HistoryWindowStart))!.IsNullable);
        Assert.True(entity.FindProperty(nameof(DiscoveryState.HistoryWindowEnd))!.IsNullable);
        Assert.True(entity.FindProperty(nameof(DiscoveryState.HistoryPageToken))!.IsNullable);
        Assert.False(entity.FindProperty(nameof(DiscoveryState.ModifiedAt))!.IsNullable);

    }

    [Fact]
    public void Channel_discovery_maps_to_existing_table_and_columns()
    {
        var entity = Entity<ChannelDiscovery>();

        Assert.Equal("channel_discoveries", entity.GetTableName());
        Assert.Equal("id", Assert.Single(entity.FindPrimaryKey()!.Properties).GetColumnName());

        var videoId = entity.FindProperty(nameof(ChannelDiscovery.YoutubeVideoId))!;
        Assert.Equal("youtube_video_id", videoId.GetColumnName());
        Assert.Equal(32, videoId.GetMaxLength());
        Assert.False(videoId.IsNullable);

        var title = entity.FindProperty(nameof(ChannelDiscovery.VideoTitle))!;
        Assert.Equal("video_title", title.GetColumnName());
        Assert.Equal(1000, title.GetMaxLength());
        Assert.False(title.IsNullable);

        Assert.True(entity.FindProperty(nameof(ChannelDiscovery.VideoThumbnailUrl))!.IsNullable);
        Assert.False(entity.FindProperty(nameof(ChannelDiscovery.VideoPublishedAt))!.IsNullable);
        Assert.False(entity.FindProperty(nameof(ChannelDiscovery.DiscoveredAt))!.IsNullable);

    }

}
