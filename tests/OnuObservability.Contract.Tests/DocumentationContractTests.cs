using System.Reflection;
using System.Text.RegularExpressions;
using OnuObservability.AspNetCore;
using OnuObservability.Hosting.Background;
using OnuObservability.Hosting.Configuration;
using OnuObservability.Mcp;

namespace OnuObservability.Contract.Tests;

public sealed partial class DocumentationContractTests
{
    private static readonly string Root = FindRepositoryRoot();
    private static readonly string[] GeneratedPathSegments =
        ["artifacts", ".packages", "bin", "obj", "TestResults"];

    [Fact]
    public void EveryPublicOptionHasOneCanonicalConfigurationEntry()
    {
        var configuration = File.ReadAllText(Path.Combine(Root, "docs", "configuration.md"));
        var optionTypes = new[]
        {
            typeof(OnuObservabilityOptions),
            typeof(AspNetCoreObservabilityOptions),
            typeof(McpObservabilityOptions),
        };

        var properties = optionTypes
            .SelectMany(type => type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            .GroupBy(property => property.Name, StringComparer.Ordinal);
        foreach (var propertyGroup in properties)
        {
            var documentedCount = Regex.Count(
                configuration,
                $"`{Regex.Escape(propertyGroup.Key)}`",
                RegexOptions.CultureInvariant);
            Assert.True(
                documentedCount == propertyGroup.Count(),
                $"Option '{propertyGroup.Key}' has {propertyGroup.Count()} public owners but {documentedCount} canonical entries.");
        }
    }

    [Fact]
    public void PublicBackgroundFieldsAndMcpCatalogHaveCanonicalOwners()
    {
        var signals = File.ReadAllText(Path.Combine(Root, "docs", "signals.md"));
        var fields = typeof(BackgroundOperationSchema)
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(property => property.PropertyType.Name == "TelemetryFieldDescriptor")
            .Select(property => property.GetValue(null))
            .OfType<OnuObservability.Schema.TelemetryFieldDescriptor>();

        Assert.All(fields, field => Assert.Contains($"`{field.Name}`", signals, StringComparison.Ordinal));
        Assert.Contains("The authoritative metric, activity, histogram, method, protocol, and event manifest is `McpTelemetrySchema`", signals, StringComparison.Ordinal);
        Assert.Contains("ASP.NET Core owner", signals, StringComparison.Ordinal);
        Assert.Contains("Core and Hosting owner", signals, StringComparison.Ordinal);
    }

    [Fact]
    public void RepositoryPackageIdsAndDependencyDirectionMatchDocumentation()
    {
        var readme = File.ReadAllText(Path.Combine(Root, "README.md"));
        var projects = new Dictionary<string, string>
        {
            ["OnuObservability"] = "src/OnuObservability/OnuObservability.csproj",
            ["OnuObservability.Hosting"] = "src/OnuObservability.Hosting/OnuObservability.Hosting.csproj",
            ["OnuObservability.AspNetCore"] = "src/OnuObservability.AspNetCore/OnuObservability.AspNetCore.csproj",
            ["OnuObservability.Mcp"] = "src/OnuObservability.Mcp/OnuObservability.Mcp.csproj",
        };

        foreach (var (packageId, projectPath) in projects)
        {
            var project = File.ReadAllText(Path.Combine(Root, projectPath));
            Assert.Contains($"<PackageId>{packageId}</PackageId>", project, StringComparison.Ordinal);
            Assert.Contains($"`{packageId}`", readme, StringComparison.Ordinal);
        }

        Assert.Contains(
            "| `OnuObservability.Hosting` | Generic Host configuration, OpenTelemetry composition, resources, exporters, and lifecycle | `OnuObservability` |",
            readme,
            StringComparison.Ordinal);
        Assert.Contains(
            "| `OnuObservability.AspNetCore` | ASP.NET Core request enrichment and bounded HTTP policy | `OnuObservability.Hosting` |",
            readme,
            StringComparison.Ordinal);
        Assert.Contains(
            "| `OnuObservability.Mcp` | MCP request/tool boundaries, tool registry, typed outcome channel, and stdio-safe profile | `OnuObservability.Hosting` |",
            readme,
            StringComparison.Ordinal);
    }

    [Fact]
    public void EveryRelativeMarkdownLinkResolves()
    {
        var markdownFiles = Directory.EnumerateFiles(Root, "*.md", SearchOption.AllDirectories)
            .Where(path => !IsGeneratedPath(path))
            .ToArray();

        foreach (var markdownFile in markdownFiles)
        {
            var directory = Path.GetDirectoryName(markdownFile)!;
            foreach (Match match in MarkdownLink().Matches(File.ReadAllText(markdownFile)))
            {
                var target = match.Groups[1].Value;
                if (Uri.TryCreate(target, UriKind.Absolute, out _) || target.StartsWith('#'))
                {
                    continue;
                }

                var pathOnly = Uri.UnescapeDataString(target.Split('#')[0]);
                Assert.True(
                    File.Exists(Path.GetFullPath(Path.Combine(directory, pathOnly))) ||
                    Directory.Exists(Path.GetFullPath(Path.Combine(directory, pathOnly))),
                    $"Broken link '{target}' in '{Path.GetRelativePath(Root, markdownFile)}'.");
            }
        }
    }

    private static bool IsGeneratedPath(string path) =>
        GeneratedPathSegments
            .Any(segment => path.Contains(
                $"{Path.DirectorySeparatorChar}{segment}{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OnuObservability.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException(
            "Could not locate the OnuObservability repository root.");
    }

    [GeneratedRegex(@"\[[^\]]+\]\(([^)]+)\)", RegexOptions.CultureInvariant)]
    private static partial Regex MarkdownLink();
}
