namespace YtFetcher.Api.Tests.Postgres;

/// <summary>
/// Guard for the proof schema helper. A previous proof run silently omitted every sequential
/// migration because the discovery pattern used a bracket character-class expression, which .NET file search
/// does not treat as a wildcard. These tests run in the ordinary unit suite, so a broken discovery pattern fails
/// immediately instead of producing a green proof run against an outdated schema.
/// </summary>
public class PostgresProofDatabaseSchemaScriptsTests
{
    [Fact]
    public void Schema_script_paths_start_with_the_baseline_and_include_every_migration_script()
    {
        var scripts = PostgresProofDatabase.SchemaScriptPaths();

        Assert.NotEmpty(scripts);
        Assert.True(Path.IsPathRooted(scripts[0]));
        Assert.Equal("001_initial_schema.sql", Path.GetFileName(scripts[0]));
        Assert.True(File.Exists(scripts[0]));

        // The baseline is applied exactly once and no script is repeated.
        Assert.Single(scripts.Where(path => Path.GetFileName(path) == "001_initial_schema.sql"));
        Assert.Equal(scripts.Count, scripts.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        // The chain is exactly the migration scripts present on disk, in lexical filename order, so a present
        // sequential migration can never be skipped again.
        var root = Path.GetDirectoryName(scripts[0])!;
        var onDisk = Directory
            .EnumerateFiles(root, "???_*.sql")
            .Where(path => !string.Equals(Path.GetFileName(path), "000_provision_database.sql", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(onDisk, scripts.Select(Path.GetFileName).ToArray());
    }

    [Fact]
    public void The_migration_schema_search_pattern_discovers_a_present_sequential_migration()
    {
        // The defect was a pattern that matched nothing, so a present sequential migration was silently skipped.
        // This proves the wildcard semantics independently of which migrations happen to exist in the workspace.
        var root = Directory.CreateTempSubdirectory("yt-fetcher-schema-discovery");

        try
        {
            File.WriteAllText(Path.Combine(root.FullName, "001_initial_schema.sql"), string.Empty);
            File.WriteAllText(Path.Combine(root.FullName, "002_sequential_change.sql"), string.Empty);
            File.WriteAllText(Path.Combine(root.FullName, "000_provision_database.sql"), string.Empty);
            File.WriteAllText(Path.Combine(root.FullName, "notes.sql"), string.Empty);

            var discovered = Directory
                .EnumerateFiles(root.FullName, "???_*.sql")
                .Where(path => !string.Equals(Path.GetFileName(path), "000_provision_database.sql", StringComparison.OrdinalIgnoreCase))
                .Select(Path.GetFileName)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            // The three-digit schema scripts are found in order; provisioning and unrelated files are excluded.
            Assert.Equal(["001_initial_schema.sql", "002_sequential_change.sql"], discovered);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
