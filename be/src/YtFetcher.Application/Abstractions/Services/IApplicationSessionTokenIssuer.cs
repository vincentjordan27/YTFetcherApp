using YtFetcher.Application.Authentication.DTO.Result;
using YtFetcher.Core.Users;

namespace YtFetcher.Application.Abstractions.Services;

/// <summary>
/// Issues the internal application session token for an already-authorized application user. The JWT
/// implementation, signing key, issuer, audience, and lifetime stay inside Infrastructure.
/// </summary>
public interface IApplicationSessionTokenIssuer
{
    IssuedSessionToken Issue(User user);
}
