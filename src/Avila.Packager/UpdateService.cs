using System.Net.Http.Headers;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avila.Core;

namespace Avila.Packager;

public sealed record UpcheckResult(
    string CurrentVersion,
    string CurrentMarker,
    string? CurrentMarkerSource,
    string LatestVersion,
    string LatestReleaseName,
    string LatestReleaseUrl,
    string? LatestAssetUrl,
    bool VersionMismatch,
    bool UpdateAvailable,
    bool HasInstallableAsset);

public sealed record InstallResult(
    string ReleaseName,
    string ReleaseVersion,
    string InstallRoot,
    string InstalledPath,
    string ExecutablePath);

public sealed record InstallProgress(string Phase, int Percent, string Message);

public sealed class UpdateService
{
    private const string RepositoryOwner = "AnThophicous";
    private const string RepositoryName = "Avila-Webview";

    private static readonly Uri LatestReleaseUri = new($"https://api.github.com/repos/{RepositoryOwner}/{RepositoryName}/releases/latest");

    private readonly HttpClient _httpClient;

    public UpdateService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? CreateDefaultHttpClient();
    }

    public async Task<UpcheckResult> UpcheckAsync(string currentMarker, string? markerSource = null, CancellationToken cancellationToken = default)
    {
        var currentVersion = ParseVersionText(Versionate.CurrentRelease);
        var markerVersion = ParseVersionText(currentMarker);
        var latest = await GetLatestReleaseAsync(cancellationToken).ConfigureAwait(false);

        var versionMismatch = !string.Equals(currentVersion.RawText, markerVersion.RawText, StringComparison.OrdinalIgnoreCase);
        var updateAvailable = latest.Version is not null
            && currentVersion.Version is not null
            && latest.Version > currentVersion.Version;

        return new UpcheckResult(
            currentVersion.RawText,
            markerVersion.RawText,
            markerSource,
            latest.Version?.ToString() ?? latest.TagName,
            latest.Name,
            latest.HtmlUrl,
            latest.ZipAssetUrl,
            versionMismatch,
            updateAvailable,
            !string.IsNullOrWhiteSpace(latest.ZipAssetUrl));
    }

    public async Task<InstallResult> InstallLatestAsync(
        string? installRoot = null,
        InstallScope scope = InstallScope.User,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var latest = await GetLatestReleaseAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(latest.ZipAssetUrl))
        {
            throw new InvalidOperationException("The latest release does not expose a downloadable ZIP asset.");
        }

        if (scope == InstallScope.Machine && !IsRunningAsAdministrator())
        {
            throw new InvalidOperationException("Machine scope installation requires administrator rights.");
        }

        var versionLabel = latest.ReleaseFolderName;
        var root = string.IsNullOrWhiteSpace(installRoot)
            ? GetDefaultInstallRoot(scope)
            : Path.GetFullPath(installRoot);
        Directory.CreateDirectory(root);

        var tempZip = Path.Combine(Path.GetTempPath(), $"avila-{Guid.NewGuid():N}.zip");
        try
        {
            progress?.Report(new InstallProgress("download", 5, "Downloading latest Avila release..."));
            using var response = await _httpClient.GetAsync(latest.ZipAssetUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using (var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var file = File.Create(tempZip))
            {
                var totalRead = 0L;
                var buffer = new byte[81920];
                var contentLength = response.Content.Headers.ContentLength;
                while (true)
                {
                    var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (read <= 0)
                    {
                        break;
                    }

                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    totalRead += read;

                    if (contentLength is > 0)
                    {
                        var percent = 5 + (int)Math.Clamp(totalRead * 65L / contentLength.Value, 0, 65);
                        progress?.Report(new InstallProgress("download", percent, "Downloading latest Avila release..."));
                    }
                }
            }

            progress?.Report(new InstallProgress("extract", 75, "Installing Avila..."));
            progress?.Report(new InstallProgress("extract", 82, "Unpacking release..."));
            progress?.Report(new InstallProgress("path", 92, "Adding Avila to PATH..."));
            System.IO.Compression.ZipFile.ExtractToDirectory(tempZip, root, overwriteFiles: true);
        }
        finally
        {
            TryDelete(tempZip);
        }

        var installedPath = Path.Combine(root, versionLabel);
        var executablePath = Path.Combine(installedPath, "avila.exe");
        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException("The installer did not produce avila.exe in the extracted release.", executablePath);
        }

        AddPathEntry(installedPath, scope);
        progress?.Report(new InstallProgress("complete", 100, "Installation completed successfully."));

        return new InstallResult(
            latest.Name,
            latest.Version?.ToString() ?? latest.TagName,
            root,
            installedPath,
            executablePath);
    }

    public async Task<GitHubReleaseInfo> GetLatestReleaseAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Avila", GetUserAgentVersion()));

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var release = await JsonSerializer.DeserializeAsync<GitHubReleaseResponse>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("GitHub release payload was empty.");

        var zipAsset = release.Assets.FirstOrDefault(asset =>
            asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

        return new GitHubReleaseInfo(
            release.TagName,
            release.Name,
            release.HtmlUrl,
            zipAsset?.BrowserDownloadUrl,
            zipAsset is null ? "" : Path.GetFileNameWithoutExtension(zipAsset.Name),
            ParseVersionText(release.Name).Version ?? ParseVersionText(release.TagName).Version,
            release.PublishedAt,
            release.TagName.TrimStart('v'));
    }

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static HttpClient CreateDefaultHttpClient()
    {
        var client = new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(30);
        return client;
    }

    private static string GetDefaultInstallRoot(InstallScope scope)
    {
        var baseDirectory = scope == InstallScope.Machine
            ? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
            : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        return Path.Combine(baseDirectory, "Avila", "Install");
    }

    private static void AddPathEntry(string installDirectory, InstallScope scope)
    {
        var target = scope == InstallScope.Machine ? EnvironmentVariableTarget.Machine : EnvironmentVariableTarget.User;
        var currentPath = Environment.GetEnvironmentVariable("Path", target) ?? "";
        var entries = currentPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        if (!entries.Any(entry => string.Equals(entry, installDirectory, StringComparison.OrdinalIgnoreCase)))
        {
            entries.Add(installDirectory);
            Environment.SetEnvironmentVariable("Path", string.Join(Path.PathSeparator, entries), target);
        }
    }

    private static bool IsRunningAsAdministrator()
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                return false;
            }

            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private static ParsedVersion ParseVersionText(string text)
    {
        var candidate = text.Split(' ', '|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? text;
        if (Version.TryParse(candidate, out var version))
        {
            return new ParsedVersion(version, text);
        }

        return new ParsedVersion(null, text);
    }

    private static string GetUserAgentVersion()
    {
        return Version.TryParse(Versionate.CurrentRelease.Split(' ', '|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault(), out var version)
            ? version.ToString()
            : "26.0.2";
    }

    private sealed record ParsedVersion(Version? Version, string RawText);
}

public sealed record GitHubReleaseInfo(
    string TagName,
    string Name,
    string HtmlUrl,
    string? ZipAssetUrl,
    string ReleaseFolderName,
    Version? Version,
    DateTimeOffset PublishedAt,
    string ReleaseLabel);

public enum InstallScope
{
    User,
    Machine
}

internal sealed class GitHubReleaseResponse
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = "";

    [JsonPropertyName("published_at")]
    public DateTimeOffset PublishedAt { get; set; }

    [JsonPropertyName("assets")]
    public GitHubAssetResponse[] Assets { get; set; } = [];
}

internal sealed class GitHubAssetResponse
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = "";
}
