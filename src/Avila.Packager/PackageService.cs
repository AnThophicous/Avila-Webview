using System.Diagnostics;
using System.Text.Json;
using Avila.Core;
using Avila.Diagnostics;
using Avila.Security;

namespace Avila.Packager;

public sealed class PackageService
{
    private readonly SafeLogger _logger;
    private readonly BuildService _buildService;
    private readonly ProcessRunner _processRunner;

    public PackageService(SafeLogger logger)
    {
        _logger = logger;
        _buildService = new BuildService(logger);
        _processRunner = new ProcessRunner(logger);
    }

    public async Task<PackageResult> PackageAsync(string? projectPath, CancellationToken cancellationToken = default)
    {
        var build = await _buildService.BuildAsync(projectPath, cancellationToken).ConfigureAwait(false);
        var project = build.Project;
        var repoRoot = ProjectLocator.TryFindRepositoryRoot(out var detectedRepoRoot) ? detectedRepoRoot : null;
        var distDirectory = Path.Combine(project.RootPath, "dist");
        var appDirectory = Path.Combine(distDirectory, "app");
        var publishDirectory = Path.Combine(distDirectory, ".avila-runtime");

        ResetDirectory(distDirectory);
        Directory.CreateDirectory(appDirectory);
        Directory.CreateDirectory(publishDirectory);
        Directory.CreateDirectory(Path.Combine(distDirectory, "logs"));

        CopyProjectFiles(project.RootPath, appDirectory, distDirectory);

        var report = build.Report;

        if (repoRoot is not null)
        {
            var runtimeProject = Path.Combine(repoRoot, "src", "Avila.Runtime", "Avila.Runtime.csproj");
            var appIcon = ResolveAppIcon(project);
            var publishArguments = BuildPublishArguments(project.Manifest.Build, runtimeProject, publishDirectory, appIcon);
            var result = await _processRunner.RunAsync("dotnet", publishArguments, repoRoot, cancellationToken).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException($"dotnet publish failed:{Environment.NewLine}{result.StandardOutput}{Environment.NewLine}{result.StandardError}");
            }

            CopyDirectory(publishDirectory, distDirectory, path => !Path.GetFullPath(path).StartsWith(Path.GetFullPath(appDirectory), StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            var runtimeDirectory = ProjectLocator.FindPackagedRuntimeDirectory();
            CopyDirectory(runtimeDirectory, distDirectory, path => Path.GetExtension(path) is not ".pdb" and not ".xml");
            report = build.Report with { ExeMode = "folder prebuilt runtime" };
        }

        var runtimeExe = Path.Combine(distDirectory, "Avila.exe");
        var outputName = SanitizeOutputName(project.Manifest.Build.OutputName, project.Manifest.App.Name);
        var finalExe = Path.Combine(distDirectory, $"{outputName}.exe");
        if (File.Exists(runtimeExe))
        {
            if (File.Exists(finalExe))
            {
                File.Delete(finalExe);
            }

            File.Move(runtimeExe, finalExe);
        }

        if (!File.Exists(finalExe))
        {
            throw new FileNotFoundException("Package did not produce the expected runtime executable.", finalExe);
        }

        CleanPackageJunk(distDirectory);
        ValidatePackage(distDirectory);

        var metadataPath = Path.Combine(distDirectory, $"{outputName}.avwmeta.json");
        await File.WriteAllTextAsync(metadataPath, JsonSerializer.Serialize(new
        {
            generatedAt = DateTimeOffset.UtcNow,
            app = project.Manifest.App,
            build = project.Manifest.Build,
            report
        }, ManifestLoader.JsonOptions), cancellationToken).ConfigureAwait(false);

        await File.WriteAllTextAsync(
            Path.Combine(distDirectory, "build-report.txt"),
            ReportFormatter.Format(report),
            cancellationToken).ConfigureAwait(false);

        var versionText = Versionate.ResolveText(repoRoot, project.RootPath);
        await File.WriteAllTextAsync(
            Path.Combine(distDirectory, Versionate.FileName),
            versionText + Environment.NewLine,
            cancellationToken).ConfigureAwait(false);

        try
        {
            Directory.Delete(publishDirectory, recursive: true);
        }
        catch (IOException)
        {
        }

        if (repoRoot is not null)
        {
            MirrorReleaseToBuildClear(repoRoot, distDirectory);
        }

        return new PackageResult(project, finalExe, distDirectory, report);
    }

    private static string BuildPublishArguments(BuildManifest build, string runtimeProject, string publishDirectory, string? appIcon)
    {
        var args = new List<string>
        {
            "publish",
            Quote(runtimeProject),
            "-c Release",
            $"-r {build.Target}",
            $"-o {Quote(publishDirectory)}",
            $"--self-contained {build.SelfContained.ToString().ToLowerInvariant()}",
            $"-p:PublishSingleFile={build.SingleFile.ToString().ToLowerInvariant()}",
            "-p:PublishTrimmed=false",
            $"-p:PublishReadyToRun={build.ReadyToRun.ToString().ToLowerInvariant()}",
            "-p:EnableCompressionInSingleFile=true",
            "-p:DebugType=None",
            "-p:DebugSymbols=false"
        };

        if (!string.IsNullOrWhiteSpace(appIcon))
        {
            args.Add($"-p:ApplicationIcon={Quote(appIcon)}");
        }

        if (build.NativeAot)
        {
            args.Add("-p:PublishAot=true");
        }

        return string.Join(' ', args);
    }

    private static string? ResolveAppIcon(AvilaProject project)
    {
        if (string.IsNullOrWhiteSpace(project.Manifest.App.Icon))
        {
            return null;
        }

        var path = SafePath.ResolveInside(project.RootPath, project.Manifest.App.Icon);
        return File.Exists(path) ? path : null;
    }

    private static void ResetDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }

