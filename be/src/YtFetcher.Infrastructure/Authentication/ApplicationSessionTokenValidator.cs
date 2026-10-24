using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using YtFetcher.Application.Abstractions.Services;
using YtFetcher.Application.Authentication.DTO.Result;
using YtFetcher.Infrastructure.Configuration;

namespace YtFetcher.Infrastructure.Authentication;

/// <summary>
/// Validates the internal application session token: issuer, audience, signing key, signature, and lifetime.
/// A token that fails any of those tests yields no principal, so a caller cannot authenticate with a forged,
/// foreign, or expired cookie.
/// </summary>
public sealed class ApplicationSessionTokenValidator : IApplicationSessionTokenValidator
{
    private readonly JsonWebTokenHandler handler = new();
    private readonly TokenValidationParameters parameters;

    public ApplicationSessionTokenValidator(AuthenticationRuntimeConfiguration configuration)
    {
        parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = configuration.JwtIssuer,
            ValidateAudience = true,
            ValidAudience = configuration.JwtAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration.JwtSigningKey!)),
            ValidateLifetime = true,
            // Only the algorithm this issuer uses is accepted, so a token cannot downgrade to "none" or switch
            // to an asymmetric algorithm whose key material this validation path does not own.
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ClockSkew = TimeSpan.FromSeconds(30),
            RoleClaimType = SessionClaimTypes.Role,
            NameClaimType = SessionClaimTypes.Email
        };
    }

    /// <summary>Returns the validated application identity, or <c>null</c> when the token must not authenticate a request.</summary>
    public async Task<ApplicationSessionIdentityResult?> ValidateAsync(string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        try
        {
            var result = await handler.ValidateTokenAsync(token, parameters);

            if (!result.IsValid || result.ClaimsIdentity is null ||
                !Guid.TryParse(result.ClaimsIdentity.FindFirst(SessionClaimTypes.UserId)?.Value, out var userId))
            {
                return null;
            }

            var email = result.ClaimsIdentity.FindFirst(SessionClaimTypes.Email)?.Value;
            var role = result.ClaimsIdentity.FindFirst(SessionClaimTypes.Role)?.Value;

            return string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(role)
                ? null
                : new ApplicationSessionIdentityResult(userId, email, role);
        }
        catch (SecurityTokenException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            // A malformed token string is a denial, never an unhandled request failure.
            return null;
        }
    }
}
