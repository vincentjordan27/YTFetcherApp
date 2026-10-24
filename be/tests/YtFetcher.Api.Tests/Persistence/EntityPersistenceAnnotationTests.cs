using System.ComponentModel.DataAnnotations.Schema;
using YtFetcher.Core.Channels;
using YtFetcher.Core.Discovery;
using YtFetcher.Core.Users;
using YtFetcher.Infrastructure.Persistence;

namespace YtFetcher.Api.Tests.Persistence;

public sealed class EntityPersistenceAnnotationTests
{
    [Theory]
    [InlineData(typeof(Channel), "channels")]
    [InlineData(typeof(DiscoveryKeyword), "discovery_keywords")]
    [InlineData(typeof(DiscoveryState), "discovery_states")]
    [InlineData(typeof(ChannelDiscovery), "channel_discoveries")]
    [InlineData(typeof(User), "users")]
    public void Persisted_entity_declares_its_database_table(Type entityType, string expectedTableName)
    {
        var table = entityType.GetCustomAttributes(typeof(TableAttribute), inherit: false)
            .Cast<TableAttribute>()
            .Single();

        Assert.Equal(expectedTableName, table.Name);
    }

    [Theory]
    [InlineData(typeof(Channel), nameof(Channel.YoutubeChannelId), "youtube_channel_id")]
    [InlineData(typeof(Channel), nameof(Channel.StatusValue), "status")]
    [InlineData(typeof(Channel), nameof(Channel.BackfillStatusValue), "backfill_status")]
    [InlineData(typeof(DiscoveryKeyword), nameof(DiscoveryKeyword.CreatedByUserId), "created_by")]
    [InlineData(typeof(DiscoveryState), nameof(DiscoveryState.LastRecentCompletedAt), "last_recent_completed_at")]
    [InlineData(typeof(ChannelDiscovery), nameof(ChannelDiscovery.YoutubeVideoId), "youtube_video_id")]
    [InlineData(typeof(User), nameof(User.FirebaseUid), "firebase_uid")]
    [InlineData(typeof(User), nameof(User.Email), "email")]
    [InlineData(typeof(User), nameof(User.DisplayName), "display_name")]
    [InlineData(typeof(User), nameof(User.Role), "role")]
    [InlineData(typeof(User), nameof(User.IsActive), "is_active")]
    [InlineData(typeof(User), nameof(User.LastLoginAt), "last_login_at")]
    public void Persisted_property_declares_its_database_column(
        Type entityType,
        string propertyName,
        string expectedColumnName)
    {
        var property = entityType.GetProperty(propertyName)
            ?? throw new InvalidOperationException($"Missing property {entityType.Name}.{propertyName}.");
        var column = property.GetCustomAttributes(typeof(ColumnAttribute), inherit: false)
            .Cast<ColumnAttribute>()
            .Single();

        Assert.Equal(expectedColumnName, column.Name);
    }

    [Fact]
    public void Db_context_declares_only_the_persisted_table_sets()
    {
        var setProperties = typeof(YtFetcherDbContext)
            .GetProperties()
            .Where(property => property.PropertyType.IsGenericType &&
                               property.PropertyType.GetGenericTypeDefinition().Name == "DbSet`1")
            .Select(property => property.Name)
            .Order()
            .ToArray();

        Assert.Equal(
            ["ChannelBackfillRuns", "ChannelDiscoveries", "Channels", "DiscoveryKeywords", "DiscoveryStates", "Users", "Videos", "VideoSnapshots", "YouTubeDailyQuotaUsages"],
            setProperties);
        Assert.DoesNotContain(
            typeof(YtFetcherDbContext).GetMethods(
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.DeclaredOnly),
            method => method.Name == "OnModelCreating");
    }
}
