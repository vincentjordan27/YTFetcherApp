using YtFetcher.Application.Abstractions.UseCases;
using YtFetcher.Application.Authentication.DTO.Response;
using YtFetcher.Application.Authentication.DTO.Result;

namespace YtFetcher.Application.Authentication.UseCases;

/// <summary>
/// The intentionally anonymous mode. It answers the same auth contract without any Firebase verifier,
/// session issuer, or user repository, so a disabled instance registers no Firebase or JWT dependency and
/// can start with no Firebase or signing configuration at all.
/// </summary>
public sealed class DisabledAuthenticationUseCase : IAuthenticationUseCase
{
    public Task<CurrentSessionResponse> GetCurrentSessionAsync(
        Guid? applicationUserId,
        CancellationToken cancellationToken) =>
        Task.FromResult(new CurrentSessionResponse(false, false, null));

    public Task<CreateAuthenticationSessionResult> CreateSessionAsync(
        string firebaseIdToken,
        CancellationToken cancellationToken)
        => Task.FromResult(CreateAuthenticationSessionResult.Disabled());
}
