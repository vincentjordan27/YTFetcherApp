using YtFetcher.Application.DiscoveryKeywords.DTO.Request;
using YtFetcher.Application.DiscoveryKeywords.DTO.Response;
using YtFetcher.Application.DiscoveryKeywords.UseCases;
using YtFetcher.Application.Abstractions.Services;
using YtFetcher.Core.Discovery;

namespace YtFetcher.Api.Tests.DiscoveryKeywords;

/// <summary>
/// Live discovery keyword management: trimmed creation, the duplicate rule that spans active, disabled
/// and removed rows, selection toggling, soft deletion, and the server-side page counts.
/// </summary>
public class DiscoveryKeywordUseCaseTests
{
    private const string DuplicateMessage = "This keyword already exists or was previously removed.";

    private static readonly DateTimeOffset Now = new(2026, 10, 5, 4, 0, 0, TimeSpan.Zero);
    private static readonly Guid ActorUserId = Guid.NewGuid();

    [Fact]
    public async Task The_page_returns_the_server_counts_and_the_database_wide_active_count()
    {
        var repository = new InMemoryDiscoveryKeywordRepository();
        repository.Seed(
            Seed("alpha", Now.AddMinutes(-3), isActive: true),
            Seed("beta", Now.AddMinutes(-2), isActive: false),
            Seed("gamma", Now.AddMinutes(-1), isActive: true),
            Seed("removed", Now, isActive: true, isDeleted: true));
        var useCase = CreateUseCase(repository);

        var response = await useCase.GetPageAsync(
            new DiscoveryKeywordsListRequest(1, 2, null),
            CancellationToken.None);

        Assert.Equal(2, response.Items.Count);
        Assert.Equal("gamma", response.Items[0].Keyword);
        Assert.Equal("beta", response.Items[1].Keyword);
        Assert.Equal(3, response.TotalCount);
        Assert.Equal(2, response.ActiveCount);
        Assert.Equal(1, response.Page);
        Assert.Equal(2, response.PageSize);
    }

    [Fact]
    public async Task The_active_count_is_independent_of_the_current_search_and_page()
    {
        var repository = new InMemoryDiscoveryKeywordRepository();
        repository.Seed(
            Seed("alpha", Now.AddMinutes(-2), isActive: true),
            Seed("beta", Now.AddMinutes(-1), isActive: true));
        var useCase = CreateUseCase(repository);

        var response = await useCase.GetPageAsync(
            new DiscoveryKeywordsListRequest(2, 1, "alpha"),
            CancellationToken.None);

        Assert.Empty(response.Items);
        Assert.Equal(1, response.TotalCount);
        Assert.Equal(2, response.ActiveCount);
    }

    [Fact]
    public async Task Search_matches_a_case_insensitive_substring()
    {
        var repository = new InMemoryDiscoveryKeywordRepository();
        repository.Seed(
            Seed("Vtuber clips", Now.AddMinutes(-2), isActive: true),
            Seed("sakura", Now.AddMinutes(-1), isActive: true));
        var useCase = CreateUseCase(repository);

        var response = await useCase.GetPageAsync(
            new DiscoveryKeywordsListRequest(1, 20, "  VTUBER  "),
            CancellationToken.None);

        var item = Assert.Single(response.Items);
        Assert.Equal("Vtuber clips", item.Keyword);
        Assert.Equal(1, response.TotalCount);
    }

    [Fact]
    public async Task Create_trims_the_keyword_and_persists_it_active()
    {
        var repository = new InMemoryDiscoveryKeywordRepository();
        var useCase = CreateUseCase(repository);

        var response = await useCase.CreateAsync(
            new CreateDiscoveryKeywordRequest("  sakura  "),
            CancellationToken.None);

        Assert.Equal(DiscoveryKeywordMutationStatus.Succeeded, response.Status);
        Assert.Null(response.Message);
        Assert.NotNull(response.Keyword);
        Assert.Equal("sakura", response.Keyword.Keyword);
        Assert.True(response.Keyword.IsActive);
        Assert.Equal(Now, response.Keyword.CreatedAt);
        Assert.Equal(Now, response.Keyword.ModifiedAt);

        var persisted = Assert.Single(repository.All);
        Assert.Equal("sakura", persisted.Keyword);
        Assert.True(persisted.IsActive);
        Assert.False(persisted.IsDeleted);
        Assert.Equal(ActorUserId, persisted.CreatedByUserId);
        Assert.Equal(ActorUserId, persisted.ModifiedByUserId);
    }

