using System.Text.RegularExpressions;

namespace YtFetcher.Api.Tests.Architecture;

/// <summary>
/// Guards the Application and Core layers against HTTP concerns. The rejection validation boundary
/// must expose a plain message, never an HTTP response shape, HTTP result factory, or transport
/// field dictionary.
/// </summary>
public sealed partial class LayeringGuardTests
{
    private static readonly string[] ForbiddenTokens =
    [
        "Microsoft.AspNetCore",
        "IResult",
        "StatusCodes",
    ];

    [Fact]
    public void Application_and_core_sources_have_no_http_validation_constructs()
    {
        var violations = new List<string>();

        foreach (var file in ApplicationAndCoreSourceFiles())
        {
            var lines = File.ReadAllLines(file);

            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index];
                var location = $"{Path.GetRelativePath(RepositoryRoot(), file)}:{index + 1}";

                foreach (var token in ForbiddenTokens.Where(token => line.Contains(token, StringComparison.Ordinal)))
                {
                    violations.Add($"{location} uses {token}: {line.Trim()}");
                }

                if (HttpResultFactoryPattern().IsMatch(line))
                {
                    violations.Add($"{location} uses an HTTP result factory: {line.Trim()}");
                }

                if (ValidationDictionaryPattern().IsMatch(line))
                {
                    violations.Add($"{location} uses an HTTP-shaped validation dictionary: {line.Trim()}");
                }
            }
        }

        Assert.Empty(violations);
    }

    [Fact]
    public void Application_and_core_projects_do_not_reference_aspnet_core()
    {
        var violatingProjects = ProjectFiles()
            .Where(file => File.ReadAllText(file).Contains("AspNetCore", StringComparison.OrdinalIgnoreCase))
            .Select(file => Path.GetFileName(file))
            .ToArray();

        Assert.Empty(violatingProjects);
    }

    private static IEnumerable<string> ApplicationAndCoreSourceFiles() =>
        ProjectFiles()
            .Select(project => Path.GetDirectoryName(project)!)
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories));

    private static IEnumerable<string> ProjectFiles()
    {
        var root = RepositoryRoot();
        yield return Path.Combine(root, "src", "YtFetcher.Application", "YtFetcher.Application.csproj");
        yield return Path.Combine(root, "src", "YtFetcher.Core", "YtFetcher.Core.csproj");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "YtFetcher.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("The backend repository root could not be located.");
    }

    [GeneratedRegex(@"\bResults\.[A-Z]")]
    private static partial Regex HttpResultFactoryPattern();

    [GeneratedRegex(@"Dictionary<\s*string\s*,\s*string\[\]\s*>")]
    private static partial Regex ValidationDictionaryPattern();
}
