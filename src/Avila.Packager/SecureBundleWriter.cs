using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Avila.Core;
using Avila.Security;

namespace Avila.Packager;

public sealed record SecureBundleWriteResult(
    string BundlePath,
    string ManifestPath,
    string SignaturePath,
    string PublicKeyPath,
    string Sha256Path,
    string PublicKeyBase64,
    int FileCount,
    string BundleSha256);

public sealed class SecureBundleWriter
{
    public async Task<SecureBundleWriteResult> WriteAsync(
        AvilaProject project,
        string distDirectory,
        bool removeSourceMaps,
        RSA signer,
        string publicKeyBase64,
        CancellationToken cancellationToken = default)
    {
        var stagingRoot = Path.Combine(distDirectory, ".avila-secure-stage");
        ResetDirectory(stagingRoot);

        CopyProjectFiles(project.RootPath, stagingRoot, removeSourceMaps);

        var manifest = BuildManifest(project, stagingRoot);
        var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, ManifestLoader.JsonOptions);

        var signatureBase64 = SecureBundleCrypto.SignManifest(manifestBytes, signer);

        var bundlePath = Path.Combine(distDirectory, SecureBundleReader.BundleFileName);
        if (File.Exists(bundlePath))
        {
            File.Delete(bundlePath);
        }

        ZipFile.CreateFromDirectory(stagingRoot, bundlePath, CompressionLevel.Optimal, includeBaseDirectory: false);

        var manifestPath = Path.Combine(distDirectory, SecureBundleReader.ManifestFileName);
        var signaturePath = Path.Combine(distDirectory, SecureBundleReader.SignatureFileName);
        var publicKeyPath = Path.Combine(distDirectory, SecureBundleReader.PublicKeyFileName);
        var shaPath = Path.Combine(distDirectory, $"{SecureBundleReader.BundleFileName}.sha256.txt");

        await File.WriteAllBytesAsync(manifestPath, manifestBytes, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(signaturePath, signatureBase64 + Environment.NewLine, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(publicKeyPath, publicKeyBase64 + Environment.NewLine, cancellationToken).ConfigureAwait(false);

        var bundleSha256 = ComputeSha256(bundlePath);
        await File.WriteAllTextAsync(
            shaPath,
            $"{bundleSha256}  {Path.GetFileName(bundlePath)}{Environment.NewLine}",
            cancellationToken).ConfigureAwait(false);

        try
        {
            Directory.Delete(stagingRoot, recursive: true);
        }
        catch
        {
        }

        return new SecureBundleWriteResult(
            bundlePath,
            manifestPath,
            signaturePath,
            publicKeyPath,
            shaPath,
            publicKeyBase64,
            manifest.Files.Count,
            bundleSha256);
    }

    private static SecureBundleManifest BuildManifest(AvilaProject project, string stagingRoot)
    {
        var files = Directory.EnumerateFiles(stagingRoot, "*", SearchOption.AllDirectories)
            .Select(path =>
            {
                var relative = Path.GetRelativePath(stagingRoot, path).Replace(Path.DirectorySeparatorChar, '/');
                return new SecureBundleFileEntry(relative, ComputeSha256(path), new FileInfo(path).Length);
            })
            .OrderBy(file => file.Path, StringComparer.Ordinal)
            .ToArray();

        var entry = string.IsNullOrWhiteSpace(project.Manifest.App.Entry)
            ? "src/index.html"
            : project.Manifest.App.Entry.Replace('\\', '/');

        return new SecureBundleManifest(
            SecureBundleCrypto.FormatName,
            project.Manifest.App.Id,
            project.Manifest.App.Name,
            project.Manifest.App.Version,
            entry,
            DateTimeOffset.UtcNow,
            files);
    }

    private static void ResetDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }

        Directory.CreateDirectory(directory);
    }

    private static void CopyProjectFiles(string sourceRoot, string targetRoot, bool removeSourceMaps)
    {
        var excludedSegments = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "bin",
            "obj",
            "build",
            "dist",
            "logs",
            "webview2-data",
            "node_modules",
            ".git",
            ".vs",
            ".idea"
        };

        foreach (var directory in Directory.EnumerateDirectories(sourceRoot, "*", SearchOption.AllDirectories))
        {
            if (ShouldExclude(sourceRoot, directory, excludedSegments))
            {
                continue;
            }

            var relative = Path.GetRelativePath(sourceRoot, directory);
            Directory.CreateDirectory(Path.Combine(targetRoot, relative));
        }

        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            if (removeSourceMaps && Path.GetExtension(file).Equals(".map", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (ShouldExclude(sourceRoot, file, excludedSegments))
            {
                continue;
            }

            var relative = Path.GetRelativePath(sourceRoot, file);
            var target = Path.Combine(targetRoot, relative);
            var parent = Path.GetDirectoryName(target);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                Directory.CreateDirectory(parent);
            }

            File.Copy(file, target, overwrite: true);
        }
    }

    private static bool ShouldExclude(string sourceRoot, string path, HashSet<string> excludedSegments)
    {
        var relative = Path.GetRelativePath(sourceRoot, Path.GetFullPath(path));
        var segments = relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        return segments.Any(excludedSegments.Contains);
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return ComputeSha256(stream);
    }

    private static string ComputeSha256(Stream stream)
    {
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
