using YtFetcher.Application.Abstractions.Repositories;
using YtFetcher.Application.Abstractions.Services;
using YtFetcher.Application.Videos.DTO.Request;
using YtFetcher.Application.Videos.UseCases;
using YtFetcher.Core.Videos;

namespace YtFetcher.Api.Tests.UseCases;

/// <summary>
/// Active/reference mapping, search normalization, paging pass-through, sort resolution, and the two
/// reference-state delegations to the repository. The use case only describes sorting; the repository owns
/// the ordering query.
/// </summary>
public class VideoUseCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 10, 0, 0, TimeSpan.Zero);

    private static readonly Guid ActorUserId = Guid.Parse("10000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task Active_page_maps_core_entities_with_their_joined_channel_fields()
    {
        var channelId = Guid.NewGuid();
        var video = CreateVideo(channelId, "video-1", "Sakura live", views: 12_345, likes: 678);
        var repository = new FakeVideoRepository
        {
            Videos = [video],
            ChannelNames = new Dictionary<Guid, string> { [channelId] = "Sakura Channel" },
            ChannelIds = new Dictionary<Guid, string> { [channelId] = "UC-sakura" },
            TotalCount = 1
        };
        var useCase = CreateUseCase(repository);

        var response = await useCase.GetActiveAsync(
            new ActiveVideosRequest(2, 25, "  ", null, null),
            CancellationToken.None);

        var item = Assert.Single(response.Items);
        Assert.Equal(video.Id, item.Id);
        Assert.Equal("video-1", item.YoutubeVideoId);
        Assert.Equal("Sakura live", item.Title);
        Assert.Equal("Sakura Channel", item.ChannelName);

        // The provider channel id travels with the row so the client never infers a link from the name.
        Assert.Equal("UC-sakura", item.YoutubeChannelId);

        Assert.Equal("https://example.com/video-1.jpg", item.ThumbnailUrl);
        Assert.Equal(120_000, item.DurationMillis);
        Assert.Equal(12_345, item.CurrentViews);
        Assert.Equal(678, item.CurrentLikes);
        Assert.Equal(Now.AddDays(-30), item.PublishedAt);
        Assert.Equal(Now, item.LastStatsFetchedAt);

        // Response metadata mirrors the request; blank search text becomes no filter at all.
        Assert.Equal(2, response.Page);
        Assert.Equal(25, response.PageSize);
        Assert.Equal(1, response.TotalCount);
        Assert.Equal(2, repository.ReceivedPage);
        Assert.Equal(25, repository.ReceivedPageSize);
        Assert.Null(repository.ReceivedSearchText);
    }

    [Fact]
    public async Task Active_page_trims_the_search_text_before_the_repository_sees_it()
    {
        var repository = new FakeVideoRepository();
        var useCase = CreateUseCase(repository);

        await useCase.GetActiveAsync(new ActiveVideosRequest(1, 10, "  sakura  ", null, null), CancellationToken.None);

        Assert.Equal("sakura", repository.ReceivedSearchText);
    }

    [Fact]
    public async Task A_video_without_a_joined_channel_still_maps_with_empty_channel_fields()
    {
        var repository = new FakeVideoRepository
        {
            Videos = [CreateVideo(Guid.NewGuid(), "video-1", "Sakura live")]
        };
        var useCase = CreateUseCase(repository);

        var response = await useCase.GetActiveAsync(new ActiveVideosRequest(1, 10, null, null, null), CancellationToken.None);

        var item = Assert.Single(response.Items);
        Assert.Equal(string.Empty, item.ChannelName);
        Assert.Equal(string.Empty, item.YoutubeChannelId);
    }

    [Fact]
    public async Task An_absent_sort_falls_back_to_views_descending()
    {
        var repository = new FakeVideoRepository();
        var useCase = CreateUseCase(repository);

        await useCase.GetActiveAsync(new ActiveVideosRequest(1, 10, null, null, null), CancellationToken.None);

        Assert.Equal(VideoListSortBy.Views, repository.ReceivedSortBy);
        Assert.Equal(VideoListSortDirection.Desc, repository.ReceivedSortDirection);
    }

    [Fact]
    public async Task A_supplied_sort_is_parsed_case_insensitively_and_forwarded_unchanged()
    {
        var repository = new FakeVideoRepository();
        var useCase = CreateUseCase(repository);

        await useCase.GetActiveAsync(
            new ActiveVideosRequest(1, 10, null, "publishedAt", "asc"),
            CancellationToken.None);
        await useCase.GetReferenceAsync(
            new ReferenceVideosRequest(1, 10, null, "TITLE", "DESC"),
            CancellationToken.None);

        Assert.Equal(VideoListSortBy.PublishedAt, repository.ReceivedSortBy);
        Assert.Equal(VideoListSortDirection.Asc, repository.ReceivedSortDirection);
        Assert.Equal(VideoListSortBy.Title, repository.ReceivedReferenceSortBy);
        Assert.Equal(VideoListSortDirection.Desc, repository.ReceivedReferenceSortDirection);
    }

    [Fact]
    public async Task An_unknown_sort_never_throws_and_uses_the_documented_default()
    {
        // The transport validator rejects unknown names before the use case runs; this proves the use case
        // itself can never throw a parse error or invent an ordering.
        var repository = new FakeVideoRepository();
        var useCase = CreateUseCase(repository);

        await useCase.GetActiveAsync(new ActiveVideosRequest(1, 10, null, "3", "sideways"), CancellationToken.None);

        Assert.Equal(VideoListSortBy.Views, repository.ReceivedSortBy);
        Assert.Equal(VideoListSortDirection.Desc, repository.ReceivedSortDirection);
    }

    [Fact]
    public async Task Reference_page_maps_core_entities_with_their_joined_channel_fields()
    {
        var channelId = Guid.NewGuid();
        var video = CreateVideo(channelId, "reference-1", "Saved clip", views: 9_000);
        var repository = new FakeVideoRepository
        {
            Videos = [video],
            ChannelNames = new Dictionary<Guid, string> { [channelId] = "Sakura Channel" },
            ChannelIds = new Dictionary<Guid, string> { [channelId] = "UC-sakura" },
            TotalCount = 1
        };
        var useCase = CreateUseCase(repository);

        var response = await useCase.GetReferenceAsync(
            new ReferenceVideosRequest(1, 25, "  ", null, null),
            CancellationToken.None);

        var item = Assert.Single(response.Items);
        Assert.Equal(video.Id, item.Id);
        Assert.Equal("reference-1", item.YoutubeVideoId);
        Assert.Equal("Sakura Channel", item.ChannelName);
        Assert.Equal("UC-sakura", item.YoutubeChannelId);
        Assert.Equal(9_000, item.CurrentViews);
        Assert.Equal(1, response.TotalCount);

        Assert.Equal(1, repository.ReceivedReferencePage);
        Assert.Equal(25, repository.ReceivedReferencePageSize);
        Assert.Null(repository.ReceivedReferenceSearchText);
    }

    [Fact]
    public async Task Reference_page_trims_the_search_text_before_the_repository_sees_it()
    {
        var repository = new FakeVideoRepository();
        var useCase = CreateUseCase(repository);

        await useCase.GetReferenceAsync(new ReferenceVideosRequest(1, 10, "  sakura  ", null, null), CancellationToken.None);

        Assert.Equal("sakura", repository.ReceivedReferenceSearchText);
    }

    [Fact]
    public async Task Reference_page_defaults_to_the_most_recent_reference_first()
    {
        var repository = new FakeVideoRepository();
        var useCase = CreateUseCase(repository);

        await useCase.GetReferenceAsync(new ReferenceVideosRequest(1, 10, null, null, null), CancellationToken.None);

        Assert.Equal(VideoListSortBy.ReferenceSetAt, repository.ReceivedReferenceSortBy);
        Assert.Equal(VideoListSortDirection.Desc, repository.ReceivedReferenceSortDirection);
    }

    [Fact]
    public async Task Mark_as_reference_forwards_the_ids_and_clock_and_returns_the_repository_outcome()
    {
        var repository = new FakeVideoRepository { MarkResult = true };
        var useCase = CreateUseCase(repository);
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };

        var marked = await useCase.MarkAsReferenceAsync(
            new MarkVideosReferenceRequest(ids),
            CancellationToken.None);

        Assert.True(marked);
        Assert.Equal(ids, repository.ReceivedMarkedIds);
        Assert.Equal(ActorUserId, repository.ReceivedMarkActorUserId);
        Assert.Equal(Now, repository.ReceivedMarkUtcNow);
    }

    [Fact]
    public async Task Mark_as_reference_returns_false_when_the_repository_rejects_the_batch()
    {
        var repository = new FakeVideoRepository { MarkResult = false };
        var useCase = CreateUseCase(repository);

        var marked = await useCase.MarkAsReferenceAsync(
            new MarkVideosReferenceRequest([Guid.NewGuid()]),
            CancellationToken.None);

        Assert.False(marked);
    }

    [Fact]
    public async Task Remove_reference_forwards_the_single_id_and_clock_and_returns_the_repository_outcome()
    {
        var repository = new FakeVideoRepository { RemoveResult = true };
        var useCase = CreateUseCase(repository);
        var videoId = Guid.NewGuid();

        var removed = await useCase.RemoveReferenceAsync(
            new UnmarkVideoReferenceRequest(videoId),
            CancellationToken.None);

        Assert.True(removed);
        Assert.Equal(videoId, repository.ReceivedRemovedVideoId);
        Assert.Equal(ActorUserId, repository.ReceivedRemoveActorUserId);
        Assert.Equal(Now, repository.ReceivedRemoveUtcNow);
    }

    [Fact]
    public async Task Remove_reference_returns_false_when_the_row_is_not_a_reference()
    {
        var repository = new FakeVideoRepository { RemoveResult = false };
        var useCase = CreateUseCase(repository);

        var removed = await useCase.RemoveReferenceAsync(
            new UnmarkVideoReferenceRequest(Guid.NewGuid()),
            CancellationToken.None);

        Assert.False(removed);
    }

    [Fact]
    public async Task Set_reference_used_forwards_the_new_value_actor_and_clock()
    {
        var repository = new FakeVideoRepository { SetReferenceUsedResult = true };
        var useCase = CreateUseCase(repository);
        var videoId = Guid.NewGuid();

        var updated = await useCase.SetReferenceUsedAsync(
            new SetVideoReferenceUsedRequest(videoId, true),
            CancellationToken.None);

        Assert.True(updated);
        Assert.Equal(videoId, repository.ReceivedSetReferenceUsedVideoId);
        Assert.Equal(true, repository.ReceivedSetReferenceUsedValue);
        Assert.Equal(ActorUserId, repository.ReceivedSetReferenceUsedActorUserId);
        Assert.Equal(Now, repository.ReceivedSetReferenceUsedUtcNow);
    }

    [Fact]
    public async Task Active_count_uses_the_repository_active_rule_with_no_search_filter()
    {
        var repository = new FakeVideoRepository { TotalCount = 42 };
        var useCase = CreateUseCase(repository);

        var response = await useCase.GetActiveCountAsync(CancellationToken.None);

        Assert.Equal(42, response.Count);
        Assert.Null(repository.ReceivedSearchText);
    }

    [Fact]
    public async Task History_maps_snapshots_and_returns_the_page_metadata()
    {
        var videoId = Guid.NewGuid();
        var snapshots = new List<VideoSnapshot>
        {
            new() { Id = 9, VideoId = videoId, CapturedAt = Now.AddHours(-1), ViewCount = 1_500, LikeCount = 40 },
            new() { Id = 4, VideoId = videoId, CapturedAt = Now.AddHours(-2), ViewCount = 1_200, LikeCount = 31 }
        };
        var repository = new FakeVideoRepository
        {
            HistoryResult = (true, 7, snapshots)
        };
        var useCase = CreateUseCase(repository);

        var response = await useCase.GetHistoryAsync(videoId, new VideoHistoryRequest(2, 2), CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal(2, response!.Page);
        Assert.Equal(2, response.PageSize);
        Assert.Equal(7, response.TotalCount);
        Assert.Equal(videoId, repository.ReceivedHistoryVideoId);
        Assert.Equal(2, repository.ReceivedHistoryPage);
        Assert.Equal(2, repository.ReceivedHistoryPageSize);

        Assert.Equal(2, response.Items.Count);
        Assert.Equal(Now.AddHours(-1), response.Items[0].CapturedAt);
        Assert.Equal(1_500, response.Items[0].ViewCount);
        Assert.Equal(40, response.Items[0].LikeCount);
        Assert.Equal(1_200, response.Items[1].ViewCount);
    }

    [Fact]
    public async Task History_returns_null_when_the_video_does_not_exist()
    {
        var repository = new FakeVideoRepository { HistoryResult = (false, 0, []) };
        var useCase = CreateUseCase(repository);

        var response = await useCase.GetHistoryAsync(
            Guid.NewGuid(),
            new VideoHistoryRequest(1, 10),
            CancellationToken.None);

        Assert.Null(response);
    }

    private static VideoUseCase CreateUseCase(FakeVideoRepository repository) =>
        new(repository, new FixedCurrentActor(ActorUserId), new FixedTimeProvider(Now));

    private static Video CreateVideo(
        Guid channelId,
        string youtubeVideoId,
        string title,
        long views = 10,
        long likes = 1) => new()
    {
        Id = Guid.NewGuid(),
        YoutubeVideoId = youtubeVideoId,
        ChannelId = channelId,
        Title = title,
        ThumbnailUrl = $"https://example.com/{youtubeVideoId}.jpg",
        PublishedAt = Now.AddDays(-30),
        DurationMillis = 120_000,
        CurrentViews = views,
        CurrentLikes = likes,
        TrackingStatus = VideoTrackingStatus.ACTIVE,
        LastStatsFetchedAt = Now,
        IsDeleted = false,
        CreatedAt = Now.AddDays(-29),
        ModifiedAt = Now
    };

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class FixedCurrentActor(Guid userId) : ICurrentActor
    {
        public Guid UserId { get; } = userId;
    }

    private sealed class FakeVideoRepository : IVideoRepository
    {
        public IReadOnlyList<Video> Videos { get; init; } = [];

        public IReadOnlyDictionary<Guid, string> ChannelNames { get; init; } = new Dictionary<Guid, string>();

        public IReadOnlyDictionary<Guid, string> ChannelIds { get; init; } = new Dictionary<Guid, string>();

        public int TotalCount { get; init; }

        public bool MarkResult { get; init; }

        public bool RemoveResult { get; init; }

        public bool SetReferenceUsedResult { get; init; }

        public int? ReceivedPage { get; private set; }

        public int? ReceivedPageSize { get; private set; }

        public string? ReceivedSearchText { get; private set; }

        public VideoListSortBy? ReceivedSortBy { get; private set; }

        public VideoListSortDirection? ReceivedSortDirection { get; private set; }

        public int ReceivedReferencePage { get; private set; }

        public int? ReceivedReferencePageSize { get; private set; }

        public string? ReceivedReferenceSearchText { get; private set; }

        public VideoListSortBy? ReceivedReferenceSortBy { get; private set; }

        public VideoListSortDirection? ReceivedReferenceSortDirection { get; private set; }

        public IReadOnlyList<Guid>? ReceivedMarkedIds { get; private set; }

        public DateTimeOffset? ReceivedMarkUtcNow { get; private set; }

        public Guid? ReceivedMarkActorUserId { get; private set; }

        public Guid? ReceivedRemovedVideoId { get; private set; }

        public DateTimeOffset? ReceivedRemoveUtcNow { get; private set; }

        public Guid? ReceivedRemoveActorUserId { get; private set; }

        public Guid? ReceivedSetReferenceUsedVideoId { get; private set; }

        public bool? ReceivedSetReferenceUsedValue { get; private set; }

        public DateTimeOffset? ReceivedSetReferenceUsedUtcNow { get; private set; }

        public Guid? ReceivedSetReferenceUsedActorUserId { get; private set; }

        public (bool Exists, int TotalCount, IReadOnlyList<VideoSnapshot> Snapshots) HistoryResult { get; set; } =
            (false, 0, []);

        public Guid? ReceivedHistoryVideoId { get; private set; }

        public int? ReceivedHistoryPage { get; private set; }

        public int? ReceivedHistoryPageSize { get; private set; }

        public Task<IReadOnlyList<(Video Video, string ChannelName, string YoutubeChannelId)>> GetActivePageAsync(
            int page,
            int pageSize,
            string? searchText,
            VideoListSortBy sortBy,
            VideoListSortDirection sortDirection,
            CancellationToken cancellationToken)
        {
            ReceivedPage = page;
            ReceivedPageSize = pageSize;
            ReceivedSearchText = searchText;
            ReceivedSortBy = sortBy;
            ReceivedSortDirection = sortDirection;

            return Task.FromResult(Project(Videos));
        }

        public Task<int> CountActiveAsync(string? searchText, CancellationToken cancellationToken)
        {
            ReceivedSearchText = searchText;
            return Task.FromResult(TotalCount);
        }

        public Task<IReadOnlyList<(Video Video, string ChannelName, string YoutubeChannelId)>> GetReferencePageAsync(
            int page,
            int pageSize,
            string? searchText,
            VideoListSortBy sortBy,
            VideoListSortDirection sortDirection,
            CancellationToken cancellationToken)
        {
            ReceivedReferencePage = page;
            ReceivedReferencePageSize = pageSize;
            ReceivedReferenceSearchText = searchText;
            ReceivedReferenceSortBy = sortBy;
            ReceivedReferenceSortDirection = sortDirection;

            return Task.FromResult(Project(Videos));
        }

        public Task<int> CountReferenceAsync(string? searchText, CancellationToken cancellationToken)
        {
            ReceivedReferenceSearchText = searchText;
            return Task.FromResult(TotalCount);
        }

        public Task<bool> TryMarkAsReferenceAsync(
            IReadOnlyList<Guid> videoIds,
            Guid actorUserId,
            DateTimeOffset utcNow,
            CancellationToken cancellationToken)
        {
            ReceivedMarkedIds = videoIds;
            ReceivedMarkActorUserId = actorUserId;
            ReceivedMarkUtcNow = utcNow;

            return Task.FromResult(MarkResult);
        }

        public Task<bool> TryRemoveReferenceAsync(
            Guid videoId,
            Guid actorUserId,
            DateTimeOffset utcNow,
            CancellationToken cancellationToken)
        {
            ReceivedRemovedVideoId = videoId;
            ReceivedRemoveActorUserId = actorUserId;
            ReceivedRemoveUtcNow = utcNow;

            return Task.FromResult(RemoveResult);
        }

        public Task<bool> TrySetReferenceUsedAsync(
            Guid videoId,
            bool isUsed,
            Guid actorUserId,
            DateTimeOffset utcNow,
            CancellationToken cancellationToken)
        {
            ReceivedSetReferenceUsedVideoId = videoId;
            ReceivedSetReferenceUsedValue = isUsed;
            ReceivedSetReferenceUsedActorUserId = actorUserId;
            ReceivedSetReferenceUsedUtcNow = utcNow;

            return Task.FromResult(SetReferenceUsedResult);
        }

        public Task<(bool Exists, int TotalCount, IReadOnlyList<VideoSnapshot> Snapshots)> GetSnapshotHistoryPageAsync(
            Guid videoId,
            int page,
            int pageSize,
            CancellationToken cancellationToken)
        {
            ReceivedHistoryVideoId = videoId;
            ReceivedHistoryPage = page;
            ReceivedHistoryPageSize = pageSize;

            return Task.FromResult(HistoryResult);
        }

        /// <summary>Mirrors the repository join: a channel the fake does not know contributes empty fields.</summary>
        private IReadOnlyList<(Video Video, string ChannelName, string YoutubeChannelId)> Project(
            IReadOnlyList<Video> videos) =>
            videos
                .Select(video => (
                    video,
                    ChannelNames.GetValueOrDefault(video.ChannelId, string.Empty),
                    ChannelIds.GetValueOrDefault(video.ChannelId, string.Empty)))
                .ToList();
    }
}
