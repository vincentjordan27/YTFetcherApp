using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using YtFetcher.Core.Auditing;

namespace YtFetcher.Core.Discovery;

/// <summary>
/// Persisted discovery search keyword and its selection state. A disabled keyword stays persisted and
/// only stops taking part in future discovery selection; a soft-deleted keyword is never revived.
/// </summary>
[Table("discovery_keywords")]
public sealed class DiscoveryKeyword : AuditableEntity
{
    [Key]
    [Column("id", TypeName = "uuid")]
    public Guid Id { get; set; }

    [Required]
    [MaxLength(KeywordMaxLength)]
    [Column("keyword")]
    public string Keyword { get; set; } = string.Empty;

    [Column("is_active")]
    public bool IsActive { get; set; }

    /// <summary>Maximum keyword length, matching the database column.</summary>
    public const int KeywordMaxLength = 200;

    /// <summary>
    /// Creates an active keyword from already validated input. The keyword is trimmed, and a blank
    /// result is refused so the persisted value can never be an empty search term. The caller owns the
    /// duplicate check and the persistence of the returned entity.
    /// </summary>
    public static DiscoveryKeyword Create(string keyword, DateTimeOffset utcNow, Guid actorUserId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyword);

        var trimmedKeyword = keyword.Trim();
        if (trimmedKeyword.Length > KeywordMaxLength)
        {
            throw new ArgumentException(
                $"A discovery keyword cannot exceed {KeywordMaxLength} characters.",
                nameof(keyword));
        }

        return new DiscoveryKeyword
        {
            Id = Guid.NewGuid(),
            Keyword = trimmedKeyword,
            IsActive = true,
            IsDeleted = false,
            CreatedAt = utcNow,
            ModifiedAt = utcNow,
            CreatedByUserId = actorUserId,
            ModifiedByUserId = actorUserId
        };
    }

    /// <summary>
    /// Compares a candidate search term with this keyword's persisted value, ignoring case, so the
    /// duplicate policy covers active, disabled, and soft-deleted rows alike.
    /// </summary>
    public bool MatchesKeyword(string? candidate) =>
        candidate is not null &&
        string.Equals(Keyword, candidate.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Excludes this keyword from future discovery selection. Idempotent: disabling an already disabled
    /// keyword returns <c>false</c> without mutating state. Returns <c>false</c> for a deleted keyword.
    /// </summary>
    public bool Disable(DateTimeOffset utcNow, Guid actorUserId)
    {
        if (IsDeleted || !IsActive)
        {
            return false;
        }

        IsActive = false;
        ModifiedAt = utcNow;
        ModifiedByUserId = actorUserId;
        return true;
    }

    /// <summary>
    /// Returns this keyword to future discovery selection. Idempotent: enabling an already active
    /// keyword returns <c>false</c> without mutating state. Returns <c>false</c> for a deleted keyword
    /// so a soft-deleted keyword is never revived.
    /// </summary>
    public bool Enable(DateTimeOffset utcNow, Guid actorUserId)
    {
        if (IsDeleted || IsActive)
        {
            return false;
        }

        IsActive = true;
        ModifiedAt = utcNow;
        ModifiedByUserId = actorUserId;
        return true;
    }

    /// <summary>
    /// Applies the <c>active -&gt; soft deleted</c> transition. The row stays in the table and keeps its
    /// keyword, so the keyword can never be created again and a rejected duplicate is always visible.
    /// Returns <c>false</c> without mutating state when the keyword is already deleted.
    /// </summary>
    public bool SoftDelete(DateTimeOffset utcNow, Guid? deletedByUserId)
    {
        if (IsDeleted)
        {
            return false;
        }

        IsDeleted = true;
        DeletedAt = utcNow;
        DeletedByUserId = deletedByUserId;
        ModifiedByUserId = deletedByUserId;
        ModifiedAt = utcNow;
        return true;
    }
}
