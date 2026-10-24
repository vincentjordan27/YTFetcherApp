using Microsoft.AspNetCore.Mvc;
using YtFetcher.Application.Abstractions.Services;

namespace YtFetcher.Api.Authentication;

/// <summary>
/// Rejects state-changing requests that carry the session cookie but do not come from the configured frontend
/// origin. The cookie is sent automatically by the browser, so the Origin check is what stops another site
/// from driving an authenticated mutation. Safe methods and Hangfire's own dashboard actions are exempt.
/// </summary>
internal sealed class FrontendOriginGuardMiddleware(
    RequestDelegate next,
    IAuthenticationRuntimeConfiguration runtimeConfiguration,
    ILogger<FrontendOriginGuardMiddleware> logger)
{
    private static readonly string[] SafeMethods = ["GET", "HEAD", "OPTIONS"];

    /// <summary>
    /// Middleware entry point. It must stay public because the pipeline resolves this method by convention.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        if (!runtimeConfiguration.AuthenticationEnabled)
        {
            await next(context);
            return;
        }

        if (SafeMethods.Contains(context.Request.Method, StringComparer.OrdinalIgnoreCase) ||
            context.Request.Path.StartsWithSegments(Configuration.HangfireDashboardMapping.Path))
        {
            await next(context);
            return;
        }

        if (!context.Request.Cookies.ContainsKey(AuthenticationCookie.Name))
        {
            // Without a cookie there is no authenticated state to protect; the authentication middleware owns
            // the answer for that request.
            await next(context);
            return;
        }

        var origin = context.Request.Headers.Origin.ToString().Trim();

        if (runtimeConfiguration.AllowedFrontendOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        logger.LogWarning(
            "Rejected a state-changing session request whose Origin is missing or not configured. Category={Category} Path={Path}",
            "FrontendOriginRejected",
            context.Request.Path.Value);

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Title = "Request origin not allowed",
                Detail = "This request did not come from the configured application origin. Reload the application and try again."
            },
            context.RequestAborted);
    }
}