    [Fact]
    public async Task Create_conflicts_with_an_existing_active_keyword_using_the_exact_sentence()
    {
        var repository = new InMemoryDiscoveryKeywordRepository();
        repository.Seed(Seed("Sakura", Now.AddMinutes(-1), isActive: true));
        var useCase = CreateUseCase(repository);

        var response = await useCase.CreateAsync(
            new CreateDiscoveryKeywordRequest("sakura"),
            CancellationToken.None);

        Assert.Equal(DiscoveryKeywordMutationStatus.Conflict, response.Status);
        Assert.Equal(DuplicateMessage, response.Message);
        Assert.Null(response.Keyword);
        Assert.Single(repository.All);
    }

    [Fact]
    public async Task Create_conflicts_with_a_disabled_keyword()
    {
        var repository = new InMemoryDiscoveryKeywordRepository();
        repository.Seed(Seed("sakura", Now.AddMinutes(-1), isActive: false));
        var useCase = CreateUseCase(repository);

        var response = await useCase.CreateAsync(
            new CreateDiscoveryKeywordRequest("Sakura"),
            CancellationToken.None);

        Assert.Equal(DiscoveryKeywordMutationStatus.Conflict, response.Status);
        Assert.Equal(DuplicateMessage, response.Message);
    }

    [Fact]
    public async Task Create_conflicts_with_a_removed_keyword_and_never_revives_it()
    {
        var repository = new InMemoryDiscoveryKeywordRepository();
        var removed = Seed("sakura", Now.AddMinutes(-1), isActive: true, isDeleted: true);
        repository.Seed(removed);
        var useCase = CreateUseCase(repository);

        var response = await useCase.CreateAsync(
            new CreateDiscoveryKeywordRequest("sakura"),
            CancellationToken.None);

        Assert.Equal(DiscoveryKeywordMutationStatus.Conflict, response.Status);
        Assert.Equal(DuplicateMessage, response.Message);
        Assert.True(removed.IsDeleted);
        Assert.Single(repository.All);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_rejects_a_blank_keyword(string candidate)
    {
        var repository = new InMemoryDiscoveryKeywordRepository();
        var useCase = CreateUseCase(repository);

        var response = await useCase.CreateAsync(
            new CreateDiscoveryKeywordRequest(candidate),
            CancellationToken.None);

        Assert.Equal(DiscoveryKeywordMutationStatus.ValidationError, response.Status);
        Assert.Null(response.Keyword);
        Assert.Empty(repository.All);
    }

    [Fact]
    public async Task Create_rejects_a_keyword_longer_than_the_column()
    {
        var repository = new InMemoryDiscoveryKeywordRepository();
        var useCase = CreateUseCase(repository);

        var response = await useCase.CreateAsync(
            new CreateDiscoveryKeywordRequest(new string('k', DiscoveryKeyword.KeywordMaxLength + 1)),
            CancellationToken.None);

        Assert.Equal(DiscoveryKeywordMutationStatus.ValidationError, response.Status);
        Assert.Empty(repository.All);
    }

    [Fact]
    public async Task Disable_and_enable_toggle_only_the_selection_state()
    {
        var repository = new InMemoryDiscoveryKeywordRepository();
        var keyword = Seed("sakura", Now.AddMinutes(-1), isActive: true);
        repository.Seed(keyword);
        var useCase = CreateUseCase(repository);

        var disabled = await useCase.DisableAsync(keyword.Id, CancellationToken.None);

        Assert.Equal(DiscoveryKeywordMutationStatus.Succeeded, disabled.Status);
        Assert.NotNull(disabled.Keyword);
        Assert.False(disabled.Keyword.IsActive);
        Assert.Equal(keyword.Keyword, disabled.Keyword.Keyword);
        Assert.False(keyword.IsDeleted);
        Assert.Equal(ActorUserId, keyword.ModifiedByUserId);
        Assert.Equal(0, await repository.CountActiveAsync(CancellationToken.None));

        var enabled = await useCase.EnableAsync(keyword.Id, CancellationToken.None);

        Assert.Equal(DiscoveryKeywordMutationStatus.Succeeded, enabled.Status);
        Assert.NotNull(enabled.Keyword);
        Assert.True(enabled.Keyword.IsActive);
        Assert.Equal(ActorUserId, keyword.ModifiedByUserId);
        Assert.Equal(1, await repository.CountActiveAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Repeating_the_requested_selection_state_succeeds_without_changing_the_row()
    {
        var repository = new InMemoryDiscoveryKeywordRepository();
        var disabledKeyword = Seed("sakura", Now.AddMinutes(-2), isActive: false);
        var activeKeyword = Seed("valo", Now.AddMinutes(-1), isActive: true);
        repository.Seed(disabledKeyword, activeKeyword);
        var useCase = CreateUseCase(repository);
        var disabledModifiedAt = disabledKeyword.ModifiedAt;
        var activeModifiedAt = activeKeyword.ModifiedAt;

        var disableAgain = await useCase.DisableAsync(disabledKeyword.Id, CancellationToken.None);
        var enableAgain = await useCase.EnableAsync(activeKeyword.Id, CancellationToken.None);

        Assert.Equal(DiscoveryKeywordMutationStatus.Succeeded, disableAgain.Status);
        Assert.Equal(DiscoveryKeywordMutationStatus.Succeeded, enableAgain.Status);
        Assert.False(disabledKeyword.IsActive);
        Assert.True(activeKeyword.IsActive);
        Assert.Equal(disabledModifiedAt, disabledKeyword.ModifiedAt);
        Assert.Equal(activeModifiedAt, activeKeyword.ModifiedAt);
    }

    [Fact]
    public async Task Remove_soft_deletes_the_keyword_and_leaves_it_gone()
    {
        var repository = new InMemoryDiscoveryKeywordRepository();
        var keyword = Seed("sakura", Now.AddMinutes(-1), isActive: true);
        repository.Seed(keyword);
        var useCase = CreateUseCase(repository);

        var removed = await useCase.RemoveAsync(keyword.Id, CancellationToken.None);

        Assert.Equal(DiscoveryKeywordMutationStatus.Succeeded, removed.Status);
        Assert.True(keyword.IsDeleted);
        Assert.Equal(Now, keyword.DeletedAt);
        Assert.Equal(Now, keyword.ModifiedAt);
        Assert.Equal(ActorUserId, keyword.DeletedByUserId);
        Assert.Equal(ActorUserId, keyword.ModifiedByUserId);

        var page = await useCase.GetPageAsync(
            new DiscoveryKeywordsListRequest(1, 20, null),
            CancellationToken.None);
        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
        Assert.Equal(0, page.ActiveCount);

        var removeAgain = await useCase.RemoveAsync(keyword.Id, CancellationToken.None);
        Assert.Equal(DiscoveryKeywordMutationStatus.NotFound, removeAgain.Status);
    }

    [Fact]
    public async Task Unknown_keyword_ids_report_not_found()
    {
        var repository = new InMemoryDiscoveryKeywordRepository();
        var useCase = CreateUseCase(repository);

        Assert.Equal(
            DiscoveryKeywordMutationStatus.NotFound,
            (await useCase.DisableAsync(Guid.NewGuid(), CancellationToken.None)).Status);
        Assert.Equal(
            DiscoveryKeywordMutationStatus.NotFound,
            (await useCase.EnableAsync(Guid.NewGuid(), CancellationToken.None)).Status);
        Assert.Equal(
            DiscoveryKeywordMutationStatus.NotFound,
            (await useCase.RemoveAsync(Guid.NewGuid(), CancellationToken.None)).Status);
    }

    private static DiscoveryKeywordUseCase CreateUseCase(InMemoryDiscoveryKeywordRepository repository) =>
        new(repository, new FakeCurrentActor(ActorUserId), new FixedTimeProvider(Now));

    private static DiscoveryKeyword Seed(
        string keyword,
        DateTimeOffset createdAt,
        bool isActive,
        bool isDeleted = false) =>
        new()
        {
            Id = Guid.NewGuid(),
            Keyword = keyword,
            IsActive = isActive,
            IsDeleted = isDeleted,
            DeletedAt = isDeleted ? createdAt : null,
            CreatedAt = createdAt,
            ModifiedAt = createdAt
        };

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class FakeCurrentActor(Guid userId) : ICurrentActor
    {
        public Guid UserId { get; } = userId;
    }
}
