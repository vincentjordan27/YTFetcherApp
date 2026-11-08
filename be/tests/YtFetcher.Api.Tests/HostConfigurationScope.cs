using YtFetcher.Infrastructure.Persistence;

namespace YtFetcher.Api.Tests;

/// <summary>
/// Temporarily supplies the database connection values and authentication mode required for a Development
/// host to start without a local runtime configuration file.
/// </summary>
public sealed class HostConfigurationScope : IDisposable
{
    private static readonly IReadOnlyDictionary<string, string> Values =
        new Dictionary<string, string>
        {
            ["Database__Host"] = "localhost",
            ["Database__Port"] = "5432",
            ["Database__Name"] = "yt_fetcher",
            ["Database__Username"] = "yt_fetcher_admin",
            ["Database__Password"] = "test-password",
            // The authentication mode is explicit for every host test: Production requires the value, and an
            // anonymous Development host is the default this suite has always exercised.
            ["Auth__Enabled"] = "false",
            ["Auth__FrontendOrigin"] = "https://app.example.test"
        };

    private readonly Dictionary<string, string?> previousValues = [];

    public HostConfigurationScope(bool includeFrontendOrigin = true)
    {
        foreach (var (key, value) in Values)
        {
            previousValues[key] = Environment.GetEnvironmentVariable(key);
            Environment.SetEnvironmentVariable(
                key,
                includeFrontendOrigin || key != "Auth__FrontendOrigin" ? value : string.Empty);
        }
    }

    public void Dispose()
    {
        foreach (var (key, value) in previousValues)
        {
            Environment.SetEnvironmentVariable(key, value);
        }
    }
}
