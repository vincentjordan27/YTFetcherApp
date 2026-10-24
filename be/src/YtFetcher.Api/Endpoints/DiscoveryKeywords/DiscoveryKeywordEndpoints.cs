using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Mvc;
using YtFetcher.Application.Abstractions.UseCases;
using YtFetcher.Application.DiscoveryKeywords.DTO.Request;
using YtFetcher.Application.DiscoveryKeywords.DTO.Response;
using YtFetcher.Api.Endpoints.DiscoveryKeywords.Validation;

namespace YtFetcher.Api.Endpoints.DiscoveryKeywords;

public static class DiscoveryKeywordEndpoints
{
    /// <summary>HTTP field key for the keyword value. The transport owns this name.</summary>
    private const string KeywordField = "keyword";

    public static IEndpointRouteBuilder MapDiscoveryKeywordEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var keywords = endpoints.MapGroup("/api/discovery-keywords");

        // Paged list is a body-based action so filters and sorting can grow without changing the route.
        keywords.MapPost("/list", async (
            [FromBody] DiscoveryKeywordsListRequest request,
            IValidator<DiscoveryKeywordsListRequest> validator,
            IDiscoveryKeywordUseCase discoveryKeywordUseCase,
            CancellationToken cancellationToken) =>
        {
            var errors = await ValidateRequestAsync(validator, request, cancellationToken);
            if (errors is not null)
            {
                return Results.ValidationProblem(errors);
            }

            var response = await discoveryKeywordUseCase.GetPageAsync(request, cancellationToken);
            return Results.Ok(response);
        });

        keywords.MapPost("/", async (
            [FromBody] CreateDiscoveryKeywordRequest request,
            IValidator<CreateDiscoveryKeywordRequest> validator,
            IDiscoveryKeywordUseCase discoveryKeywordUseCase,
            CancellationToken cancellationToken) =>
        {
            var errors = await ValidateRequestAsync(validator, request, cancellationToken);
            if (errors is not null)
            {
                return Results.ValidationProblem(errors);
            }

            var result = await discoveryKeywordUseCase.CreateAsync(request, cancellationToken);
            return result.Status switch
            {
                DiscoveryKeywordMutationStatus.Succeeded => Results.Created(
                    $"/api/discovery-keywords/{result.Keyword!.Id}",
                    result.Keyword),
                DiscoveryKeywordMutationStatus.ValidationError => KeywordProblem(result.Message!),
                _ => KeywordConflict(result.Message!)
            };
        });

        keywords.MapPost("/{keywordId}/disable", async (
            string keywordId,
            [FromBody] DisableDiscoveryKeywordRequest request,
            IDiscoveryKeywordUseCase discoveryKeywordUseCase,
            CancellationToken cancellationToken) =>
        {
            if (!Guid.TryParse(keywordId, out var parsedKeywordId))
            {
                return InvalidKeywordId();
            }

            var result = await discoveryKeywordUseCase.DisableAsync(parsedKeywordId, cancellationToken);
            return result.Status switch
            {
                DiscoveryKeywordMutationStatus.Succeeded => Results.Ok(result.Keyword!),
                DiscoveryKeywordMutationStatus.NotFound => Results.NotFound(),
                _ => KeywordConflict(result.Message!)
            };
        });

        keywords.MapPost("/{keywordId}/enable", async (
            string keywordId,
            [FromBody] EnableDiscoveryKeywordRequest request,
            IDiscoveryKeywordUseCase discoveryKeywordUseCase,
            CancellationToken cancellationToken) =>
        {
            if (!Guid.TryParse(keywordId, out var parsedKeywordId))
            {
                return InvalidKeywordId();
            }

            var result = await discoveryKeywordUseCase.EnableAsync(parsedKeywordId, cancellationToken);
            return result.Status switch
            {
                DiscoveryKeywordMutationStatus.Succeeded => Results.Ok(result.Keyword!),
                DiscoveryKeywordMutationStatus.NotFound => Results.NotFound(),
                _ => KeywordConflict(result.Message!)
            };
        });

        // Removal is a soft delete: the row stays and keeps blocking its keyword.
        keywords.MapDelete("/{keywordId}", async (
            string keywordId,
            IDiscoveryKeywordUseCase discoveryKeywordUseCase,
            CancellationToken cancellationToken) =>
        {
            if (!Guid.TryParse(keywordId, out var parsedKeywordId))
            {
                return InvalidKeywordId();
            }

            var result = await discoveryKeywordUseCase.RemoveAsync(parsedKeywordId, cancellationToken);
            return result.Status switch
            {
                DiscoveryKeywordMutationStatus.Succeeded => Results.NoContent(),
                DiscoveryKeywordMutationStatus.NotFound => Results.NotFound(),
                _ => KeywordConflict(result.Message!)
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

    private static IResult InvalidKeywordId() =>
        Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["keywordId"] = ["keywordId must be a valid GUID."]
        });

    private static IResult KeywordProblem(string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]>
        {
            [KeywordField] = [message]
        });

    /// <summary>
    /// The duplicate sentence is owned by Application and is passed through unchanged, so the operator
    /// always reads exactly one wording for an active, disabled, or removed keyword.
    /// </summary>
    private static IResult KeywordConflict(string message) =>
        Results.Problem(
            detail: message,
            statusCode: StatusCodes.Status409Conflict,
            title: "Keyword conflict");
}
