using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using YtFetcher.Application.Abstractions.Services;
using YtFetcher.Application.Authentication.DTO.Result;
using YtFetcher.Core.Users;
using YtFetcher.Infrastructure.Configuration;

namespace YtFetcher.Infrastructure.Authentication;

/// <summary>
/// Issues the internal application session token. The token carries only the application identity needed for
/// authorization, is signed with the deployment's symmetric key, and expires exactly when the session cookie does.
/// </summary>
public sealed class ApplicationSessionTokenIssuer(
    AuthenticationRuntimeConfiguration configuration,
    TimeProvider timeProvider) : IApplicationSessionTokenIssuer
{
    private readonly JsonWebTokenHandler handler = new();

    public IssuedSessionToken Issue(User user)
    {
        var issuedAt = timeProvider.GetUtcNow();
        var expiresAt = issuedAt.AddMinutes(configuration.SessionLifetimeMinutes);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = configuration.JwtIssuer,
            Audience = configuration.JwtAudience,
            IssuedAt = issuedAt.UtcDateTime,
            NotBefore = issuedAt.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            Claims = new Dictionary<string, object>
            {
                [SessionClaimTypes.UserId] = user.Id.ToString(),
                [SessionClaimTypes.FirebaseUid] = user.FirebaseUid,
                [SessionClaimTypes.Email] = user.Email,
                [SessionClaimTypes.Role] = user.Role
            },
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration.JwtSigningKey!)),
                SecurityAlgorithms.HmacSha256)
        };

        return new IssuedSessionToken(handler.CreateToken(descriptor), expiresAt);
    }
}
