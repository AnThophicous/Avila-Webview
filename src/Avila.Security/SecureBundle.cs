using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Avila.Security;

public sealed record SecureBundleFileEntry(
    string Path,
    string Sha256,
    long Size);

public sealed record SecureBundleManifest(
    string Format,
    string AppId,
    string AppName,
    string Version,
    string Entry,
    DateTimeOffset CreatedAt,
    IReadOnlyList<SecureBundleFileEntry> Files);

public sealed record SecureBundleVerificationResult(
    string BundlePath,
    string ManifestPath,
    string SignaturePath,
    string PublicKeyPath,
    SecureBundleManifest Manifest,
    string BundleSha256,
    int FileCount,
    string ExtractionRoot);

public static class SecureBundleCrypto
{
    public const string FormatName = "avila-secure-bundle-v1";

    public static RSA CreateSigner()
    {
        return RSA.Create(3072);
    }

    public static string ExportPublicKeyBase64(RSA rsa)
    {
        return Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo());
    }

    public static string SignManifest(byte[] manifestBytes, RSA rsa)
    {
        var signature = rsa.SignData(manifestBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        return Convert.ToBase64String(signature);
    }

    public static bool VerifyManifest(byte[] manifestBytes, string signatureBase64, string publicKeyBase64)
    {
        if (string.IsNullOrWhiteSpace(signatureBase64) || string.IsNullOrWhiteSpace(publicKeyBase64))
        {
            return false;
        }

        using var rsa = RSA.Create();
        rsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKeyBase64), out _);
        var signature = Convert.FromBase64String(signatureBase64);
        return rsa.VerifyData(manifestBytes, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
    }
}

public static class SecureBundleReader
{
    public const string BundleFileName = "app.avila.bundle";
    public const string ManifestFileName = "app.avila.bundle.manifest.json";
    public const string SignatureFileName = "app.avila.bundle.sig";
    public const string PublicKeyFileName = "app.avila.bundle.publickey.txt";

    public static async Task<SecureBundleVerificationResult> VerifyAsync(
        string bundlePathOrDirectory,
        string publicKeyBase64,
        CancellationToken cancellationToken = default)
    {
        var paths = ResolveBundlePaths(bundlePathOrDirectory);
        var manifestBytes = await File.ReadAllBytesAsync(paths.ManifestPath, cancellationToken).ConfigureAwait(false);
        var signatureBase64 = (await File.ReadAllTextAsync(paths.SignaturePath, cancellationToken).ConfigureAwait(false)).Trim();
        if (!SecureBundleCrypto.VerifyManifest(manifestBytes, signatureBase64, publicKeyBase64))
        {
            throw new InvalidOperationException("Secure bundle signature verification failed.");
        }

        var manifest = JsonSerializer.Deserialize<SecureBundleManifest>(manifestBytes, ManifestLoader.JsonOptions)
            ?? throw new InvalidOperationException("Secure bundle manifest is empty or invalid.");

        if (!manifest.Format.Equals(SecureBundleCrypto.FormatName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unexpected secure bundle format: {manifest.Format}");
        }

        var bundleSha256 = ComputeSha256(paths.BundlePath);
        var fileCount = VerifyBundleContents(paths.BundlePath, manifest);
        var extractionRoot = ResolveExtractionRoot(manifest.AppId, bundleSha256);

        return new SecureBundleVerificationResult(
            paths.BundlePath,
            paths.ManifestPath,
            paths.SignaturePath,
            paths.PublicKeyPath,
            manifest,
            bundleSha256,
            fileCount,
            extractionRoot);
    }

    public static async Task<AvilaProject> LoadProjectAsync(
        string bundlePathOrDirectory,
        string publicKeyBase64,
        CancellationToken cancellationToken = default)
    {
        var verification = await VerifyAsync(bundlePathOrDirectory, publicKeyBase64, cancellationToken).ConfigureAwait(false);
        ExtractBundle(verification.BundlePath, verification.ExtractionRoot, verification.Manifest);

        var manifestPath = Path.Combine(verification.ExtractionRoot, "avila.json");
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("Secure bundle did not contain avila.json.", manifestPath);
        }

        var project = await ManifestLoader.LoadProjectAsync(verification.ExtractionRoot, cancellationToken: cancellationToken).ConfigureAwait(false);
        return project with
        {
            BundlePath = verification.BundlePath,
            BundleManifestPath = verification.ManifestPath
        };
    }

