using YtFetcher.Core.Users;

namespace YtFetcher.Application.Authentication.DTO.Response;

/// <summary>
/// The current application identity, as consumed by the frontend shell. It deliberately carries no token,
/// no Firebase access token, and no provider claim.
/// </summary>
public sealed record AuthenticationIdentityDto(
    Guid UserId,
    string Email,
    string? DisplayName,
    string Role)
{
    public static AuthenticationIdentityDto ToIdentity(User user) =>
        new(user.Id, user.Email, user.DisplayName, user.Role);
}
