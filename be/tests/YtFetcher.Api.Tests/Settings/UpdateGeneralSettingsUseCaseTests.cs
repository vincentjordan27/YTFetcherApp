using YtFetcher.Application.Abstractions.Repositories;
using YtFetcher.Application.Abstractions.Services;
using YtFetcher.Application.Settings;
using YtFetcher.Application.Settings.DTO;
using YtFetcher.Application.Settings.UseCases;

namespace YtFetcher.Api.Tests.Settings;

public sealed class UpdateGeneralSettingsUseCaseTests
{
    [Fact]
    public async Task UpdateAsync_rejects_invalid_values_without_writing()
    {
        var repository = new FakeGeneralSettingsRepository();
        var useCase = new UpdateGeneralSettingsUseCase(repository, new StubCurrentActor());

        var result = await useCase.UpdateAsync(
            new UpdateGeneralSettingsRequest(0, 10000, 100, 1, 0, 3, 10, -1),
            CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.NotNull(result.Errors);
        Assert.Contains(result.Errors, error => error.Field == "queriesDailyQuotaLimit");
        Assert.Contains(result.Errors, error => error.Field == "maxDailyApprovedChannels");
        Assert.Contains(result.Errors, error => error.Field == "minimumSubscriberCount");
        Assert.Equal(0, repository.UpdateCount);
    }

    [Fact]
    public async Task UpdateAsync_saves_all_values_with_the_current_actor()
    {
        var repository = new FakeGeneralSettingsRepository();
        var actor = new StubCurrentActor();
        var useCase = new UpdateGeneralSettingsUseCase(repository, actor);
        var request = new UpdateGeneralSettingsRequest(20000, 15000, 200, 2, 250, 5, 20, 1000);

        var result = await useCase.UpdateAsync(request, CancellationToken.None);

        Assert.True(result.IsValid);
        Assert.Equal(1, repository.UpdateCount);
        Assert.Equal(actor.UserId, repository.LastActorUserId);
        Assert.Equal(20000, result.Settings!.QueriesDailyQuotaLimit);
        Assert.Equal(1000, result.Settings.MinimumSubscriberCount);
    }

    private sealed class StubCurrentActor : ICurrentActor
    {
        public Guid UserId { get; } = Guid.NewGuid();
    }

    private sealed class FakeGeneralSettingsRepository : IGeneralSettingsRepository
    {
        public int UpdateCount { get; private set; }

        public Guid? LastActorUserId { get; private set; }

        public Task<GeneralSettingsSnapshot> GetRequiredAsync(CancellationToken cancellationToken) =>
            Task.FromResult(GeneralSettingsSnapshot.Default);

        public Task<GeneralSettingsSnapshot> UpdateAsync(
            GeneralSettingsSnapshot settings,
            Guid? actorUserId,
            CancellationToken cancellationToken)
        {
            UpdateCount += 1;
            LastActorUserId = actorUserId;
            return Task.FromResult(settings);
        }
    }
}
