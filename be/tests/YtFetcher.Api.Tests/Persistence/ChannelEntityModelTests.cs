using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using YtFetcher.Core.Channels;
using YtFetcher.Infrastructure.Persistence;

namespace YtFetcher.Api.Tests.Persistence;

public class ChannelEntityModelTests
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

    private static IEntityType ChannelEntity() =>
        Model.FindEntityType(typeof(Channel))
        ?? throw new InvalidOperationException("Channel entity is not mapped.");

    [Fact]
    public void Channel_maps_to_channels_table_with_id_primary_key()
    {
        var entity = ChannelEntity();

        Assert.Equal("channels", entity.GetTableName());

        var primaryKey = entity.FindPrimaryKey();
        Assert.NotNull(primaryKey);
        Assert.Equal("id", Assert.Single(primaryKey.Properties).GetColumnName());
    }

    [Fact]
    public void Youtube_channel_id_is_required_with_max_length()
    {
        var entity = ChannelEntity();
        var property = entity.FindProperty(nameof(Channel.YoutubeChannelId));

        Assert.NotNull(property);
        Assert.Equal("youtube_channel_id", property.GetColumnName());
        Assert.False(property.IsNullable);
        Assert.Equal(64, property.GetMaxLength());

    }

    [Fact]
    public void Status_and_backfill_status_use_string_storage_properties()
    {
        var entity = ChannelEntity();

        var status = entity.FindProperty(nameof(Channel.StatusValue));
        Assert.NotNull(status);
        Assert.Equal("status", status.GetColumnName());
        Assert.Equal(typeof(string), status.ClrType);
        Assert.Equal(20, status.GetMaxLength());

        var backfillStatus = entity.FindProperty(nameof(Channel.BackfillStatusValue));
        Assert.NotNull(backfillStatus);
        Assert.Equal("backfill_status", backfillStatus.GetColumnName());
        Assert.Equal(typeof(string), backfillStatus.ClrType);
        Assert.Equal(20, backfillStatus.GetMaxLength());

        Assert.Null(entity.FindProperty(nameof(Channel.Status)));
        Assert.Null(entity.FindProperty(nameof(Channel.BackfillStatus)));
    }

    [Fact]
    public void Name_is_required_with_max_length_500()
    {
        var property = ChannelEntity().FindProperty(nameof(Channel.Name));

        Assert.NotNull(property);
        Assert.Equal("name", property.GetColumnName());
        Assert.False(property.IsNullable);
        Assert.Equal(500, property.GetMaxLength());
    }

    [Fact]
    public void Is_deleted_and_deleted_at_keep_their_column_shapes()
    {
        var entity = ChannelEntity();

        var isDeleted = entity.FindProperty(nameof(Channel.IsDeleted));
        Assert.NotNull(isDeleted);
        Assert.Equal("is_deleted", isDeleted.GetColumnName());

        var deletedAt = entity.FindProperty(nameof(Channel.DeletedAt));
        Assert.NotNull(deletedAt);
        Assert.Equal("deleted_at", deletedAt.GetColumnName());
        Assert.True(deletedAt.IsNullable);
    }

    [Fact]
    public void Approved_at_and_rejected_at_are_nullable_timestamptz_columns()
    {
        var entity = ChannelEntity();

        var approvedAt = entity.FindProperty(nameof(Channel.ApprovedAt));
        Assert.NotNull(approvedAt);
        Assert.Equal("approved_at", approvedAt.GetColumnName());
        Assert.True(approvedAt.IsNullable);
        Assert.Equal("timestamp with time zone", approvedAt.GetColumnType());

        var rejectedAt = entity.FindProperty(nameof(Channel.RejectedAt));
        Assert.NotNull(rejectedAt);
        Assert.Equal("rejected_at", rejectedAt.GetColumnName());
        Assert.True(rejectedAt.IsNullable);
        Assert.Equal("timestamp with time zone", rejectedAt.GetColumnType());
    }
}