        Directory.CreateDirectory(directory);
    }

    private static void CopyProjectFiles(string sourceRoot, string targetRoot, string distDirectory)
    {
        CopyDirectory(sourceRoot, targetRoot, path =>
        {
            var fullPath = Path.GetFullPath(path);
            var dist = Path.GetFullPath(distDirectory);

            if (fullPath.StartsWith(dist, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return !HasExcludedSegment(sourceRoot, fullPath);
        });
    }

    private static bool HasExcludedSegment(string sourceRoot, string fullPath)
    {
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "bin",
            "obj",
            "build",
            "logs",
            "webview2-data",
            "node_modules",
            ".git",
            ".vs",
            ".idea"
        };

        var relative = Path.GetRelativePath(sourceRoot, fullPath);
        var segments = relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        return segments.Any(excluded.Contains);
    }

    private static void CopyDirectory(string sourceRoot, string targetRoot, Func<string, bool> include)
    {
        foreach (var directory in Directory.EnumerateDirectories(sourceRoot, "*", SearchOption.AllDirectories))
        {
            if (!include(directory))
            {
                continue;
            }

            var relative = Path.GetRelativePath(sourceRoot, directory);
            Directory.CreateDirectory(Path.Combine(targetRoot, relative));
        }

        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            if (!include(file))
            {
                continue;
            }

            var relative = Path.GetRelativePath(sourceRoot, file);
            var target = Path.Combine(targetRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static string SanitizeOutputName(string value, string fallbackAppName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var candidate = string.IsNullOrWhiteSpace(value) ? fallbackAppName : value;
        var name = new string(candidate.Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(name) ? EngineIdentity.ResolveProcessName(null, fallbackAppName) : name;
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private static void CleanPackageJunk(string distDirectory)
    {
        foreach (var file in Directory.EnumerateFiles(distDirectory, "*", SearchOption.AllDirectories))
        {
            var extension = Path.GetExtension(file);
            if (extension.Equals(".pdb", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".xml", StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(file);
            }
        }
    }

    private static void ValidatePackage(string distDirectory)
    {
        var blocked = FindDisallowedPackageFiles(distDirectory).ToArray();
        if (blocked.Length > 0)
        {
            var list = string.Join(Environment.NewLine, blocked.Select(file => Path.GetRelativePath(distDirectory, file)));
            throw new InvalidOperationException($"Package contains files that should not be distributed:{Environment.NewLine}{list}");
        }
    }

    private static void MirrorReleaseToBuildClear(string repoRoot, string distDirectory)
    {
        var buildClearRoot = Path.Combine(repoRoot, "buildclear");
        var buildClearDist = Path.Combine(buildClearRoot, "dist");
        ResetDirectory(buildClearDist);
        CopyDirectory(distDirectory, buildClearDist, _ => true);

        var versionatePath = Path.Combine(repoRoot, Versionate.FileName);
        if (File.Exists(versionatePath))
        {
            File.Copy(versionatePath, Path.Combine(buildClearRoot, Versionate.FileName), overwrite: true);
        }
    }

    public static IEnumerable<string> FindDisallowedPackageFiles(string distDirectory)
    {
        var blockedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".env",
            ".env.local",
            ".env.production",
            "secrets.json",
            "appsettings.Development.json"
        };

        var blockedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".pdb",
            ".xml",
            ".user",
            ".suo",
            ".tmp",
            ".log"
        };

        foreach (var file in Directory.EnumerateFiles(distDirectory, "*", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(file);
            var extension = Path.GetExtension(file);
            if (blockedNames.Contains(name) || blockedExtensions.Contains(extension))
            {
                yield return file;
            }
        }
    }
}
