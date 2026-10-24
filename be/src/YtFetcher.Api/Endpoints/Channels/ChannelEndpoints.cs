using FluentValidation;
using FluentValidation.Results;
using YtFetcher.Application.Abstractions.UseCases;
using YtFetcher.Application.Channels.DTO.Request;
using YtFetcher.Application.Channels.DTO.Response;
using YtFetcher.Application.Channels.DTO.Result;
using YtFetcher.Api.Endpoints.Channels.Validation;
using Microsoft.AspNetCore.Mvc;

namespace YtFetcher.Api.Endpoints.Channels;

public static class ChannelEndpoints
{
    /// <summary>HTTP field key for the rejection reason. The transport owns this name.</summary>
    private const string RejectionReasonField = "reason";

    /// <summary>HTTP field key for the manual-add handle. The transport owns this name.</summary>
    private const string ChannelReferenceField = "channelReference";

    public static IEndpointRouteBuilder MapChannelEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var channels = endpoints.MapGroup("/api/channels");

        // Paged list is a body-based action so filters and sorting can grow without changing the route.
        channels.MapPost("/pending/list", async (
            [FromBody] PendingChannelsRequest request,
            IValidator<PendingChannelsRequest> validator,
            IChannelUseCase channelUseCase,
            CancellationToken cancellationToken) =>
        {
            var errors = await ValidateRequestAsync(validator, request, cancellationToken);
            return errors is not null
                ? Results.ValidationProblem(errors)
                : Results.Ok(await channelUseCase.GetPendingAsync(request, cancellationToken));
        });

        channels.MapPost("/approved/list", async (
            [FromBody] ApprovedChannelsRequest request,
            IValidator<ApprovedChannelsRequest> validator,
            IChannelUseCase channelUseCase,
            CancellationToken cancellationToken) =>
        {
            var errors = await ValidateRequestAsync(validator, request, cancellationToken);
            return errors is not null
                ? Results.ValidationProblem(errors)
                : Results.Ok(await channelUseCase.GetApprovedChannelsAsync(request, cancellationToken));
        });

        channels.MapPost("/rejected/list", async (
            [FromBody] RejectedChannelsRequest request,
            IValidator<RejectedChannelsRequest> validator,
            IChannelUseCase channelUseCase,
            CancellationToken cancellationToken) =>
        {
            var errors = await ValidateRequestAsync(validator, request, cancellationToken);
            return errors is not null
                ? Results.ValidationProblem(errors)
                : Results.Ok(await channelUseCase.GetRejectedChannelsAsync(request, cancellationToken));
        });

        // Narrow navigation summary so every visible tab badge can be real while another tab is open.
        channels.MapGet("/review-counts", async (
            IChannelUseCase channelUseCase,
            CancellationToken cancellationToken) =>
            Results.Ok(await channelUseCase.GetReviewCountsAsync(cancellationToken)));

        channels.MapGet("/{channelId}", async (
            string channelId,
            IChannelUseCase channelUseCase,
            CancellationToken cancellationToken) =>
        {
            if (!Guid.TryParse(channelId, out var parsedChannelId))
            {
                return InvalidChannelId();
            }

            var detail = await channelUseCase.GetByIdAsync(parsedChannelId, cancellationToken);
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        });

        channels.MapGet("/{channelId}/evidence", async (
            string channelId,
            IChannelUseCase channelUseCase,
            CancellationToken cancellationToken) =>
        {
            if (!Guid.TryParse(channelId, out var parsedChannelId))
            {
                return InvalidChannelId();
            }

            var evidence = await channelUseCase.GetEvidenceAsync(parsedChannelId, cancellationToken);
            return evidence is null ? Results.NotFound() : Results.Ok(evidence);
        });

        channels.MapPost("/{channelId}/approve", async (
            string channelId,
            [FromBody] ApproveChannelRequest request,
            IChannelUseCase channelUseCase,
            CancellationToken cancellationToken) =>
        {
            if (!Guid.TryParse(channelId, out var parsedChannelId))
            {
                return InvalidChannelId();
            }

            var result = await channelUseCase.ApproveAsync(parsedChannelId, request, cancellationToken);
            return result.Status switch
            {
                ChannelMutationStatus.Succeeded => Results.Ok(result.Channel!),
                ChannelMutationStatus.NotFound => Results.NotFound(),
                ChannelMutationStatus.ValidationError => RejectionReasonProblem(result.ValidationMessage!),
                ChannelMutationStatus.QuotaWindowNotReady => QuotaWindowNotReadyProblem(),
                ChannelMutationStatus.QuotaExceededToday => QuotaLimitReachedProblem(result.ValidationMessage!),
                _ => Conflict(result.CurrentStatus)
            };
        });

