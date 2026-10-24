namespace YtFetcher.Infrastructure.Configuration;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 5432;

    public string Name { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;
}
