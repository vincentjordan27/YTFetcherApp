using FluentValidation;
using YtFetcher.Application.Authentication.DTO.Request;

namespace YtFetcher.Api.Endpoints.Authentication.Validation;

/// <summary>
/// Transport validation for the session-exchange action. The token must be present, but whether it is
/// authentic, unexpired, and backed by an acceptable application identity is decided by Firebase verification
/// and the application user state, never by a validator.
/// </summary>
public sealed class CreateAuthenticationSessionRequestValidator : AbstractValidator<CreateAuthenticationSessionRequest>
{
    public CreateAuthenticationSessionRequestValidator()
    {
        RuleFor(request => request.FirebaseIdToken)
            .NotEmpty()
            .WithMessage("firebaseIdToken is required.")
            .OverridePropertyName("firebaseIdToken");
    }
}