        // All-or-nothing: one stale row or insufficient first-page capacity leaves every selected row unchanged.
        channels.MapPost("/approve", async (
            [FromBody] BulkApproveChannelsRequest request,
            IValidator<BulkApproveChannelsRequest> validator,
            IChannelUseCase channelUseCase,
            CancellationToken cancellationToken) =>
        {
            var errors = await ValidateRequestAsync(validator, request, cancellationToken);
            if (errors is not null)
            {
                return Results.ValidationProblem(errors);
            }

            var status = await channelUseCase.ApproveManyAsync(request, cancellationToken);
            return status switch
            {
                ChannelBatchMutationStatus.Succeeded => Results.NoContent(),
                ChannelBatchMutationStatus.NotFound => Results.NotFound(),
                ChannelBatchMutationStatus.WindowNotReady => QuotaWindowNotReadyProblem(),
                ChannelBatchMutationStatus.LimitReached => QuotaLimitReachedProblem(
                    "Maximum daily approved channels reached. Try again after the next quota reset."),
                _ => BatchConflict("One or more selected channels are no longer pending. Refresh the list and try again.")
            };
        });

        // Operator adds one channel found outside discovery, by its @handle. One channels.list lookup,
        // no search.list and no scraping; the created channel then follows the existing first-page
        // scheduling path unchanged.
        channels.MapPost("/manual-approve", async (
            [FromBody] ManualApproveChannelRequest request,
            IValidator<ManualApproveChannelRequest> validator,
            IChannelUseCase channelUseCase,
            CancellationToken cancellationToken) =>
        {
            var errors = await ValidateRequestAsync(validator, request, cancellationToken);
            if (errors is not null)
            {
                return Results.ValidationProblem(errors);
            }

            var result = await channelUseCase.ManualApproveAsync(request, cancellationToken);
            return result.Status switch
            {
                ChannelMutationStatus.Succeeded => Results.Ok(result.Channel!),
                ChannelMutationStatus.NotFound => ChannelNotFound(),
                ChannelMutationStatus.ValidationError => ManualReferenceProblem(result.ValidationMessage!),
                ChannelMutationStatus.ProviderUnavailable => ProviderUnavailable(),
                ChannelMutationStatus.QuotaWindowNotReady => QuotaWindowNotReadyProblem(),
                ChannelMutationStatus.QuotaExceededToday => QuotaLimitReachedProblem(result.ValidationMessage!),
                _ => ManualDuplicateConflict(result.CurrentStatus)
            };
        });

        channels.MapPost("/{channelId}/reject", async (
            string channelId,
            [FromBody] RejectChannelRequest request,
            IValidator<RejectChannelRequest> validator,
            IChannelUseCase channelUseCase,
            CancellationToken cancellationToken) =>
        {
            if (!Guid.TryParse(channelId, out var parsedChannelId))
            {
                return InvalidChannelId();
            }

            var errors = await ValidateRequestAsync(validator, request, cancellationToken);
            if (errors is not null)
            {
                return Results.ValidationProblem(errors);
            }

            var result = await channelUseCase.RejectAsync(parsedChannelId, request, cancellationToken);
            return result.Status switch
            {
                ChannelMutationStatus.Succeeded => Results.Ok(result.Channel!),
                ChannelMutationStatus.NotFound => Results.NotFound(),
                ChannelMutationStatus.ValidationError => RejectionReasonProblem(result.ValidationMessage!),
                _ => Conflict(result.CurrentStatus)
            };
        });

        // All selected rows receive the same operator reason or none are changed.
        channels.MapPost("/reject", async (
            [FromBody] BulkRejectChannelsRequest request,
            IValidator<BulkRejectChannelsRequest> validator,
            IChannelUseCase channelUseCase,
            CancellationToken cancellationToken) =>
        {
            var errors = await ValidateRequestAsync(validator, request, cancellationToken);
            if (errors is not null)
            {
                return Results.ValidationProblem(errors);
            }

            var status = await channelUseCase.RejectManyAsync(request, cancellationToken);
            return status switch
            {
                ChannelBatchMutationStatus.Succeeded => Results.NoContent(),
                ChannelBatchMutationStatus.NotFound => Results.NotFound(),
                _ => BatchConflict("One or more selected channels are no longer eligible for rejection. Refresh the list and try again.")
            };
        });

