namespace OnuObservability.Integration.Tests;

internal static class SampleAssemblyResolver
{
    public static string Resolve(
        string environmentVariable,
        string projectName,
        string assemblyName)
    {
        var configured = Environment.GetEnvironmentVariable(environmentVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return RequireAssembly(Path.GetFullPath(configured));
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "OnuObservability.sln")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new DirectoryNotFoundException(
                "The OnuObservability repository root could not be located from the test output directory.");
        }

        var configuration = AppContext.BaseDirectory.Split(
                Path.DirectorySeparatorChar,
                StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(part => part is "Debug" or "Release")
            ?? "Debug";
        return RequireAssembly(Path.Combine(
            directory.FullName,
            "samples",
            projectName,
            "bin",
            configuration,
            "net10.0",
            assemblyName));
    }

    private static string RequireAssembly(string assembly)
    {
        if (!File.Exists(assembly))
        {
            throw new FileNotFoundException(
                "Build the sample before running its process integration test.",
                assembly);
        }

        return assembly;
    }
}
