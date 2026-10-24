using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using YtFetcher.Api.Authentication;
using YtFetcher.Api.Endpoints.Authentication.Validation;
using YtFetcher.Application.Abstractions.UseCases;
using YtFetcher.Application.Authentication.DTO.Request;
using YtFetcher.Application.Authentication.DTO.Response;
using YtFetcher.Application.Authentication.DTO.Result;

namespace YtFetcher.Api.Endpoints.Authentication;

public static class AuthenticationEndpoints
{
    public static IEndpointRouteBuilder MapAuthenticationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Anonymous in both modes: the frontend needs the session mode and the sign-in exchange before any
        // session exists, and a disabled instance must still be able to answer with a readable state.
        var authentication = endpoints.MapGroup("/api/auth").AllowAnonymous();

        authentication.MapGet("/session", async (
            HttpContext httpContext,
            IAuthenticationUseCase authenticationUseCase,
            CancellationToken cancellationToken) =>
            Results.Ok(await authenticationUseCase.GetCurrentSessionAsync(
                ReadApplicationUserId(httpContext.User),
                cancellationToken)));

        authentication.MapPost("/session", async (
            [FromBody] CreateAuthenticationSessionRequest request,
            IValidator<CreateAuthenticationSessionRequest> validator,
            IAuthenticationUseCase authenticationUseCase,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var validation = await validator.ValidateAsync(request, cancellationToken);
            if (!validation.IsValid)
            {
                return Results.ValidationProblem(validation.ToDictionary());
            }

            var result = await authenticationUseCase.CreateSessionAsync(
                request.FirebaseIdToken,
                cancellationToken);

            switch (result.Status)
            {
                case CreateAuthenticationSessionStatus.Created:
                    // The cookie is written only after the user row is durable, so the session never refers
                    // to an identity that was not persisted.
                    AuthenticationCookie.Append(httpContext.Response, result.Session!);
                    return Results.Ok(new CurrentSessionResponse(true, true, result.Identity));

                case CreateAuthenticationSessionStatus.Disabled:
                    return Results.Problem(
                        detail: "Authentication is disabled on this instance.",
                        statusCode: StatusCodes.Status409Conflict,
                        title: "Authentication disabled");

                case CreateAuthenticationSessionStatus.IdentityNotAllowed:
                    return Results.Problem(
                        detail: "This account is no longer allowed to access this instance.",
                        statusCode: StatusCodes.Status403Forbidden,
                        title: "Account not allowed");

                default:
                    return Results.Problem(
                        detail: "Google sign-in could not be verified. Try again.",
                        statusCode: StatusCodes.Status401Unauthorized,
                        title: "Sign-in not verified");
            }
        });

        // Idempotent by construction: clearing a cookie that is absent or already invalid is a no-op, and
        // Firebase client sign-out stays frontend work.
        authentication.MapPost("/logout", ([FromBody] LogoutRequest request, HttpContext httpContext) =>
        {
            AuthenticationCookie.Delete(httpContext.Response);
            return Results.NoContent();
        });

        return endpoints;
    }

    private static Guid? ReadApplicationUserId(ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId) ? userId : null;
}
