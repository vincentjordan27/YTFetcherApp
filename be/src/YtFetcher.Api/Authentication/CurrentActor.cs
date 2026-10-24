using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using YtFetcher.Application.Abstractions.Services;
using YtFetcher.Core.Users;

namespace YtFetcher.Api.Authentication;

/// <summary>Resolves the verified request identity or the durable SYSTEM actor for anonymous requests.</summary>
public sealed class CurrentActor(IHttpContextAccessor httpContextAccessor) : ICurrentActor
{
    public Guid UserId
    {
        get
        {
            var principal = httpContextAccessor.HttpContext?.User;
            if (principal?.Identity?.IsAuthenticated != true)
            {
                return User.SystemUserId;
            }

            var value = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(value, out var userId) && userId != Guid.Empty
                ? userId
                : User.SystemUserId;
        }
    }
}
