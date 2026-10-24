using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using Serilog.Sinks.Grafana.Loki;

namespace YtFetcher.Api.Configuration;

public static class SerilogRegistration
{
    public static WebApplicationBuilder AddApiObservability(this WebApplicationBuilder builder)
    {
        var loggerConfiguration = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .Enrich.WithProperty("service", builder.Configuration["Service:Name"] ?? "yt-fetcher")
            .Enrich.WithProperty("environment", builder.Environment.EnvironmentName)
            // Hangfire logs normal scheduler work at Information, which grows large local log files for no
            // operator value. The override lives here because the JSON Logging section is not consumed by this
            // programmatic configuration; warning and error events from every source are never suppressed.
            .MinimumLevel.Override("Hangfire", LogEventLevel.Warning)
            // Hangfire's PostgreSQL storage writes through EF Core categories rather than Hangfire.*. Keeping
            // Microsoft at Warning prevents storage polling and dashboard query chatter from becoming logs.
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .WriteTo.Console(new RenderedCompactJsonFormatter());

        var lokiUrl = builder.Configuration["Grafana:LokiUrl"];
        var lokiUsername = builder.Configuration["Grafana:Username"];
        var lokiToken = builder.Configuration["Grafana:Token"];

        if (!string.IsNullOrWhiteSpace(lokiUrl) &&
            !string.IsNullOrWhiteSpace(lokiUsername) &&
            !string.IsNullOrWhiteSpace(lokiToken) &&
            !lokiUrl.StartsWith('<') &&
            !lokiUsername.StartsWith('<') &&
            !lokiToken.StartsWith('<'))
        {
            const string pushPath = "/loki/api/v1/push";
            var sinkUri = lokiUrl.EndsWith(pushPath, StringComparison.OrdinalIgnoreCase)
                ? lokiUrl[..^pushPath.Length]
                : lokiUrl.TrimEnd('/');

            loggerConfiguration.WriteTo.GrafanaLoki(
                sinkUri,
                labels:
                [
                    new LokiLabel { Key = "service", Value = builder.Configuration["Service:Name"] ?? "yt-fetcher" },
                    new LokiLabel { Key = "environment", Value = builder.Environment.EnvironmentName }
                ],
                credentials: new LokiCredentials { Login = lokiUsername, Password = lokiToken });
        }

        Log.Logger = loggerConfiguration.CreateLogger();

        builder.Host.UseSerilog();

        return builder;
    }
}
