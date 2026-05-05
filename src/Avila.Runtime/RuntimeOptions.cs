using Avila.Security;

namespace Avila.Runtime;

public sealed class RuntimeOptions
{
    public string ProjectPath { get; init; } = "";

    public string Mode { get; init; } = "production";

    public bool? DevToolsOverride { get; init; }

    public string? BenchmarkFilePath { get; init; }

    public IReadOnlyList<string> Arguments { get; init; } = [];

    public static RuntimeOptions Parse(string[] args)
    {
        var projectPath = "";
        var mode = "production";
        bool? devTools = null;
        string? benchmarkFilePath = null;

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            switch (arg)
            {
                case "--project" when index + 1 < args.Length:
                    projectPath = args[++index];
                    break;
                case "--mode" when index + 1 < args.Length:
                    mode = args[++index];
                    break;
                case "--devtools":
                    devTools = true;
                    break;
                case "--no-devtools":
                    devTools = false;
                    break;
                case "--benchmark-file" when index + 1 < args.Length:
                    benchmarkFilePath = args[++index];
                    break;
            }
        }

        return new RuntimeOptions
        {
            ProjectPath = string.IsNullOrWhiteSpace(projectPath) ? ResolveDefaultProjectPath() : projectPath,
            Mode = mode.Equals("dev", StringComparison.OrdinalIgnoreCase) ? "dev" : "production",
            DevToolsOverride = devTools,
            BenchmarkFilePath = benchmarkFilePath,
            Arguments = args.ToArray()
        };
    }

    private static string ResolveDefaultProjectPath()
    {
        if (File.Exists(Path.Combine(AppContext.BaseDirectory, SecureBundleReader.BundleFileName))
            || File.Exists(Path.Combine(AppContext.BaseDirectory, SecureBundleReader.ManifestFileName)))
        {
            return AppContext.BaseDirectory;
        }

        var packagedApp = Path.Combine(AppContext.BaseDirectory, "app");
        if (File.Exists(Path.Combine(packagedApp, "avila.json")))
        {
            return packagedApp;
        }

        if (File.Exists(Path.Combine(Environment.CurrentDirectory, "avila.json")))
        {
            return Environment.CurrentDirectory;
        }

        var avw = Directory.GetDirectories(Environment.CurrentDirectory, "*.avw").FirstOrDefault();
        return avw ?? Environment.CurrentDirectory;
    }
}