    public static string ResolveBundlePath(string bundlePathOrDirectory)
    {
        var fullPath = Path.GetFullPath(bundlePathOrDirectory);
        if (File.Exists(fullPath))
        {
            return fullPath;
        }

        if (Directory.Exists(fullPath))
        {
            var candidate = Path.Combine(fullPath, BundleFileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("Could not locate app.avila.bundle.", fullPath);
    }

    public static string ResolveSidecarPath(string bundlePath, string fileName)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(bundlePath)) ?? Environment.CurrentDirectory;
        return Path.Combine(directory, fileName);
    }

    private static (string BundlePath, string ManifestPath, string SignaturePath, string PublicKeyPath) ResolveBundlePaths(string bundlePathOrDirectory)
    {
        var bundlePath = ResolveBundlePath(bundlePathOrDirectory);
        var manifestPath = ResolveSidecarPath(bundlePath, ManifestFileName);
        var signaturePath = ResolveSidecarPath(bundlePath, SignatureFileName);
        var publicKeyPath = ResolveSidecarPath(bundlePath, PublicKeyFileName);

        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("Secure bundle manifest was not found.", manifestPath);
        }

        if (!File.Exists(signaturePath))
        {
            throw new FileNotFoundException("Secure bundle signature was not found.", signaturePath);
        }

        return (bundlePath, manifestPath, signaturePath, publicKeyPath);
    }

    private static int VerifyBundleContents(string bundlePath, SecureBundleManifest manifest)
    {
        using var archive = ZipFile.OpenRead(bundlePath);
        var count = 0;
        var expectedEntries = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in manifest.Files)
        {
            var relativePath = NormalizeBundlePath(file.Path);
            ValidateBundleRelativePath(relativePath);
            if (!expectedEntries.Add(relativePath))
            {
                throw new InvalidOperationException($"Secure bundle manifest contains a duplicate entry: {file.Path}");
            }

            var entry = archive.GetEntry(relativePath)
                ?? throw new InvalidOperationException($"Secure bundle entry was not found: {file.Path}");

            using var stream = entry.Open();
            var sha256 = ComputeSha256(stream);
            if (!sha256.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Secure bundle hash mismatch for {file.Path}.");
            }

            count++;
        }

        var actualFiles = archive.Entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
            .Select(entry => NormalizeBundlePath(entry.FullName))
            .ToArray();

        if (actualFiles.Length != expectedEntries.Count
            || actualFiles.Any(entry => !expectedEntries.Contains(entry)))
        {
            throw new InvalidOperationException("Secure bundle archive contains unexpected files.");
        }

        return count;
    }

    private static void ExtractBundle(string bundlePath, string extractionRoot, SecureBundleManifest manifest)
    {
        if (Directory.Exists(extractionRoot))
        {
            Directory.Delete(extractionRoot, recursive: true);
        }

        Directory.CreateDirectory(extractionRoot);

        using var archive = ZipFile.OpenRead(bundlePath);
        foreach (var file in manifest.Files)
        {
            var relativePath = NormalizeBundlePath(file.Path);
            ValidateBundleRelativePath(relativePath);
            var entry = archive.GetEntry(relativePath)
                ?? throw new InvalidOperationException($"Secure bundle entry was not found: {file.Path}");

            var destination = Path.GetFullPath(Path.Combine(extractionRoot, file.Path.Replace('/', Path.DirectorySeparatorChar)));
            if (!IsInsideRoot(extractionRoot, destination))
            {
                throw new InvalidOperationException($"Secure bundle entry escapes the extraction root: {file.Path}");
            }

            var destinationDirectory = Path.GetDirectoryName(destination);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            using var input = entry.Open();
            using var output = File.Create(destination);
            input.CopyTo(output);
        }
    }

    private static string ResolveExtractionRoot(string appId, string bundleSha256)
    {
        var safeAppId = ToSafeSegment(appId);
        var root = Path.Combine(Path.GetTempPath(), "Avila", "bundles", safeAppId, bundleSha256);
        Directory.CreateDirectory(root);
        return root;
    }

    private static string ToSafeSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(character => invalid.Contains(character) ? '_' : character).ToArray();
        var result = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(result) ? "app" : result;
    }

    private static string NormalizeBundlePath(string path) => path.Replace('\\', '/');

    private static void ValidateBundleRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("Secure bundle entry path is empty.");
        }

        if (path.StartsWith('/') || path.Contains("..", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Secure bundle entry path is not safe: {path}");
        }
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

    private static bool IsInsideRoot(string root, string candidate)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var normalizedCandidate = Path.GetFullPath(candidate);

        return normalizedCandidate.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase)
            || normalizedCandidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