        channels.MapPost("/{channelId}/reopen", async (
            string channelId,
            [FromBody] ReopenChannelRequest request,
            IChannelUseCase channelUseCase,
            CancellationToken cancellationToken) =>
        {
            if (!Guid.TryParse(channelId, out var parsedChannelId))
            {
                return InvalidChannelId();
            }

            var result = await channelUseCase.ReopenAsync(parsedChannelId, request, cancellationToken);
            return result.Status switch
            {
                ChannelMutationStatus.Succeeded => Results.Ok(result.Channel!),
                ChannelMutationStatus.NotFound => Results.NotFound(),
                _ => Conflict(result.CurrentStatus, result.CurrentBackfillStatus)
            };
        });

        channels.MapDelete("/{channelId}", async (
            string channelId,
            [FromBody] DeleteChannelRequest request,
            IChannelUseCase channelUseCase,
            CancellationToken cancellationToken) =>
        {
            if (!Guid.TryParse(channelId, out var parsedChannelId))
            {
                return InvalidChannelId();
            }

            var result = await channelUseCase.SoftDeleteAsync(parsedChannelId, request, cancellationToken);
            return result.Status switch
            {
                ChannelMutationStatus.Succeeded => Results.NoContent(),
                ChannelMutationStatus.NotFound => Results.NotFound(),
                _ => Conflict(result.CurrentStatus)
            };
        });

        channels.MapPost("/{channelId}/backfill/retry", async (
            string channelId,
            [FromBody] RetryBackfillRequest request,
            IChannelUseCase channelUseCase,
            CancellationToken cancellationToken) =>
        {
            if (!Guid.TryParse(channelId, out var parsedChannelId))
            {
                return InvalidChannelId();
            }

            var result = await channelUseCase.RetryBackfillAsync(parsedChannelId, request, cancellationToken);
            return result.Status switch
            {
                ChannelMutationStatus.Succeeded => Results.Ok(result.Channel!),
                ChannelMutationStatus.NotFound => Results.NotFound(),
                _ => Conflict(result.CurrentStatus, result.CurrentBackfillStatus)
            };
        });

        return endpoints;
    }

    private static async Task<IDictionary<string, string[]>?> ValidateRequestAsync<TRequest>(
        IValidator<TRequest> validator,
        TRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        return validation.IsValid ? null : validation.ToDictionary();
    }

    private static IResult InvalidChannelId() =>
        Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["channelId"] = ["channelId must be a valid GUID."]
        });

    private static IResult RejectionReasonProblem(string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]>
        {
            [RejectionReasonField] = [message]
        });

    private static IResult ManualReferenceProblem(string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]>
        {
            [ChannelReferenceField] = [message]
        });

    private static IResult ChannelNotFound() =>
        Results.Problem(
            detail: "The YouTube channel could not be found.",
            statusCode: StatusCodes.Status404NotFound,
            title: "Channel not found");

    private static IResult ProviderUnavailable() =>
        Results.Problem(
            detail: "The YouTube channel could not be checked right now. Try again later.",
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "YouTube unavailable");

    /// <summary>HTTP 429 for either the daily approved-channel maximum or the ordinary request quota.</summary>
    private static IResult QuotaLimitReachedProblem(string message) =>
        Results.Problem(
            detail: message,
            statusCode: StatusCodes.Status429TooManyRequests,
            title: "Daily quota limit reached");

    /// <summary>HTTP 503 until Phase 0 has initialized today's first-page window.</summary>
    private static IResult QuotaWindowNotReadyProblem() =>
        Results.Problem(
            detail: "The daily quota window has not started yet. Try again shortly.",
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "Quota window not ready");

    /// <summary>
    /// Maps the durable duplicate status to the fixed readable sentence. The status is preserved in
    /// the problem extension; the persisted row is never changed.
    /// </summary>
    private static IResult ManualDuplicateConflict(string? currentStatus) =>
        Results.Problem(
            detail: currentStatus switch
            {
                "PENDING" => "This channel is already pending review.",
                "APPROVED" => "This channel is already approved.",
                "REJECTED" => "This channel was rejected and cannot be added again.",
                "DELETED" => "This channel was deleted and cannot be added again.",
                _ => "This channel could not be added because it already exists."
            },
            statusCode: StatusCodes.Status409Conflict,
            title: "Channel status conflict",
            extensions: new Dictionary<string, object?>
            {
                ["currentStatus"] = currentStatus
            });

    private static IResult Conflict(string? currentStatus, string? currentBackfillStatus = null) =>
        Results.Problem(
            detail: $"The channel is currently {currentStatus}.",
            statusCode: StatusCodes.Status409Conflict,
            title: "Channel status conflict",
            extensions: new Dictionary<string, object?>
            {
                ["currentStatus"] = currentStatus,
                ["currentBackfillStatus"] = currentBackfillStatus
            });

    private static IResult BatchConflict(string detail) =>
        Results.Problem(
            detail: detail,
            statusCode: StatusCodes.Status409Conflict,
            title: "Channel status conflict");
}
