namespace YtFetcher.Api.Tests.Postgres;

/// <summary>
/// Serializes the PostgreSQL-only proofs: they share one disposable target and recreate the schema,
/// so they must never run concurrently against the same database.
/// </summary>
[CollectionDefinition(Name)]
public sealed class PostgresProofCollection
{
    public const string Name = "postgres-proof";
}
