using System.IO.Compression;
using System.Security.Cryptography;
using Avila.Core;
using Avila.Diagnostics;

namespace Avila.Packager;

public sealed record ReleaseResult(
    string ReleaseRoot,
    string BundleDirectory,
    string ZipPath,
    string Sha256Path,
    string CliExecutablePath,
    string RuntimeExecutablePath,
    string VersionText);

public sealed class ReleaseService
{
    private readonly SafeLogger _logger;
    private readonly ProcessRunner _processRunner;

    public ReleaseService(SafeLogger logger)
    {
        _logger = logger;
        _processRunner = new ProcessRunner(logger);
    }

    public async Task<ReleaseResult> PublishAsync(
        string? outputDirectory = null,
        bool sign = false,
        string? certificatePath = null,
        string? certificatePassword = null,
        string? timestampUrl = null,
        string runtimeIdentifier = "win-x64",
        CancellationToken cancellationToken = default)
    {
        var repoRoot = ProjectLocator.FindRepositoryRoot();
        var releaseRoot = ResolveReleaseRoot(repoRoot, outputDirectory);
        var versionText = Versionate.ResolveText(repoRoot);
        var label = BuildReleaseLabel(versionText, runtimeIdentifier);
        var stagingRoot = Path.Combine(releaseRoot, ".staging");
        var cliStage = Path.Combine(stagingRoot, "cli");
        var runtimeStage = Path.Combine(stagingRoot, "runtime");
        var bundleDirectory = Path.Combine(releaseRoot, label);
        var cliProject = Path.Combine(repoRoot, "src", "Avila.CLI", "Avila.CLI.csproj");
        var runtimeProject = Path.Combine(repoRoot, "src", "Avila.Runtime", "Avila.Runtime.csproj");

        ResetDirectory(releaseRoot);
        Directory.CreateDirectory(cliStage);
        Directory.CreateDirectory(runtimeStage);

        await PublishProjectAsync(cliProject, cliStage, runtimeIdentifier, repoRoot, cancellationToken).ConfigureAwait(false);
        await PublishProjectAsync(runtimeProject, runtimeStage, runtimeIdentifier, repoRoot, cancellationToken).ConfigureAwait(false);

        ResetDirectory(bundleDirectory);
        CopyDirectory(cliStage, bundleDirectory);
        var runtimeBundleDirectory = Path.Combine(bundleDirectory, "runtime");
        CopyDirectory(runtimeStage, runtimeBundleDirectory);

        CopyIfExists(Path.Combine(repoRoot, "README.md"), Path.Combine(bundleDirectory, "README.md"));
        CopyIfExists(Path.Combine(repoRoot, "SECURITY.md"), Path.Combine(bundleDirectory, "SECURITY.md"));
        await File.WriteAllTextAsync(
            Path.Combine(bundleDirectory, Versionate.FileName),
            versionText + Environment.NewLine,
            cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(
            Path.Combine(bundleDirectory, "release-notes.txt"),
            BuildReleaseNotes(versionText),
            cancellationToken).ConfigureAwait(false);

        if (sign || !string.IsNullOrWhiteSpace(certificatePath))
        {
            await SignBundleAsync(
                bundleDirectory,
                repoRoot,
                certificatePath,
                certificatePassword,
                timestampUrl,
                cancellationToken).ConfigureAwait(false);
        }

        var zipPath = Path.Combine(releaseRoot, $"{label}.zip");
        if (File.Exists(zipPath))
        {
            File.Delete(zipPath);
        }

        ZipFile.CreateFromDirectory(bundleDirectory, zipPath, CompressionLevel.Optimal, includeBaseDirectory: true);

        var sha256 = ComputeSha256(zipPath);
        var sha256Path = Path.Combine(releaseRoot, $"{label}.sha256.txt");
        await File.WriteAllTextAsync(
            sha256Path,
            $"{sha256}  {Path.GetFileName(zipPath)}{Environment.NewLine}",
            cancellationToken).ConfigureAwait(false);

        try
        {
            Directory.Delete(stagingRoot, recursive: true);
        }
        catch
        {
        }

        return new ReleaseResult(
            releaseRoot,
            bundleDirectory,
            zipPath,
            sha256Path,
            Path.Combine(bundleDirectory, "avila.exe"),
            Path.Combine(bundleDirectory, "runtime", "Avila.exe"),
            versionText);
    }

    private async Task PublishProjectAsync(
        string projectPath,
        string publishDirectory,
        string runtimeIdentifier,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var arguments = string.Join(' ', new[]
        {
            "publish",
            Quote(projectPath),
            "-c Release",
            $"-r {runtimeIdentifier}",
            $"-o {Quote(publishDirectory)}",
            "--self-contained true",
            "-p:PublishSingleFile=true",
            "-p:PublishTrimmed=false",
            "-p:PublishReadyToRun=true",
            "-p:EnableCompressionInSingleFile=true",
            "-p:DebugType=None",
            "-p:DebugSymbols=false"
        });

        var result = await _processRunner.RunAsync("dotnet", arguments, workingDirectory, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"dotnet publish failed:{Environment.NewLine}{result.StandardOutput}{Environment.NewLine}{result.StandardError}");
        }
    }

