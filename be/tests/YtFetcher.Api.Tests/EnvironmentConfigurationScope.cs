namespace YtFetcher.Api.Tests;

/// <summary>
/// Temporarily sets <c>ASPNETCORE_ENVIRONMENT</c> so a host test can control the hosting
/// environment that <c>WebApplication.CreateBuilder</c> reads at startup.
/// </summary>
public sealed class EnvironmentConfigurationScope : IDisposable
{
    private const string EnvironmentVariableName = "ASPNETCORE_ENVIRONMENT";

    private readonly string? previousValue;

    public EnvironmentConfigurationScope(string environmentName)
    {
        previousValue = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        Environment.SetEnvironmentVariable(EnvironmentVariableName, environmentName);
    }

    public void Dispose() =>
        Environment.SetEnvironmentVariable(EnvironmentVariableName, previousValue);
}
