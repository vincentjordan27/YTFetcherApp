using YtFetcher.Core.Discovery;

namespace YtFetcher.Api.Tests.Core;

/// <summary>
/// Lifecycle invariants of a persisted discovery keyword: trimming, selection state, and the soft
/// delete that must never be undone.
/// </summary>
public class DiscoveryKeywordDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 4, 0, 0, TimeSpan.Zero);
    private static readonly Guid ActorUserId = Guid.NewGuid();

    [Fact]
    public void Create_trims_the_keyword_and_starts_active()
    {
        var keyword = DiscoveryKeyword.Create("  sakura  ", Now, ActorUserId);

        Assert.Equal("sakura", keyword.Keyword);
        Assert.True(keyword.IsActive);
        Assert.False(keyword.IsDeleted);
        Assert.Equal(Now, keyword.CreatedAt);
        Assert.Equal(Now, keyword.ModifiedAt);
        Assert.Equal(ActorUserId, keyword.CreatedByUserId);
        Assert.Equal(ActorUserId, keyword.ModifiedByUserId);
        Assert.NotEqual(Guid.Empty, keyword.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Create_refuses_a_blank_keyword(string candidate)
    {
        Assert.ThrowsAny<ArgumentException>(() => DiscoveryKeyword.Create(candidate, Now, ActorUserId));
    }

    [Fact]
    public void Create_refuses_a_keyword_longer_than_the_column()
    {
        var overlong = new string('k', DiscoveryKeyword.KeywordMaxLength + 1);

        Assert.Throws<ArgumentException>(() => DiscoveryKeyword.Create(overlong, Now, ActorUserId));

        var atLimit = DiscoveryKeyword.Create(new string('k', DiscoveryKeyword.KeywordMaxLength), Now, ActorUserId);
        Assert.Equal(DiscoveryKeyword.KeywordMaxLength, atLimit.Keyword.Length);
    }

    [Fact]
    public void Matching_ignores_case_and_surrounding_whitespace()
    {
        var keyword = DiscoveryKeyword.Create("Sakura", Now, ActorUserId);

        Assert.True(keyword.MatchesKeyword("sakura"));
        Assert.True(keyword.MatchesKeyword("  SAKURA  "));
        Assert.False(keyword.MatchesKeyword("sakura blossom"));
        Assert.False(keyword.MatchesKeyword(null));
        Assert.False(keyword.MatchesKeyword("   "));
    }

    [Fact]
    public void Disable_excludes_the_keyword_and_never_repeats()
    {
        var keyword = DiscoveryKeyword.Create("sakura", Now, ActorUserId);

        Assert.True(keyword.Disable(Now.AddMinutes(1), ActorUserId));
        Assert.False(keyword.IsActive);
        Assert.Equal(Now.AddMinutes(1), keyword.ModifiedAt);
        Assert.Equal(ActorUserId, keyword.ModifiedByUserId);

        Assert.False(keyword.Disable(Now.AddMinutes(2), ActorUserId));
    }

    [Fact]
    public void Enable_returns_the_keyword_to_selection_and_never_repeats()
    {
        var keyword = DiscoveryKeyword.Create("sakura", Now, ActorUserId);
        keyword.Disable(Now.AddMinutes(1), ActorUserId);

        Assert.True(keyword.Enable(Now.AddMinutes(2), ActorUserId));
        Assert.True(keyword.IsActive);
        Assert.Equal(Now.AddMinutes(2), keyword.ModifiedAt);
        Assert.Equal(ActorUserId, keyword.ModifiedByUserId);

        Assert.False(keyword.Enable(Now.AddMinutes(3), ActorUserId));
    }

    [Fact]
    public void A_deleted_keyword_is_never_selected_again()
    {
        var keyword = DiscoveryKeyword.Create("sakura", Now, ActorUserId);
        var deletedBy = Guid.NewGuid();

        Assert.True(keyword.SoftDelete(Now.AddMinutes(1), deletedBy));

        Assert.True(keyword.IsDeleted);
        Assert.Equal(Now.AddMinutes(1), keyword.DeletedAt);
        Assert.Equal(deletedBy, keyword.DeletedByUserId);
        Assert.Equal(deletedBy, keyword.ModifiedByUserId);
        Assert.Equal(Now.AddMinutes(1), keyword.ModifiedAt);

        Assert.False(keyword.SoftDelete(Now.AddMinutes(2), deletedBy));
        Assert.False(keyword.Enable(Now.AddMinutes(3), ActorUserId));
        Assert.False(keyword.Disable(Now.AddMinutes(3), ActorUserId));

        // Soft delete changes only the delete transition: the selection flag keeps its last value and
        // the row becomes invisible through the soft-delete filter alone.
        Assert.True(keyword.IsActive);
        Assert.Equal(Now.AddMinutes(1), keyword.ModifiedAt);
    }
}
