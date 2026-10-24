using Microsoft.EntityFrameworkCore;
using YtFetcher.Infrastructure.Persistence;

namespace YtFetcher.Api.Tests.Postgres;

/// <summary>
/// Resets only objects owned by the disposable proof role. PostgreSQL's EnsureDeletedAsync drops the
/// entire database, which would require CREATEDB for every proof and makes later tests lose their target.
/// The proof target is already dedicated, so schema-object cleanup gives each serialized test a fresh model
/// without deleting the database or its administrator-installed extensions.
/// </summary>
public static class PostgresProofDatabase
{
    private const string BaselineFileName = "001_initial_schema.sql";
    private const string ProvisioningFileName = "000_provision_database.sql";

    /// <summary>
    /// The workspace-root schema search pattern. .NET file-search patterns support only <c>*</c> and <c>?</c>;
    /// a bracket character-class expression is not a wildcard there and matches nothing, which would silently
    /// omit every migration. Three <c>?</c> characters match the three-digit migration prefix and nothing else.
    /// </summary>
    private const string MigrationSearchPattern = "???_*.sql";

    public static Task ResetAsync(YtFetcherDbContext dbContext) =>
        dbContext.Database.ExecuteSqlRawAsync("DROP OWNED BY CURRENT_USER CASCADE;");

    /// <summary>
    /// Recreates the proof schema from the authoritative manual baseline. The project owns its schema in
    /// <c>be/migrations/001_initial_schema.sql</c> and deliberately keeps <c>YtFetcherDbContext</c> to sets and
    /// annotations only, so a model-derived schema would be an approximation without the SQL-defined
    /// constraints and indexes that several proofs depend on. Proof-owned objects are dropped first, the
    /// baseline is located from the test output directory, and the whole script is sent as one command.
    /// </summary>
    public static async Task RecreateBaselineAsync(
        YtFetcherDbContext dbContext,
        CancellationToken cancellationToken = default)
    {
        await ResetAsync(dbContext);

        // The migration SQL is the authoritative schema, so the whole chain is applied in order: the baseline first,
        // then every sequential migration by file name. The baseline is consolidated, so proof setup applies
        // every root schema script that exists in the chain rather than assuming the baseline already contains
        // a named former migration. A model-derived schema is never used: it would miss the SQL-defined
        // constraints and indexes that several proofs assert.
        foreach (var scriptPath in SchemaScriptPaths())
        {
            var script = await File.ReadAllTextAsync(scriptPath, cancellationToken);
            await dbContext.Database.ExecuteSqlRawAsync(script, cancellationToken);
        }
    }

    /// <summary>
    /// Migration scripts in apply order: <c>001_initial_schema.sql</c> followed by every
    /// <c>00N_*.sql</c> sequential migration in file-name order. The project owns its schema in these migration
    /// files, so a proof that recreates the schema always exercises the real migration chain. The baseline is
    /// listed exactly once.
    /// </summary>
    public static IReadOnlyList<string> SchemaScriptPaths()
    {
        var baseline = LocateSchemaScript(BaselineFileName);
        var root = Path.GetDirectoryName(baseline)!;

        var migrations = Directory
            .EnumerateFiles(root, MigrationSearchPattern)
            .Where(path =>
                !string.Equals(path, baseline, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(Path.GetFileName(path), ProvisioningFileName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal);

        var scripts = new List<string> { baseline };
        scripts.AddRange(migrations);

        return scripts;
    }

    /// <summary>
    /// Locates one schema script by walking up from the test output directory to the repository's
    /// <c>migrations</c> directory. No machine path or current working directory is used.
    /// </summary>
    private static string LocateSchemaScript(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "migrations", fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"{fileName} could not be located from the test output directory.");
    }
}
