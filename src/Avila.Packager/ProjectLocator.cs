using Avila.Security;

namespace Avila.Packager;

public static class ProjectLocator
{
    public static string ResolveProjectPath(string? explicitProjectPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitProjectPath))
        {
            return Path.GetFullPath(explicitProjectPath);
        }

        if (File.Exists(Path.Combine(Environment.CurrentDirectory, "avila.json")))
        {
            return Environment.CurrentDirectory;
        }

        var avw = Directory.GetDirectories(Environment.CurrentDirectory, "*.avw").FirstOrDefault();
        if (avw is not null)
        {
            return avw;
        }

        throw new FileNotFoundException("Could not locate an Avila project. Run inside a .avw directory or pass --project.");
    }

    public static string FindRepositoryRoot()
    {
        if (TryFindRepositoryRoot(out var root))
        {
            return root;
        }

        throw new DirectoryNotFoundException("Could not locate src/Avila.Runtime/Avila.Runtime.csproj. Set AVILA_REPO_ROOT.");
    }

    public static bool TryFindRepositoryRoot(out string root)
    {
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("AVILA_REPO_ROOT"),
            Environment.CurrentDirectory,
            AppContext.BaseDirectory
        }.Where(path => !string.IsNullOrWhiteSpace(path));

        foreach (var candidate in candidates)
        {
            var candidateRoot = WalkUpForRuntimeProject(Path.GetFullPath(candidate!));
            if (candidateRoot is not null)
            {
                root = Path.GetFullPath(candidateRoot);
                return true;
            }
        }

        root = "";
        return false;
    }

    public static string FindPackagedRuntimeDirectory()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "runtime"),
            Path.Combine(Environment.CurrentDirectory, "runtime"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "runtime"))
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(Path.Combine(candidate, "Avila.exe")))
            {
                return Path.GetFullPath(candidate);
            }
        }

        throw new DirectoryNotFoundException("Could not locate packaged runtime folder next to avila.exe.");
    }

    public static async Task<AvilaProject> LoadProjectAsync(string? explicitProjectPath, CancellationToken cancellationToken = default)
    {
        var path = ResolveProjectPath(explicitProjectPath);
        return await ManifestLoader.LoadProjectAsync(path, cancellationToken).ConfigureAwait(false);
    }

    private static string? WalkUpForRuntimeProject(string start)
    {
        var directory = Directory.Exists(start) ? new DirectoryInfo(start) : new FileInfo(start).Directory;
        while (directory is not null)
        {
            var projectPath = Path.Combine(directory.FullName, "src", "Avila.Runtime", "Avila.Runtime.csproj");
            if (File.Exists(projectPath))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
