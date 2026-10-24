using System.ComponentModel.DataAnnotations.Schema;
using YtFetcher.Core.Auditing;
using YtFetcher.Core.Channels;
using YtFetcher.Core.Discovery;

namespace YtFetcher.Api.Tests.Core;

public sealed class AuditableEntityTests
{
    [Theory]
    [InlineData(typeof(Channel))]
    [InlineData(typeof(DiscoveryKeyword))]
    [InlineData(typeof(DiscoveryState))]
    [InlineData(typeof(ChannelDiscovery))]
    public void Persisted_entity_inherits_the_common_audit_fields(Type entityType)
    {
        Assert.True(typeof(AuditableEntity).IsAssignableFrom(entityType));
    }

    [Theory]
    [InlineData(nameof(AuditableEntity.CreatedAt), "created_at")]
    [InlineData(nameof(AuditableEntity.CreatedByUserId), "created_by")]
    [InlineData(nameof(AuditableEntity.ModifiedAt), "modified_at")]
    [InlineData(nameof(AuditableEntity.ModifiedByUserId), "modified_by")]
    [InlineData(nameof(AuditableEntity.IsDeleted), "is_deleted")]
    [InlineData(nameof(AuditableEntity.DeletedAt), "deleted_at")]
    [InlineData(nameof(AuditableEntity.DeletedByUserId), "deleted_by")]
    public void Common_audit_field_declares_its_database_column(string propertyName, string columnName)
    {
        var property = typeof(AuditableEntity).GetProperty(propertyName)
            ?? throw new InvalidOperationException($"Missing audit property {propertyName}.");
        var column = property.GetCustomAttributes(typeof(ColumnAttribute), inherit: false)
            .Cast<ColumnAttribute>()
            .Single();

        Assert.Equal(columnName, column.Name);
    }
}
