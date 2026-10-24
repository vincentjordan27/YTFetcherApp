using System.ComponentModel.DataAnnotations.Schema;

namespace YtFetcher.Core.Auditing;

public abstract class AuditableEntity
{
    [Column("created_at", TypeName = "timestamp with time zone")]
    public DateTimeOffset CreatedAt { get; set; }

    [Column("created_by", TypeName = "uuid")]
    public Guid? CreatedByUserId { get; set; }

    [Column("modified_at", TypeName = "timestamp with time zone")]
    public DateTimeOffset ModifiedAt { get; set; }

    [Column("modified_by", TypeName = "uuid")]
    public Guid? ModifiedByUserId { get; set; }

    [Column("is_deleted")]
    public bool IsDeleted { get; set; }

    [Column("deleted_at", TypeName = "timestamp with time zone")]
    public DateTimeOffset? DeletedAt { get; set; }

    [Column("deleted_by", TypeName = "uuid")]
    public Guid? DeletedByUserId { get; set; }
}
