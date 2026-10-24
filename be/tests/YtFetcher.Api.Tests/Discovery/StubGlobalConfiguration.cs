using Hangfire;

namespace YtFetcher.Api.Tests.Discovery;

/// <summary>
/// Marker implementation of <see cref="IGlobalConfiguration"/> for the test host. Hangfire's
/// dashboard registration requires a configured global configuration, and the one registered by
/// AddInfrastructure builds a PostgreSQL storage that opens a database connection while it is being
/// constructed. Tests run without a live database, so this empty configuration is registered instead.
/// </summary>
internal sealed class StubGlobalConfiguration : IGlobalConfiguration;