    private async Task SignBundleAsync(
        string bundleDirectory,
        string repoRoot,
        string? certificatePath,
        string? certificatePassword,
        string? timestampUrl,
        CancellationToken cancellationToken)
    {
        certificatePath ??= Environment.GetEnvironmentVariable("AVILA_SIGN_CERT_PFX");
        certificatePassword ??= Environment.GetEnvironmentVariable("AVILA_SIGN_CERT_PASSWORD");
        if (string.IsNullOrWhiteSpace(certificatePath) || string.IsNullOrWhiteSpace(certificatePassword))
        {
            throw new InvalidOperationException("Signing was requested, but AVILA_SIGN_CERT_PFX and AVILA_SIGN_CERT_PASSWORD are not configured.");
        }

        timestampUrl ??= Environment.GetEnvironmentVariable("AVILA_SIGN_TIMESTAMP_URL");
        if (string.IsNullOrWhiteSpace(timestampUrl))
        {
            timestampUrl = "http://timestamp.digicert.com";
        }

        var signtool = "signtool";
        foreach (var exe in new[]
        {
            Path.Combine(bundleDirectory, "avila.exe"),
            Path.Combine(bundleDirectory, "runtime", "Avila.exe")
        })
        {
            var arguments = string.Join(' ', new[]
            {
                "sign",
                $"/f {Quote(certificatePath)}",
                $"/p {Quote(certificatePassword)}",
                "/fd SHA256",
                $"/tr {Quote(timestampUrl)}",
                "/td SHA256",
                Quote(exe)
            });

            var result = await _processRunner.RunAsync(signtool, arguments, repoRoot, cancellationToken).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException($"signtool failed for {Path.GetFileName(exe)}:{Environment.NewLine}{result.StandardOutput}{Environment.NewLine}{result.StandardError}");
            }
        }
    }

    private static string ResolveReleaseRoot(string repoRoot, string? outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            return Path.Combine(repoRoot, "buildclear", "release");
        }

        return Path.IsPathRooted(outputDirectory)
            ? Path.GetFullPath(outputDirectory)
            : Path.GetFullPath(outputDirectory, repoRoot);
    }

    private static string BuildReleaseLabel(string versionText, string runtimeIdentifier)
    {
        var parts = versionText.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var versionPart = parts.Length > 0 ? parts[0] : Versionate.CurrentRelease;
        var labelPart = parts.Length > 1 ? parts[1] : "";
        var labelSlug = ToSlug(labelPart);
        return string.IsNullOrWhiteSpace(labelSlug)
            ? $"Avila-{versionPart}-{runtimeIdentifier}"
            : $"Avila-{versionPart}-{labelSlug}-{runtimeIdentifier}";
    }

    private static string ToSlug(string value)
    {
        var builder = new System.Text.StringBuilder();
        var previousDash = false;
        foreach (var character in value.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                previousDash = false;
                continue;
            }

            if (!previousDash)
            {
                builder.Append('-');
                previousDash = true;
            }
        }

        return builder.ToString().Trim('-');
    }

    private static void ResetDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }

        Directory.CreateDirectory(directory);
    }

    private static void CopyDirectory(string source, string target)
    {
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, directory);
            Directory.CreateDirectory(Path.Combine(target, relative));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var extension = Path.GetExtension(file);
            if (extension.Equals(".pdb", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".xml", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relative = Path.GetRelativePath(source, file);
            var destination = Path.Combine(target, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
    }

    private static void CopyIfExists(string source, string target)
    {
        if (!File.Exists(source))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(source, target, overwrite: true);
    }

    private static string BuildReleaseNotes(string versionText)
    {
        return string.Join(Environment.NewLine, new[]
        {
            $"{Versionate.CurrentRelease}",
            "",
            "Compiled engine bundle for Windows x64.",
            "Includes the Avila CLI and the packaged runtime folder.",
            $"Release marker: {versionText}"
        });
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}
