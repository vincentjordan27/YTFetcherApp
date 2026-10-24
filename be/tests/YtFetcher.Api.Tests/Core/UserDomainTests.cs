using YtFetcher.Core.Users;

namespace YtFetcher.Api.Tests.Core;

/// <summary>
/// The application user lifecycle: a first verified login creates exactly one active ADMIN identity, and a
/// deactivated or soft-deleted row can never hold a session.
/// </summary>
public class UserDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void First_login_creates_an_active_admin_identity()
    {
        var user = User.CreateFirstLogin("firebase-uid", "person@example.test", "Person One", Now);

        Assert.NotEqual(Guid.Empty, user.Id);
        Assert.Equal("firebase-uid", user.FirebaseUid);
        Assert.Equal("person@example.test", user.Email);
        Assert.Equal("Person One", user.DisplayName);
        Assert.Equal(User.AdminRole, user.Role);
        Assert.True(user.IsActive);
        Assert.False(user.IsDeleted);
        Assert.Equal(Now, user.LastLoginAt);
        Assert.Equal(Now, user.CreatedAt);
        Assert.Equal(Now, user.ModifiedAt);
        Assert.Equal(User.SystemUserId, user.CreatedByUserId);
        Assert.Equal(User.SystemUserId, user.ModifiedByUserId);
        Assert.True(user.IsAllowed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_display_name_is_not_stored(string? displayName)
    {
        var user = User.CreateFirstLogin("firebase-uid", "person@example.test", displayName, Now);

        Assert.Null(user.DisplayName);
    }

    [Fact]
    public void A_display_name_beyond_the_column_length_is_trimmed_to_fit()
    {
        var user = User.CreateFirstLogin(
            "firebase-uid",
            "person@example.test",
            new string('n', User.DisplayNameMaxLength + 25),
            Now);

        Assert.Equal(User.DisplayNameMaxLength, user.DisplayName!.Length);
    }

    [Theory]
    [InlineData("", "person@example.test")]
    [InlineData("   ", "person@example.test")]
    [InlineData("firebase-uid", "")]
    public void A_verified_identity_needs_both_identifiers(string firebaseUid, string email)
    {
        Assert.Throws<ArgumentException>(() => User.CreateFirstLogin(firebaseUid, email, null, Now));
    }

    [Fact]
    public void Deactivated_and_soft_deleted_users_are_not_allowed()
    {
        var active = User.CreateFirstLogin("firebase-uid", "person@example.test", null, Now);

        Assert.True(active.IsAllowed);

        active.IsActive = false;
        Assert.False(active.IsAllowed);

        active.IsActive = true;
        active.IsDeleted = true;
        Assert.False(active.IsAllowed);
    }

    [Fact]
    public void Recording_a_login_only_advances_the_login_and_modified_timestamps()
    {
        var created = User.CreateFirstLogin("firebase-uid", "person@example.test", "Person One", Now);
        var later = Now.AddDays(1);

        created.RecordLogin(later);

        Assert.Equal(later, created.LastLoginAt);
        Assert.Equal(later, created.ModifiedAt);
        Assert.Equal(created.Id, created.ModifiedByUserId);
        Assert.Equal(Now, created.CreatedAt);
        Assert.Equal("firebase-uid", created.FirebaseUid);
        Assert.Equal("person@example.test", created.Email);
        Assert.Equal(User.AdminRole, created.Role);
        Assert.True(created.IsAllowed);
    }

    [Fact]
    public void System_actor_has_a_stable_non_login_identity()
    {
        Assert.NotEqual(Guid.Empty, User.SystemUserId);
        Assert.Equal("SYSTEM", User.SystemFirebaseUid);
        Assert.Equal("system@ytfetcher.invalid", User.SystemEmail);
    }
}
