using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using YtFetcher.Core.Auditing;

namespace YtFetcher.Core.Users;

/// <summary>
/// Persisted application identity behind an authenticated Firebase Google account. It stores the verified
/// Firebase UID, the verified email, the single current application role, and the access state an
/// authentication check must honour.
/// </summary>
[Table("users")]
public sealed class User : AuditableEntity
{
    /// <summary>
    /// Durable actor used by anonymous deployments and background work. It is seeded by the SQL schema and
    /// never represents a Firebase account.
    /// </summary>
    public static readonly Guid SystemUserId = new("00000000-0000-0000-0000-000000000001");

    public const string SystemFirebaseUid = "SYSTEM";

    public const string SystemEmail = "system@ytfetcher.invalid";

    [Key]
    [Column("id", TypeName = "uuid")]
    public Guid Id { get; set; }

    [Required]
    [MaxLength(FirebaseUidMaxLength)]
    [Column("firebase_uid")]
    public string FirebaseUid { get; set; } = string.Empty;

    [Required]
    [MaxLength(EmailMaxLength)]
    [Column("email")]
    public string Email { get; set; } = string.Empty;

    [MaxLength(DisplayNameMaxLength)]
    [Column("display_name")]
    public string? DisplayName { get; set; }

    [Required]
    [MaxLength(RoleMaxLength)]
    [Column("role")]
    public string Role { get; set; } = AdminRole;

    [Column("is_active")]
    public bool IsActive { get; set; } = true;

    [Column("last_login_at", TypeName = "timestamp with time zone")]
    public DateTimeOffset? LastLoginAt { get; set; }

    /// <summary>The only application role that exists today. Role management is deliberately not implemented.</summary>
    public const string AdminRole = "ADMIN";

    /// <summary>Maximum length of the Firebase UID, matching the database column.</summary>
    public const int FirebaseUidMaxLength = 128;

    /// <summary>Maximum length of the verified email, matching the database column.</summary>
    public const int EmailMaxLength = 320;

    /// <summary>Maximum length of the display name, matching the database column.</summary>
    public const int DisplayNameMaxLength = 200;

    /// <summary>Maximum length of the role, matching the database column.</summary>
    public const int RoleMaxLength = 20;

    /// <summary>
    /// True when this identity may hold a session. A soft-deleted or deactivated row is denied even
    /// though the Firebase account itself authenticated successfully.
    /// </summary>
    public bool IsAllowed => !IsDeleted && IsActive;

    /// <summary>
    /// Records a successful application login. It only advances the login timestamp and the modified audit,
    /// so it never changes identity, role, or access state.
    /// </summary>
    public void RecordLogin(DateTimeOffset utcNow)
    {
        LastLoginAt = utcNow;
        ModifiedAt = utcNow;
        ModifiedByUserId = Id;
    }

    /// <summary>
    /// Creates the application user for a first verified Firebase login. The identity values must already be
    /// verified from the Firebase ID token; the caller persists the returned entity with an ordinary
    /// single-table save before any session is issued.
    /// </summary>
    public static User CreateFirstLogin(
        string firebaseUid,
        string verifiedEmail,
        string? displayName,
        DateTimeOffset utcNow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(firebaseUid);
        ArgumentException.ThrowIfNullOrWhiteSpace(verifiedEmail);

        var trimmedDisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();

        return new User
        {
            Id = Guid.NewGuid(),
            FirebaseUid = firebaseUid.Trim(),
            Email = verifiedEmail.Trim(),
            DisplayName = trimmedDisplayName is { Length: > DisplayNameMaxLength }
                ? trimmedDisplayName[..DisplayNameMaxLength]
                : trimmedDisplayName,
            Role = AdminRole,
            IsActive = true,
            IsDeleted = false,
            LastLoginAt = utcNow,
            CreatedAt = utcNow,
            ModifiedAt = utcNow,
            CreatedByUserId = SystemUserId,
            ModifiedByUserId = SystemUserId
        };
    }
}
