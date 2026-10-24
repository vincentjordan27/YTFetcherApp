namespace YtFetcher.Api.Tests;

/// <summary>
/// Connection target for the PostgreSQL-only proofs. The value is supplied by the test runner as
/// <c>YTF_TEST_POSTGRES</c>; the suite never reads a local runtime configuration file for it.
/// </summary>
public static class PostgresProofTarget
{
    private const string EnvironmentVariableName = "YTF_TEST_POSTGRES";

    public static string? ConnectionString =>
        Environment.GetEnvironmentVariable(EnvironmentVariableName) is { Length: > 0 } value ? value : null;

    public static bool IsAvailable => ConnectionString is not null;
}
