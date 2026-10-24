using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Mvc;
using YtFetcher.Application.Abstractions.UseCases;
using YtFetcher.Application.BackfillRuns.DTO.Request;
using YtFetcher.Api.Endpoints.BackfillRuns.Validation;

namespace YtFetcher.Api.Endpoints.BackfillRuns;

/// <summary>
/// Read-only transport for the durable backfill run history. It binds, validates, calls one use case, and
/// maps the result to a status; it never queries a repository and never exposes a raw diagnostic.
/// </summary>
public static class BackfillRunEndpoints
{
    public static IEndpointRouteBuilder MapBackfillRunEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var runs = endpoints.MapGroup("/api/backfill-runs");

        // Paged list is a body-based action so filters can grow without changing the route.
        runs.MapPost("/list", async (
            [FromBody] BackfillRunsListRequest request,
            IValidator<BackfillRunsListRequest> validator,
            IBackfillRunUseCase backfillRunUseCase,
            CancellationToken cancellationToken) =>
        {
            var errors = await ValidateRequestAsync(validator, request, cancellationToken);
            if (errors is not null)
            {
                return Results.ValidationProblem(errors);
            }

            var response = await backfillRunUseCase.GetPageAsync(request, cancellationToken);
            return Results.Ok(response);
        });

        runs.MapGet("/{runId}", async (
            string runId,
            IBackfillRunUseCase backfillRunUseCase,
            CancellationToken cancellationToken) =>
        {
            if (!Guid.TryParse(runId, out var parsedRunId))
            {
                return InvalidRunId();
            }

            var run = await backfillRunUseCase.GetByIdAsync(parsedRunId, cancellationToken);

            return run is null ? Results.NotFound() : Results.Ok(run);
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

    private static IResult InvalidRunId() =>
        Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["runId"] = ["runId must be a valid GUID."]
        });
}
