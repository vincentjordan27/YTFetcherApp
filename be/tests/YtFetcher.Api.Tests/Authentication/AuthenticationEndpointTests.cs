using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using YtFetcher.Api.Authentication;
using YtFetcher.Core.Users;
using YtFetcher.Infrastructure.Authentication;
using YtFetcher.Infrastructure.Persistence;

namespace YtFetcher.Api.Tests.Authentication;

/// <summary>
/// The auth-enabled host contract: session exchange, cookie transport, protected endpoints, logout,
/// and the frontend-origin guard. Firebase verification is the only replaced boundary, so token issuance,
/// token validation, and user persistence are the real implementations.
/// </summary>
[Collection(ApiStartupCollection.Name)]
public class AuthenticationEndpointTests
{
    private const string AcceptedToken = "accepted-firebase-id-token";
    private const string FirebaseUid = "firebase-uid-1";
    private const string SessionCookieName = AuthenticationCookie.Name;

    [Fact]
    public async Task Config_endpoint_is_not_exposed_when_firebase_web_configuration_belongs_to_the_frontend()
    {
        using var hostConfiguration = new HostConfigurationScope();
        using var environment = DevelopmentEnabled();
        using var factory = new AuthenticationApiFactory();
        factory.FirebaseVerifier.Accept(AcceptedToken, FirebaseUid, "operator@example.test");
        using var client = CreateClient(factory);

        var login = await client.PostAsJsonAsync("/api/auth/session", new { firebaseIdToken = AcceptedToken });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/config");
        request.Headers.Add("Cookie", CookiePair(SingleSetCookie(login)));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Session_exchange_creates_exactly_one_user_updates_last_login_and_sets_the_cookie()
    {
        // The host scope first, then the auth scope: the later scope must win the shared environment variables.
        using var hostConfiguration = new HostConfigurationScope();
        using var environment = DevelopmentEnabled();
        using var factory = new AuthenticationApiFactory();
        factory.FirebaseVerifier.Accept(AcceptedToken, FirebaseUid, "Operator@Example.test", "Operator One");
        using var client = CreateClient(factory);

        var response = await client.PostAsJsonAsync(
            "/api/auth/session",
            new { firebaseIdToken = AcceptedToken });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.True(root.GetProperty("authenticated").GetBoolean());
        Assert.Equal("Operator@Example.test", root.GetProperty("identity").GetProperty("email").GetString());
        Assert.Equal("ADMIN", root.GetProperty("identity").GetProperty("role").GetString());

        var cookie = SingleSetCookie(response);
        Assert.StartsWith($"{SessionCookieName}=", cookie, StringComparison.Ordinal);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", cookie, StringComparison.OrdinalIgnoreCase);

        var users = await factory.WithDatabaseAsync(context => context.Users.ToListAsync());

        var created = Assert.Single(users);
        Assert.Equal(FirebaseUid, created.FirebaseUid);
        Assert.Equal("Operator@Example.test", created.Email);
        Assert.Equal(User.AdminRole, created.Role);
        Assert.True(created.IsActive);
        Assert.False(created.IsDeleted);
        Assert.NotNull(created.LastLoginAt);
    }

    [Fact]
    public async Task A_later_session_exchange_updates_last_login_without_creating_a_second_user()
    {
        // The host scope first, then the auth scope: the later scope must win the shared environment variables.
        using var hostConfiguration = new HostConfigurationScope();
        using var environment = DevelopmentEnabled();
        using var factory = new AuthenticationApiFactory();
        factory.FirebaseVerifier.Accept(AcceptedToken, FirebaseUid, "operator@example.test");
        using var client = CreateClient(factory);

        await client.PostAsJsonAsync("/api/auth/session", new { firebaseIdToken = AcceptedToken });

        var firstLoginAt = await factory.WithDatabaseAsync(context =>
            context.Users.Select(user => user.LastLoginAt).SingleAsync());

        await Task.Delay(15);
        var second = await client.PostAsJsonAsync("/api/auth/session", new { firebaseIdToken = AcceptedToken });

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var users = await factory.WithDatabaseAsync(context => context.Users.ToListAsync());

        var stored = Assert.Single(users);
        Assert.True(stored.LastLoginAt > firstLoginAt);
    }

    [Fact]
    public async Task An_unknown_firebase_token_is_denied_without_a_cookie()
    {
        // The host scope first, then the auth scope: the later scope must win the shared environment variables.
        using var hostConfiguration = new HostConfigurationScope();
        using var environment = DevelopmentEnabled();
        using var factory = new AuthenticationApiFactory();
        using var client = CreateClient(factory);

        var response = await client.PostAsJsonAsync(
            "/api/auth/session",
            new { firebaseIdToken = "unverifiable-token" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Empty(await factory.WithDatabaseAsync(context => context.Users.ToListAsync()));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task An_inactive_or_soft_deleted_user_is_denied(bool isActive, bool isDeleted)
    {
        // The host scope first, then the auth scope: the later scope must win the shared environment variables.
        using var hostConfiguration = new HostConfigurationScope();
        using var environment = DevelopmentEnabled();
        using var factory = new AuthenticationApiFactory();
        factory.FirebaseVerifier.Accept(AcceptedToken, FirebaseUid, "blocked@example.test");

        await factory.WithDatabaseAsync(async context =>
        {
            var now = DateTimeOffset.UtcNow;
            context.Users.Add(new User
            {
                Id = Guid.NewGuid(),
                FirebaseUid = FirebaseUid,
                Email = "blocked@example.test",
                Role = User.AdminRole,
                IsActive = isActive,
                IsDeleted = isDeleted,
                DeletedAt = isDeleted ? now : null,
                CreatedAt = now,
                ModifiedAt = now
            });

            await context.SaveChangesAsync();
        });

        using var client = CreateClient(factory);
        var response = await client.PostAsJsonAsync(
            "/api/auth/session",
            new { firebaseIdToken = AcceptedToken });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task A_repeated_first_session_converges_on_the_stored_user()
    {
        // The host scope first, then the auth scope: the later scope must win the shared environment variables.
        using var hostConfiguration = new HostConfigurationScope();
        using var environment = DevelopmentEnabled();
        using var factory = new AuthenticationApiFactory();
        factory.FirebaseVerifier.Accept(AcceptedToken, FirebaseUid, "concurrent@example.test");
        using var client = CreateClient(factory);

        // The Firebase UID is the durable identity key, so a first session that is exchanged again must reuse
        // the stored row. The true concurrent race is proven by UserPersistencePostgresTests, because only
        // PostgreSQL enforces the unique index that makes it converge.
        var first = await client.PostAsJsonAsync("/api/auth/session", new { firebaseIdToken = AcceptedToken });
        var second = await client.PostAsJsonAsync("/api/auth/session", new { firebaseIdToken = AcceptedToken });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var users = await factory.WithDatabaseAsync(context => context.Users.ToListAsync());

        Assert.Single(users);
        Assert.True(first.Headers.Contains("Set-Cookie"));
        Assert.True(second.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task Protected_endpoints_reject_a_missing_invalid_foreign_or_expired_cookie()
    {
        // The host scope first, then the auth scope: the later scope must win the shared environment variables.
        using var hostConfiguration = new HostConfigurationScope();
        using var environment = DevelopmentEnabled();
        using var factory = new AuthenticationApiFactory();
        using var client = CreateClient(factory);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/usage/today")).StatusCode);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await GetWithSessionCookieAsync(client, "not-a-jwt")).StatusCode);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await GetWithSessionCookieAsync(client, CreateToken("a-different-signing-key-0123456789abcdef"))).StatusCode);

        var expired = CreateToken(
            AuthenticationConfigurationScope.SigningKey,
            expiresAt: DateTime.UtcNow.AddHours(-2),
            notBefore: DateTime.UtcNow.AddHours(-4),
            issuedAt: DateTime.UtcNow.AddHours(-4));

        Assert.Equal(HttpStatusCode.Unauthorized, (await GetWithSessionCookieAsync(client, expired)).StatusCode);
    }

    [Fact]
    public async Task A_valid_session_cookie_authenticates_a_protected_endpoint()
    {
        // The host scope first, then the auth scope: the later scope must win the shared environment variables.
        using var hostConfiguration = new HostConfigurationScope();
        using var environment = DevelopmentEnabled();
        using var factory = new AuthenticationApiFactory();
        factory.FirebaseVerifier.Accept(AcceptedToken, FirebaseUid, "operator@example.test");
        using var client = CreateClient(factory);

        var login = await client.PostAsJsonAsync("/api/auth/session", new { firebaseIdToken = AcceptedToken });
        var cookiePair = CookiePair(SingleSetCookie(login));

        var response = await GetWithSessionCookieAsync(client, cookiePair.Replace($"{SessionCookieName}=", string.Empty, StringComparison.Ordinal));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Session_lookup_reports_unauthenticated_then_the_current_identity()
    {
        // The host scope first, then the auth scope: the later scope must win the shared environment variables.
        using var hostConfiguration = new HostConfigurationScope();
        using var environment = DevelopmentEnabled();
        using var factory = new AuthenticationApiFactory();
        factory.FirebaseVerifier.Accept(AcceptedToken, FirebaseUid, "operator@example.test");
        using var client = CreateClient(factory);

        var anonymous = await client.GetAsync("/api/auth/session");

        using var anonymousDocument = JsonDocument.Parse(await anonymous.Content.ReadAsStringAsync());
        Assert.True(anonymousDocument.RootElement.GetProperty("authenticationEnabled").GetBoolean());
        Assert.False(anonymousDocument.RootElement.GetProperty("authenticated").GetBoolean());
        Assert.Equal(JsonValueKind.Null, anonymousDocument.RootElement.GetProperty("identity").ValueKind);

        var login = await client.PostAsJsonAsync("/api/auth/session", new { firebaseIdToken = AcceptedToken });
        var token = CookiePair(SingleSetCookie(login)).Replace($"{SessionCookieName}=", string.Empty, StringComparison.Ordinal);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/session");
        request.Headers.Add("Cookie", $"{SessionCookieName}={token}");
        var authenticated = await client.SendAsync(request);

        using var authenticatedDocument = JsonDocument.Parse(await authenticated.Content.ReadAsStringAsync());
        Assert.True(authenticatedDocument.RootElement.GetProperty("authenticated").GetBoolean());
        Assert.Equal(
            "operator@example.test",
            authenticatedDocument.RootElement.GetProperty("identity").GetProperty("email").GetString());
    }

    [Fact]
    public async Task Logout_clears_only_the_session_cookie_and_is_idempotent()
    {
        // The host scope first, then the auth scope: the later scope must win the shared environment variables.
        using var hostConfiguration = new HostConfigurationScope();
        using var environment = DevelopmentEnabled();
        using var factory = new AuthenticationApiFactory();
        factory.FirebaseVerifier.Accept(AcceptedToken, FirebaseUid, "operator@example.test");
        using var client = CreateClient(factory);

        var login = await client.PostAsJsonAsync("/api/auth/session", new { firebaseIdToken = AcceptedToken });
        var token = CookiePair(SingleSetCookie(login)).Replace($"{SessionCookieName}=", string.Empty, StringComparison.Ordinal);

        var first = await SendLogoutAsync(client, token);

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        var clearing = SingleSetCookie(first);
        Assert.StartsWith($"{SessionCookieName}=", clearing, StringComparison.Ordinal);
        Assert.Contains("expires=thu, 01 jan 1970", clearing, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", clearing, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", clearing, StringComparison.OrdinalIgnoreCase);

        // Logout without a cookie at all must be equally safe.
        var second = await SendLogoutAsync(client, sessionToken: null);

        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
    }

    [Fact]
    public async Task A_state_changing_request_with_a_cookie_from_an_unconfigured_origin_is_rejected()
    {
        // The host scope first, then the auth scope: the later scope must win the shared environment variables.
        using var hostConfiguration = new HostConfigurationScope();
        using var environment = DevelopmentEnabled();
        using var factory = new AuthenticationApiFactory();
        factory.FirebaseVerifier.Accept(AcceptedToken, FirebaseUid, "operator@example.test");
        using var client = CreateClient(factory);

        var login = await client.PostAsJsonAsync("/api/auth/session", new { firebaseIdToken = AcceptedToken });
        var token = CookiePair(SingleSetCookie(login)).Replace($"{SessionCookieName}=", string.Empty, StringComparison.Ordinal);

        var foreign = await SendPendingListAsync(client, token, "https://evil.example");
        var missing = await SendPendingListAsync(client, token, origin: null);
        var allowed = await SendPendingListAsync(client, token, AuthenticationConfigurationScope.FrontendOrigin);

        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, missing.StatusCode);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    [Fact]
    public async Task Health_stays_anonymous_while_authentication_is_enabled()
    {
        // The host scope first, then the auth scope: the later scope must win the shared environment variables.
        using var hostConfiguration = new HostConfigurationScope();
        using var environment = DevelopmentEnabled();
        using var factory = new AuthenticationApiFactory();
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static AuthenticationConfigurationScope DevelopmentEnabled() => new();

    private static HttpClient CreateClient(AuthenticationApiFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    private static Task<HttpResponseMessage> GetWithSessionCookieAsync(HttpClient client, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/usage/today");
        request.Headers.Add("Cookie", $"{SessionCookieName}={token}");

        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> SendLogoutAsync(HttpClient client, string? sessionToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout")
        {
            Content = JsonContent.Create(new { })
        };

        if (sessionToken is not null)
        {
            request.Headers.Add("Cookie", $"{SessionCookieName}={sessionToken}");
            // A cookie-authenticated state change must carry the configured frontend origin.
            request.Headers.Add("Origin", AuthenticationConfigurationScope.FrontendOrigin);
        }

        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> SendPendingListAsync(HttpClient client, string token, string? origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/channels/pending/list")
        {
            Content = JsonContent.Create(new { page = 1, pageSize = 10, searchText = (string?)null })
        };
        request.Headers.Add("Cookie", $"{SessionCookieName}={token}");

        if (origin is not null)
        {
            request.Headers.Add("Origin", origin);
        }

        return client.SendAsync(request);
    }

    private static string SingleSetCookie(HttpResponseMessage response) =>
        Assert.Single(response.Headers.GetValues("Set-Cookie"));

    private static string CookiePair(string setCookie) => setCookie.Split(';')[0];

    private static string CreateToken(
        string signingKey,
        DateTime? expiresAt = null,
        DateTime? notBefore = null,
        DateTime? issuedAt = null)
    {
        var now = DateTime.UtcNow;

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = AuthenticationConfigurationScope.Issuer,
            Audience = AuthenticationConfigurationScope.Audience,
            IssuedAt = issuedAt ?? now,
            NotBefore = notBefore ?? now,
            Expires = expiresAt ?? now.AddMinutes(30),
            Claims = new Dictionary<string, object>
            {
                [SessionClaimTypes.UserId] = Guid.NewGuid().ToString(),
                [SessionClaimTypes.FirebaseUid] = FirebaseUid,
                [SessionClaimTypes.Email] = "operator@example.test",
                [SessionClaimTypes.Role] = User.AdminRole
            },
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                SecurityAlgorithms.HmacSha256)
        });
    }
}
