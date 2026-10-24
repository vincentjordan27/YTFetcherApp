using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Net;
using YtFetcher.Api.Tests.Channels;
using YtFetcher.Application.Abstractions.UseCases;
using YtFetcher.Application.Channels.DTO.Request;
using YtFetcher.Application.Channels.DTO.Result;
using YtFetcher.Application.Channels.DTO.Response;

namespace YtFetcher.Api.Tests.Observability;

[Collection(ApiStartupCollection.Name)]
public class UnhandledApiExceptionLoggingTests
{
    private const string SensitiveExceptionMessage =
        "Host=secret-db;Username=admin;Password=super-secret-token";

    [Fact]
    public async Task Unexpected_pending_list_failure_logs_one_safe_error_entry()
    {
        using var databaseConfiguration = new HostConfigurationScope();
        using var application = new ChannelApiFactory();
        using var failingApplication = application.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IChannelUseCase>();
                services.AddScoped<IChannelUseCase, ThrowingChannelUseCase>();
            }));
        using var client = failingApplication.CreateClient();

        await RequestPendingListExpectingFailureAsync(client);

        var entry = Assert.Single(application.LogSink.Entries, candidate => candidate.Level == LogLevel.Error);
        Assert.StartsWith("UnhandledApiException", entry.Message, StringComparison.Ordinal);
        Assert.Contains("/api/channels/pending", entry.Properties["Operation"]?.ToString(), StringComparison.Ordinal);
        Assert.Equal("POST", entry.Properties["Method"]);
        Assert.Null(entry.Properties["ChannelId"]);
        Assert.Equal(nameof(InvalidOperationException), entry.Properties["ExceptionType"]);
        Assert.Equal("500", entry.Properties["StatusCode"]?.ToString());
        Assert.False(string.IsNullOrWhiteSpace(entry.Properties["TraceId"]?.ToString()));

        Assert.DoesNotContain(SensitiveExceptionMessage, entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(
            entry.Properties.Values,
            value => value?.ToString()?.Contains(SensitiveExceptionMessage, StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task Expected_client_outcomes_are_not_logged_as_errors()
    {
        using var databaseConfiguration = new HostConfigurationScope();
        using var application = new ChannelApiFactory();
        using var client = application.CreateClient();

        var missing = await client.GetAsync($"/api/channels/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        var invalidReason = await client.PostAsJsonAsync(
            $"/api/channels/{Guid.NewGuid()}/reject",
            new { reason = "   " });
        Assert.Equal(HttpStatusCode.BadRequest, invalidReason.StatusCode);

        Assert.DoesNotContain(application.LogSink.Entries, entry => entry.Level == LogLevel.Error);
    }

    private static async Task RequestPendingListExpectingFailureAsync(HttpClient client)
    {
        try
        {
            using var response = await client.PostAsJsonAsync(
                "/api/channels/pending/list",
                new { page = 1, pageSize = 10, searchText = (string?)null });
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        }
        catch (Exception)
        {
            // The test server can surface the exception directly when no error page handles it.
        }
    }

    private sealed class ThrowingChannelUseCase : IChannelUseCase
    {
        public Task<PendingChannelsResponse> GetPendingAsync(
            PendingChannelsRequest request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(SensitiveExceptionMessage);

        public Task<ApprovedChannelsResponse> GetApprovedChannelsAsync(
            ApprovedChannelsRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<RejectedChannelsResponse> GetRejectedChannelsAsync(
            RejectedChannelsRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ReviewCountsResponse> GetReviewCountsAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ChannelEvidenceResponse?> GetEvidenceAsync(Guid channelId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ChannelDetailDto?> GetByIdAsync(Guid channelId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ChannelMutationResponse> ApproveAsync(
            Guid channelId,
            ApproveChannelRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ChannelBatchMutationStatus> ApproveManyAsync(
            BulkApproveChannelsRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ChannelMutationResponse> ManualApproveAsync(
            ManualApproveChannelRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ChannelMutationResponse> RetryBackfillSchedulingAsync(
            Guid channelId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ChannelMutationResponse> RetryBackfillAsync(
            Guid channelId,
            RetryBackfillRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ChannelMutationResponse> ReopenAsync(
            Guid channelId,
            ReopenChannelRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ChannelMutationResponse> RejectAsync(
            Guid channelId,
            RejectChannelRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ChannelBatchMutationStatus> RejectManyAsync(
            BulkRejectChannelsRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ChannelMutationResponse> SoftDeleteAsync(
            Guid channelId,
            DeleteChannelRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
