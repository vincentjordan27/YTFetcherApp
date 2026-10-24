using FirebaseAdmin;
using FirebaseAdmin.Auth;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using YtFetcher.Application.Abstractions.Services;
using YtFetcher.Application.Authentication.DTO.Result;
using YtFetcher.Infrastructure.Configuration;

namespace YtFetcher.Infrastructure.Firebase;

/// <summary>
/// Firebase Admin SDK adapter. The SDK owns Firebase signature, issuer, audience, and expiry verification;
/// this adapter additionally enforces the product's Google-provider and verified-email rules.
/// </summary>
public sealed class FirebaseIdentityVerifier(
    AuthenticationRuntimeConfiguration configuration,
    ILogger<FirebaseIdentityVerifier> logger) : IFirebaseIdentityVerifier
{
    private const string GoogleSignInProvider = "google.com";
    private readonly FirebaseAuth firebaseAuth = CreateFirebaseAuth(configuration);

    public async Task<FirebaseIdentityResult?> VerifyIdTokenAsync(
        string idToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idToken))
        {
            return null;
        }

        try
        {
            var firebaseToken = await firebaseAuth.VerifyIdTokenAsync(idToken, cancellationToken);
            return IsGoogleIdentity(firebaseToken, out var identity) ? identity : null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(
                "Firebase ID token verification could not be completed. Category={Category}",
                exception.GetType().Name);
            return null;
        }
    }

    private static bool IsGoogleIdentity(FirebaseToken token, out FirebaseIdentityResult identity)
    {
        var firebase = token.Claims.TryGetValue("firebase", out var firebaseClaim) ? firebaseClaim : null;
        var email = ReadStringClaim(token, "email");

        if (string.IsNullOrWhiteSpace(token.Uid) ||
            string.IsNullOrWhiteSpace(email) ||
            !ReadBooleanClaim(token, "email_verified") ||
            !HasGoogleProvider(firebase))
        {
            identity = null!;
            return false;
        }

        identity = new FirebaseIdentityResult(
            token.Uid,
            email.Trim(),
            ReadStringClaim(token, "name"));
        return true;
    }

    private static string? ReadStringClaim(FirebaseToken token, string name) =>
        token.Claims.TryGetValue(name, out var value) ? value as string : null;

    private static bool ReadBooleanClaim(FirebaseToken token, string name) =>
        token.Claims.TryGetValue(name, out var value) && value switch
        {
            bool booleanValue => booleanValue,
            string stringValue => bool.TryParse(stringValue, out var parsed) && parsed,
            _ => false
        };

    private static FirebaseAuth CreateFirebaseAuth(AuthenticationRuntimeConfiguration configuration)
    {
        var serviceAccountFilePath = configuration.FirebaseServiceAccountFilePath!;

        if (!Path.IsPathFullyQualified(serviceAccountFilePath) || !File.Exists(serviceAccountFilePath))
        {
            throw new InvalidOperationException(
                "Firebase Admin credential file is unavailable. Set 'Auth:Firebase:ServiceAccountFilePath' " +
                "to an absolute readable path inside the API container.");
        }

        try
        {
            var app = FirebaseApp.Create(
                new AppOptions
                {
                    Credential = GoogleCredential.FromFile(serviceAccountFilePath),
                    ProjectId = configuration.FirebaseProjectId
                });

            return FirebaseAuth.GetAuth(app);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                "Firebase Admin credential could not be initialized from " +
                "'Auth:Firebase:ServiceAccountFilePath'.",
                exception);
        }
    }

    private static bool HasGoogleProvider(object? firebaseClaim)
    {
        // Firebase Admin deserializes nested token claims through Newtonsoft.Json. Reading the JToken directly
        // avoids re-serializing a provider-owned object with System.Text.Json during a security decision.
        return firebaseClaim is JObject firebase &&
               string.Equals(
                   firebase.Value<string>("sign_in_provider"),
                   GoogleSignInProvider,
                   StringComparison.Ordinal);
    }
}
