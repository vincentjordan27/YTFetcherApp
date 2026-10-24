namespace YtFetcher.Infrastructure.Configuration;

/// <summary>
/// Credential-free configuration key owned by Infrastructure. It is the one key shared by the three YouTube
/// clients; it stays Infrastructure-only and is never referenced by Application or Core.
/// </summary>
public static class YouTubeConfigurationKeys
{
    public const string ApiKey = "YouTube:ApiKey";
}
