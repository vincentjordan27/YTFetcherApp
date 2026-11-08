using Microsoft.AspNetCore.Mvc;
using YtFetcher.Application.Abstractions.UseCases;
using YtFetcher.Application.Settings.DTO;

namespace YtFetcher.Api.Endpoints.Settings;

public static class GeneralSettingsEndpoints
{
    public static IEndpointRouteBuilder MapGeneralSettingsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var settings = endpoints.MapGroup("/api/settings/general");

        settings.MapGet("", async (
            IGetGeneralSettingsUseCase generalSettingsUseCase,
            CancellationToken cancellationToken) =>
            Results.Ok(await generalSettingsUseCase.GetAsync(cancellationToken)));

        settings.MapPut("", async (
            [FromBody] UpdateGeneralSettingsRequest request,
            IUpdateGeneralSettingsUseCase generalSettingsUseCase,
            CancellationToken cancellationToken) =>
        {
            var result = await generalSettingsUseCase.UpdateAsync(request, cancellationToken);
            return result.IsValid
                ? Results.Ok(result.Settings)
                : Results.ValidationProblem(result.Errors!);
        });

        return endpoints;
    }
}
