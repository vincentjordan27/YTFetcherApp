using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace YtFetcher.Api.Observability;

/// <summary>
/// Writes exactly one structured Error log for an unexpected API failure and then rethrows, so the
/// normal ASP.NET error response is unchanged. Expected outcomes (400, 404, 409) are returned as
/// results instead of exceptions and are therefore never logged here. Only safe fields are logged:
/// never request bodies, rejection reasons, connection strings, credentials, tokens, or the
/// exception message, which can contain provider detail.
/// </summary>
internal sealed class UnhandledApiExceptionLoggingMiddleware(
    RequestDelegate next,
    ILogger<UnhandledApiExceptionLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception exception)
        {
            logger.LogError(
                "UnhandledApiException {Operation} {Method} {ChannelId} {ExceptionType} {StatusCode} {TraceId}",
                context.GetEndpoint()?.DisplayName ?? context.Request.Path.Value ?? "unknown",
                context.Request.Method,
                context.Request.RouteValues.TryGetValue("channelId", out var channelId) ? channelId : null,
                exception.GetType().Name,
                StatusCodes.Status500InternalServerError,
                context.TraceIdentifier);

            throw;
        }
    }
}
