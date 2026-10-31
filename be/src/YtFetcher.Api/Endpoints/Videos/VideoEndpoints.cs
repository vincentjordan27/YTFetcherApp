using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using YtFetcher.Api.Endpoints.Videos.Validation;
using YtFetcher.Application.Abstractions.UseCases;
using YtFetcher.Application.Videos.DTO.Request;

namespace YtFetcher.Api.Endpoints.Videos;

public static class VideoEndpoints
{
    public static IEndpointRouteBuilder MapVideoEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var videos = endpoints.MapGroup("/api/videos");

        // Body-based list action, so filters and sorting can grow without changing the route.
        videos.MapPost("/active/list", async (
            [FromBody] ActiveVideosRequest request,
            IValidator<ActiveVideosRequest> validator,
            IVideoUseCase videoUseCase,
            CancellationToken cancellationToken) =>
        {
            var validation = await validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid)
            {
                return Results.ValidationProblem(validation.ToDictionary());
            }

            return Results.Ok(await videoUseCase.GetActiveAsync(request, cancellationToken));
        });

        // All-or-nothing bulk mutation: one invalid row rolls back every selected row.
        videos.MapPost("/mark-reference", async (
            [FromBody] MarkVideosReferenceRequest request,
            IValidator<MarkVideosReferenceRequest> validator,
            IVideoUseCase videoUseCase,
            CancellationToken cancellationToken) =>
        {
            var validation = await validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid)
            {
                return Results.ValidationProblem(validation.ToDictionary());
            }

            var marked = await videoUseCase.MarkAsReferenceAsync(request, cancellationToken);
            return marked
                ? Results.NoContent()
                : Results.Problem(
                    detail: "One or more selected videos are no longer active. Refresh the list and try again.",
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Video status conflict");
        });

        // Per-row removal from Reference. The row lock and the Core mature-stop decision are one
        // transaction; a row that is missing, soft deleted, or no longer reference is a readable 404.
        videos.MapPost("/unmark-reference", async (
            [FromBody] UnmarkVideoReferenceRequest request,
            IValidator<UnmarkVideoReferenceRequest> validator,
            IVideoUseCase videoUseCase,
            CancellationToken cancellationToken) =>
        {
            var validation = await validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid)
            {
                return Results.ValidationProblem(validation.ToDictionary());
            }

            var removed = await videoUseCase.RemoveReferenceAsync(request, cancellationToken);
            return removed
                ? Results.NoContent()
                : Results.Problem(
                    detail: "This video is no longer in the reference list. Refresh the list and try again.",
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Reference video not found");
        });

        videos.MapPost("/reference/set-used", async (
            [FromBody] SetVideoReferenceUsedRequest request,
            IValidator<SetVideoReferenceUsedRequest> validator,
            IVideoUseCase videoUseCase,
            CancellationToken cancellationToken) =>
        {
            var validation = await validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid)
            {
                return Results.ValidationProblem(validation.ToDictionary());
            }

            var updated = await videoUseCase.SetReferenceUsedAsync(request, cancellationToken);
            return updated
                ? Results.NoContent()
                : Results.Problem(
                    detail: "This video is no longer in the reference list. Refresh the list and try again.",
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Reference video not found");
        });

        videos.MapPost("/reference/list", async (
            [FromBody] ReferenceVideosRequest request,
            IValidator<ReferenceVideosRequest> validator,
            IVideoUseCase videoUseCase,
            CancellationToken cancellationToken) =>
        {
            var validation = await validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid)
            {
                return Results.ValidationProblem(validation.ToDictionary());
            }

            return Results.Ok(await videoUseCase.GetReferenceAsync(request, cancellationToken));
        });

        // Narrow shell badge: the active tracked-video total only. No generic navigation or summary endpoint.
        videos.MapGet("/active/count", async (
            IVideoUseCase videoUseCase,
            CancellationToken cancellationToken) =>
            Results.Ok(await videoUseCase.GetActiveCountAsync(cancellationToken)));

        // Per-video snapshot history, fetched only after the operator opens History from a row action.
        videos.MapPost("/{videoId}/history/list", async (
            string videoId,
            [FromBody] VideoHistoryRequest request,
            IValidator<VideoHistoryRequest> validator,
            IVideoUseCase videoUseCase,
            CancellationToken cancellationToken) =>
        {
            if (!Guid.TryParse(videoId, out var parsedVideoId))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["videoId"] = ["videoId must be a valid GUID."]
                });
            }

            var validation = await validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid)
            {
                return Results.ValidationProblem(validation.ToDictionary());
            }

            var history = await videoUseCase.GetHistoryAsync(parsedVideoId, request, cancellationToken);
            return history is null ? Results.NotFound() : Results.Ok(history);
        });

        return endpoints;
    }
}
