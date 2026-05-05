using Avila.Core;
using Avila.Diagnostics;
using Avila.Packager;
using Avila.Security;
using System.Diagnostics;

var command = args.Length == 0 ? "help" : args[0].ToLowerInvariant();
var options = CliOptions.Parse(args.Skip(1).ToArray());
var logger = new SafeLogger(verbose: options.Verbose || options.Debug);

try
{
    switch (command)
    {
        case "new":
        case "create":
            await CreateAsync(options);
            break;
        case "init":
            await CreateAppAsync(options, legacyInit: true);
            break;
        case "dev":
            await RunRuntimeAsync(options, "dev");
            break;
        case "run":
            await RunRuntimeAsync(options, "production");
            break;
        case "build":
            await BuildAsync(options);
            break;
        case "package":
            await PackageAsync(options);
            break;
        case "verify":
            await VerifyBundleAsync(options);
            break;
        case "check":
            await CheckAsync(options);
            break;
        case "audit":
            await AuditAsync(options);
            break;
        case "publish":
            await PublishReleaseAsync(options);
            break;
        case "benchmark":
            await BenchmarkAsync(options);
            break;
        case "upcheck":
            await UpcheckAsync(options);
            break;
        case "upgrade":
        case "install":
            await UpgradeAsync(options);
            break;
        case "version":
        case "--version":
            PrintVersion();
            break;
        case "doctor":
            await DoctorAsync(options);
            break;
        case "inspect":
            await InspectAsync(options);
            break;
        case "help":
        case "--help":
        case "-h":
            PrintHelp();
            break;
        default:
            Console.Error.WriteLine($"Unknown command: {command}");
            PrintHelp();
            Environment.ExitCode = 1;
            break;
    }
}
catch (Exception exception)
{
    Console.Error.WriteLine(logger.Sanitize(exception.Message));
    Environment.ExitCode = 1;
}

async Task CreateAsync(CliOptions cliOptions)
{
    var subcommand = cliOptions.Positionals.FirstOrDefault();
    if (string.IsNullOrWhiteSpace(subcommand))
    {
        throw new ArgumentException("Usage: avila create app <app-name> | avila create <app-name> [--url <site>] [--template <name>]");
    }

    if (string.Equals(subcommand, "app", StringComparison.OrdinalIgnoreCase))
    {
        await CreateAppAsync(cliOptions, legacyInit: false).ConfigureAwait(false);
        return;
    }

    await CreateNamedProjectAsync(subcommand, cliOptions).ConfigureAwait(false);
}

async Task CreateAppAsync(CliOptions cliOptions, bool legacyInit)
{
    var name = legacyInit ? cliOptions.Positionals.FirstOrDefault() : cliOptions.Positionals.Skip(1).FirstOrDefault();

    if (string.IsNullOrWhiteSpace(name))
    {
        throw new ArgumentException(legacyInit
            ? "Usage: avila init <app-name>"
            : "Usage: avila create app <app-name>");
    }

    await CreateNamedProjectAsync(name, cliOptions).ConfigureAwait(false);
}

async Task CreateNamedProjectAsync(string name, CliOptions cliOptions)
{
    var path = await new ProjectScaffolder().InitAsync(
        name,
        template: cliOptions.Template ?? "vanilla",
        url: cliOptions.Url);
    Console.WriteLine($"Created {path}");
}

async Task BuildAsync(CliOptions cliOptions)
{
    var result = await new BuildService(logger).BuildAsync(cliOptions.ProjectPath);
    Console.WriteLine($"Build prepared: {result.BuildDirectory}");
    Console.WriteLine(ReportFormatter.Format(result.Report));
}

async Task PackageAsync(CliOptions cliOptions)
{
    var result = await new PackageService(logger).PackageAsync(cliOptions.ProjectPath, cliOptions.SecureBundle);
    Console.WriteLine($"Package created: {result.ExePath}");
    if (!string.IsNullOrWhiteSpace(result.BundlePath))
    {
        Console.WriteLine($"Bundle created: {result.BundlePath}");
        Console.WriteLine($"Bundle manifest: {result.BundleManifestPath}");
    }
    Console.WriteLine(ReportFormatter.Format(result.Report));
}

async Task CheckAsync(CliOptions cliOptions)
{
    if (cliOptions.Security)
    {
        await AuditAsync(cliOptions).ConfigureAwait(false);
        return;
    }

    await DoctorAsync(cliOptions).ConfigureAwait(false);
}

async Task AuditAsync(CliOptions cliOptions)
{
    var project = await ProjectLocator.LoadProjectAsync(cliOptions.ProjectPath).ConfigureAwait(false);
    var validation = ManifestLoader.Validate(project);
    var policy = PolicyResolver.Resolve(project, "production");
    var packagePath = Path.Combine(project.RootPath, "dist");
    var packageIssues = Directory.Exists(packagePath)
        ? PackageService.FindDisallowedPackageFiles(packagePath).ToArray()
        : Array.Empty<string>();
    SecureBundleVerificationResult? bundleVerification = null;

    if (Directory.Exists(packagePath))
    {
        var bundlePath = Path.Combine(packagePath, SecureBundleReader.BundleFileName);
        var manifestPath = Path.Combine(packagePath, SecureBundleReader.ManifestFileName);
        var signaturePath = Path.Combine(packagePath, SecureBundleReader.SignatureFileName);
        var publicKeyPath = Path.Combine(packagePath, SecureBundleReader.PublicKeyFileName);

        if (File.Exists(bundlePath) && File.Exists(manifestPath) && File.Exists(signaturePath) && File.Exists(publicKeyPath))
        {
            bundleVerification = await SecureBundleReader.VerifyAsync(
                bundlePath,
                await File.ReadAllTextAsync(publicKeyPath).ConfigureAwait(false)).ConfigureAwait(false);
        }
    }

    Console.WriteLine("Security audit");
    Console.WriteLine($"Project: {project.Manifest.App.Name}");
    Console.WriteLine($"Manifest: {(validation.Errors.Any() ? "fail" : "ok")} ({validation.Errors.Count()} errors, {validation.Warnings.Count()} warnings)");
    Console.WriteLine($"Policy: {(policy.HasErrors ? "fail" : "ok")} ({policy.Issues.Count} issues)");
    Console.WriteLine($"Package: {(packageIssues.Length > 0 ? "fail" : "ok")} ({packageIssues.Length} blocked files)");
    if (bundleVerification is not null)
    {
        Console.WriteLine($"Secure bundle: ok ({bundleVerification.FileCount} files, {bundleVerification.BundleSha256})");
    }

    foreach (var issue in validation.Errors)
    {
        Console.WriteLine($"[manifest:error] {issue.Code}: {issue.Message}");
    }

    foreach (var warning in validation.Warnings)
    {
        Console.WriteLine($"[manifest:warning] {warning.Code}: {warning.Message}");
    }

    foreach (var issue in policy.Issues)
    {
        Console.WriteLine($"[policy:{issue.Severity.ToString().ToLowerInvariant()}] {issue.Code}: {issue.Message}");
    }

    foreach (var file in packageIssues)
    {
        Console.WriteLine($"[package:block] {Path.GetRelativePath(packagePath, file)}");
    }

    if (bundleVerification is not null)
    {
        Console.WriteLine($"[bundle:ok] {Path.GetFileName(bundleVerification.BundlePath)}");
    }

    if (validation.Errors.Any() || validation.Warnings.Any() || policy.Issues.Any() || packageIssues.Length > 0 || (cliOptions.Production && bundleVerification is null))
    {
        Environment.ExitCode = 2;
    }
}

async Task VerifyBundleAsync(CliOptions cliOptions)
{
    var target = cliOptions.Positionals.FirstOrDefault();
    if (string.IsNullOrWhiteSpace(target))
    {
        throw new ArgumentException("Usage: avila verify <bundle-path|bundle-directory>");
    }

    var bundlePath = SecureBundleReader.ResolveBundlePath(target);
    var publicKeyPath = SecureBundleReader.ResolveSidecarPath(bundlePath, SecureBundleReader.PublicKeyFileName);
    if (!File.Exists(publicKeyPath))
    {
        throw new FileNotFoundException("Could not find the secure bundle public key sidecar.", publicKeyPath);
    }

    var verification = await SecureBundleReader.VerifyAsync(
        bundlePath,
        await File.ReadAllTextAsync(publicKeyPath).ConfigureAwait(false)).ConfigureAwait(false);

    Console.WriteLine("Secure bundle verification");
    Console.WriteLine($"Bundle: {verification.BundlePath}");
    Console.WriteLine($"Manifest: {verification.ManifestPath}");
    Console.WriteLine($"Signature: {verification.SignaturePath}");
    Console.WriteLine($"Files: {verification.FileCount}");
    Console.WriteLine($"SHA256: {verification.BundleSha256}");
    Console.WriteLine($"Extraction root: {verification.ExtractionRoot}");
}

async Task PublishReleaseAsync(CliOptions cliOptions)
{
    var result = await new ReleaseService(logger).PublishAsync(
        outputDirectory: cliOptions.OutputPath,
        sign: cliOptions.Sign,
        certificatePath: cliOptions.CertificatePath,
        certificatePassword: cliOptions.CertificatePassword,
        timestampUrl: cliOptions.TimestampUrl,
        runtimeIdentifier: cliOptions.RuntimeIdentifier ?? "win-x64").ConfigureAwait(false);

    Console.WriteLine($"Release bundle: {result.ZipPath}");
    Console.WriteLine($"SHA256: {result.Sha256Path}");
    Console.WriteLine($"Version: {result.VersionText}");
}

async Task BenchmarkAsync(CliOptions cliOptions)
{
    var buildService = new BuildService(logger);
    var packageService = new PackageService(logger);
    var buildTimer = Stopwatch.StartNew();
    var build = await buildService.BuildAsync(cliOptions.ProjectPath).ConfigureAwait(false);
    buildTimer.Stop();

    var packageTimer = Stopwatch.StartNew();
    var package = await packageService.PackageAsync(cliOptions.ProjectPath).ConfigureAwait(false);
    packageTimer.Stop();

    var avilaStartup = await MeasureAvilaStartupAsync(package.ExePath, Path.GetDirectoryName(package.ExePath) ?? package.DistDirectory).ConfigureAwait(false);
    var distBytes = GetDirectorySizeBytes(package.DistDirectory);

    Console.WriteLine($"Project: {build.Project.Manifest.App.Name}");
    Console.WriteLine($"Build time: {buildTimer.Elapsed.TotalMilliseconds:N0} ms");
    Console.WriteLine($"Package time: {packageTimer.Elapsed.TotalMilliseconds:N0} ms");
    Console.WriteLine($"Avila startup: {avilaStartup.StartupMs:N0} ms");
    Console.WriteLine($"Avila memory: {avilaStartup.WorkingSetBytes / 1024d / 1024d:N2} MB");
    Console.WriteLine($"Avila CPU idle: {avilaStartup.CpuIdlePercent:N1}%");
    Console.WriteLine($"Package size: {distBytes / 1024d / 1024d:N2} MB");
    Console.WriteLine(ReportFormatter.Format(package.Report));

    if (!string.IsNullOrWhiteSpace(cliOptions.ElectronPath))
    {
        var electron = await BenchmarkElectronAsync(cliOptions.ElectronPath, cliOptions.ProjectPath).ConfigureAwait(false);
        Console.WriteLine();
        Console.WriteLine("Electron comparison");
        Console.WriteLine($"Electron startup: {electron.StartupMs:N0} ms");
        Console.WriteLine($"Electron memory: {electron.WorkingSetBytes / 1024d / 1024d:N2} MB");
        Console.WriteLine($"Electron CPU idle: {electron.CpuIdlePercent:N1}%");
        Console.WriteLine($"Electron package size: {electron.PackageSizeMb:N2} MB");
        Console.WriteLine($"Electron app path: {electron.AppPath}");
    }
}

async Task UpcheckAsync(CliOptions cliOptions)
{
    var updateService = new UpdateService();
    var currentMarker = Versionate.ResolveText(AppContext.BaseDirectory, Environment.CurrentDirectory);
    var result = await updateService.UpcheckAsync(currentMarker, AppContext.BaseDirectory).ConfigureAwait(false);

    Console.WriteLine("Checking Avila version...");
    Console.WriteLine($"Local version: {result.CurrentVersion}");
    Console.WriteLine($"Latest version: {result.LatestVersion}");

    if (result.VersionMismatch)
    {
        Console.WriteLine("Version marker mismatch detected. Reinstall Avila to repair this installation.");
        Console.WriteLine($"Marker source: {result.CurrentMarkerSource ?? "unknown"}");
        Environment.ExitCode = 2;
        return;
    }

    if (result.UpdateAvailable)
    {
        Console.WriteLine("A new Avila release is available.");
        Console.WriteLine($"Release: {result.LatestReleaseName}");
        Console.WriteLine($"URL: {result.LatestReleaseUrl}");

        if (cliOptions.OpenRelease && PromptYesNo("Open the release page now?"))
        {
            OpenInBrowser(result.LatestReleaseUrl);
        }

        return;
    }

    Console.WriteLine("Avila is already up to date.");
}

async Task UpgradeAsync(CliOptions cliOptions)
{
    var updateService = new UpdateService();
    var progress = new Progress<InstallProgress>(report => WriteProgressLine(report));
    Console.WriteLine("Downloading latest Avila release...");
    var result = await updateService.InstallLatestAsync(
        cliOptions.InstallDirectory,
        cliOptions.InstallScope,
        progress,
        cancellationToken: default).ConfigureAwait(false);

    Console.WriteLine();
    Console.WriteLine("Installing Avila...");
    Console.WriteLine("Adding Avila to PATH...");
    Console.WriteLine("Installation completed successfully.");
    Console.WriteLine($"Installed release: {result.ReleaseName}");
    Console.WriteLine($"Version: {result.ReleaseVersion}");
    Console.WriteLine($"Install root: {result.InstallRoot}");
    Console.WriteLine($"Executable: {result.ExecutablePath}");
}

async Task<(double StartupMs, long WorkingSetBytes, double CpuIdlePercent)> MeasureAvilaStartupAsync(string executablePath, string workingDirectory)
{
    var markerPath = Path.Combine(Path.GetTempPath(), $"avila-startup-{Guid.NewGuid():N}.txt");
    TryDelete(markerPath);

    using var process = StartBenchmarkProcess(
        executablePath,
        workingDirectory,
        markerPath,
        extraArguments: []);

    var stopwatch = Stopwatch.StartNew();
    var stdoutTask = process.StandardOutput.ReadToEndAsync();
    var stderrTask = process.StandardError.ReadToEndAsync();
    await WaitForMarkerAsync(process, markerPath, TimeSpan.FromSeconds(60)).ConfigureAwait(false);
    stopwatch.Stop();

    process.Refresh();
    var workingSetBytes = process.WorkingSet64;
    var elapsedMs = Math.Max(1d, stopwatch.Elapsed.TotalMilliseconds);
    var cpuMs = process.TotalProcessorTime.TotalMilliseconds;
    var cpuUsage = Math.Clamp(cpuMs / elapsedMs / Environment.ProcessorCount * 100d, 0d, 100d);
    var cpuIdle = 100d - cpuUsage;

    TryTerminate(process);
    await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
    TryDelete(markerPath);

    return (stopwatch.Elapsed.TotalMilliseconds, workingSetBytes, cpuIdle);
}

async Task<(double StartupMs, double PackageSizeMb, long WorkingSetBytes, double CpuIdlePercent, string AppPath)> BenchmarkElectronAsync(string electronPath, string? avilaProjectPath)
{
    var resolvedElectron = ResolveElectronExecutablePath(electronPath);
    var electronRoot = Directory.Exists(electronPath)
        ? Path.GetFullPath(electronPath)
        : Path.GetDirectoryName(resolvedElectron) ?? Path.GetDirectoryName(Path.GetFullPath(resolvedElectron)) ?? Environment.CurrentDirectory;

    var tempRoot = Path.Combine(Path.GetTempPath(), $"avila-electron-benchmark-{Guid.NewGuid():N}");
    Directory.CreateDirectory(tempRoot);

    var markerPath = Path.Combine(tempRoot, "electron-startup.txt");
    await File.WriteAllTextAsync(Path.Combine(tempRoot, "package.json"), """
{
  "name": "avila-electron-benchmark",
  "version": "1.0.0",
  "main": "main.js"
}
""").ConfigureAwait(false);

    await File.WriteAllTextAsync(Path.Combine(tempRoot, "index.html"), """
<!doctype html>
<html>
  <head>
    <meta charset="utf-8" />
    <title>Electron benchmark</title>
  </head>
  <body>
    <main>Electron benchmark</main>
  </body>
</html>
""").ConfigureAwait(false);

    await File.WriteAllTextAsync(Path.Combine(tempRoot, "main.js"), $$"""
const { app, BrowserWindow } = require("electron");
const fs = require("fs");

function resolveMarker() {
  const args = process.argv.slice(2);
  const index = args.indexOf("--benchmark-file");
  if (index >= 0 && index + 1 < args.length) {
    return args[index + 1];
  }

  return null;
}

function createWindow() {
  const win = new BrowserWindow({
    width: 1280,
    height: 800,
    show: false
  });

  win.webContents.once("did-finish-load", () => {
    const marker = resolveMarker();
    if (marker) {
      fs.writeFileSync(marker, new Date().toISOString() + "\n");
    }

    setTimeout(() => app.quit(), 50);
  });

  win.loadFile("index.html");
}

app.whenReady().then(createWindow);
app.on("window-all-closed", () => app.quit());
""").ConfigureAwait(false);

    try
    {
        using var process = StartBenchmarkProcess(
            resolvedElectron,
            tempRoot,
            markerPath,
            extraArguments: ["."]);

        var stopwatch = Stopwatch.StartNew();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await WaitForMarkerAsync(process, markerPath, TimeSpan.FromSeconds(60)).ConfigureAwait(false);
        stopwatch.Stop();

        process.Refresh();
        var workingSetBytes = process.WorkingSet64;
        var elapsedMs = Math.Max(1d, stopwatch.Elapsed.TotalMilliseconds);
        var cpuMs = process.TotalProcessorTime.TotalMilliseconds;
        var cpuUsage = Math.Clamp(cpuMs / elapsedMs / Environment.ProcessorCount * 100d, 0d, 100d);
        var cpuIdle = 100d - cpuUsage;

        TryTerminate(process);
        await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);

        var packageSize = (GetDirectorySizeBytes(tempRoot) + GetDirectorySizeBytes(electronRoot)) / 1024d / 1024d;
        return (stopwatch.Elapsed.TotalMilliseconds, packageSize, workingSetBytes, cpuIdle, tempRoot);
    }
    catch
    {
        TryDeleteDirectory(tempRoot);
        throw;
    }
}

Process StartBenchmarkProcess(string executablePath, string workingDirectory, string markerPath, IReadOnlyList<string> extraArguments)
{
    var startInfo = new ProcessStartInfo
    {
        FileName = executablePath,
        WorkingDirectory = workingDirectory,
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    };

    startInfo.ArgumentList.Add("--mode");
    startInfo.ArgumentList.Add("production");
    startInfo.ArgumentList.Add("--benchmark-file");
    startInfo.ArgumentList.Add(markerPath);

    foreach (var argument in extraArguments)
    {
        startInfo.ArgumentList.Add(argument);
    }

    var process = Process.Start(startInfo);
    if (process is null)
    {
        throw new InvalidOperationException($"Could not start benchmark process: {executablePath}");
    }

    return process;
}

async Task WaitForMarkerAsync(Process process, string markerPath, TimeSpan timeout)
{
    var deadline = DateTimeOffset.UtcNow + timeout;
    while (DateTimeOffset.UtcNow < deadline)
    {
        if (File.Exists(markerPath))
        {
            return;
        }

        if (process.HasExited)
        {
            throw new InvalidOperationException($"Benchmark process exited before writing the marker file: {markerPath}");
        }

        await Task.Delay(50).ConfigureAwait(false);
    }

    throw new TimeoutException($"Benchmark marker was not written in time: {markerPath}");
}

void TryTerminate(Process process)
{
    try
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
        }

        process.WaitForExit(5000);
    }
    catch
    {
    }
}

static void TryDelete(string path)
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

static void TryDeleteDirectory(string path)
{
    try
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
    catch
    {
    }
}

static long GetDirectorySizeBytes(string root)
{
    if (!Directory.Exists(root))
    {
        return 0;
    }

    long total = 0;
    foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
    {
        try
        {
            total += new FileInfo(file).Length;
        }
        catch
        {
        }
    }

    return total;
}

static string ResolveElectronExecutablePath(string electronPath)
{
    if (File.Exists(electronPath))
    {
        return Path.GetFullPath(electronPath);
    }

    if (Directory.Exists(electronPath))
    {
        foreach (var candidate in new[]
        {
            Path.Combine(electronPath, "electron.exe"),
            Path.Combine(electronPath, "electron.cmd"),
            Path.Combine(electronPath, "electron")
        })
        {
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }
    }

    throw new FileNotFoundException("Could not locate an Electron executable.", electronPath);
}

static bool PromptYesNo(string message)
{
    Console.Write($"{message} [y/N] ");
    var response = Console.ReadLine()?.Trim().ToLowerInvariant();
    return response is "y" or "yes";
}

static void OpenInBrowser(string url)
{
    var startInfo = new ProcessStartInfo
    {
        FileName = url,
        UseShellExecute = true
    };

    Process.Start(startInfo);
}

static void WriteProgressLine(InstallProgress progress)
{
    const int width = 24;
    var filled = Math.Clamp(progress.Percent * width / 100, 0, width);
    var bar = new string('#', filled) + new string('-', width - filled);
    Console.Write($"\r[{bar}] {progress.Percent,3}% {progress.Message.PadRight(44)}");
    if (progress.Percent >= 100)
    {
        Console.WriteLine();
    }
}

async Task DoctorAsync(CliOptions cliOptions)
{
    var checks = await new DoctorService(logger).RunAsync(cliOptions.ProjectPath);
    foreach (var check in checks)
    {
        Console.WriteLine($"{(check.Passed ? "[ok]" : "[fail]")} {check.Name}: {check.Message}");
    }

    if (checks.Any(check => !check.Passed))
    {
        Environment.ExitCode = 2;
    }
}

async Task InspectAsync(CliOptions cliOptions)
{
    var target = cliOptions.Positionals.FirstOrDefault()?.ToLowerInvariant() ?? "apis";
    var service = new InspectService();
    var output = target switch
    {
        "apis" or "api" => await service.InspectApisAsync(cliOptions.ProjectPath),
        "permissions" or "perms" => await service.InspectPermissionsAsync(cliOptions.ProjectPath),
        "package" or "dist" => await service.InspectPackageAsync(cliOptions.ProjectPath),
        _ => throw new ArgumentException("Usage: avila inspect apis|permissions|package [--project <path>]")
    };

    Console.WriteLine(output);
}

async Task RunRuntimeAsync(CliOptions cliOptions, string mode)
{
    var projectPath = ProjectLocator.ResolveProjectPath(cliOptions.ProjectPath);
    var modeArgs = mode == "dev"
        ? string.Join(' ', new[]
        {
            cliOptions.DevTools ? "--devtools" : "",
            cliOptions.Debug ? "--debug" : ""
        }.Where(value => !string.IsNullOrWhiteSpace(value)))
        : "";
    var runner = new ProcessRunner(logger);

    ProcessResult result;
    if (ProjectLocator.TryFindRepositoryRoot(out var repoRoot))
    {
        var runtimeProject = Path.Combine(repoRoot, "src", "Avila.Runtime", "Avila.Runtime.csproj");
        var watchPrefix = mode == "dev" ? "watch run" : "run";
        var arguments = $"{watchPrefix} --project \"{runtimeProject}\" -- --project \"{projectPath}\" --mode {mode} {modeArgs}";
        if (mode == "dev")
        {
            arguments = $"watch --non-interactive run --project \"{runtimeProject}\" -- --project \"{projectPath}\" --mode {mode} {modeArgs}";
        }
        result = await runner.RunAsync("dotnet", arguments, repoRoot);
    }
    else
    {
        var runtimeDirectory = ProjectLocator.FindPackagedRuntimeDirectory();
        var runtimeExe = Path.Combine(runtimeDirectory, "Avila.exe");
        var arguments = $"--project \"{projectPath}\" --mode {mode} {modeArgs}";
        result = await runner.RunAsync(runtimeExe, arguments, runtimeDirectory);
    }

    if (!result.Succeeded)
    {
        throw new InvalidOperationException(result.StandardOutput + Environment.NewLine + result.StandardError);
    }
}

void PrintHelp()
{
    Console.WriteLine("""
 Avila Tooling CLI

Usage:
  avila new <app-name>
  avila create app <app-name>
  avila create <app-name> [--url <site>] [--template browser-app|browser|vanilla|react|...]
  avila init <app-name>
  avila dev [--project <path>] [--devtools]
  avila dev [--project <path>] [--devtools] [--debug]
  avila run [--project <path>]
  avila build [--project <path>]
  avila package [--project <path>] [--secure]
  avila verify <bundle-path|bundle-directory>
  avila check [--project <path>] [--security]
  avila audit [--project <path>] [--production]
  avila upcheck
  avila upgrade [--scope user|machine] [--directory <path>]
  avila publish [--sign] [--cert <pfx>] [--cert-password <pwd>] [--timestamp-url <url>] [--output <path>] [--runtime <rid>]
  avila benchmark [--project <path>] [--electron <path>]
  avila version
  avila doctor [--project <path>]
  avila inspect apis [--project <path>]
  avila inspect permissions [--project <path>]
  avila inspect package [--project <path>]
""");
}

void PrintVersion()
{
    Console.WriteLine(Versionate.ResolveText(AppContext.BaseDirectory, Environment.CurrentDirectory));
}

internal sealed class CliOptions
{
    public string? ProjectPath { get; init; }

    public string? Template { get; init; }

    public string? Url { get; init; }

    public string? OutputPath { get; init; }

    public string? RuntimeIdentifier { get; init; }

    public string? ElectronPath { get; init; }

    public string? InstallDirectory { get; init; }

    public bool DevTools { get; init; }

    public bool Debug { get; init; }

    public bool Security { get; init; }

    public bool SecureBundle { get; init; }

    public bool Production { get; init; }

    public bool Sign { get; init; }

    public string? CertificatePath { get; init; }

    public string? CertificatePassword { get; init; }

    public string? TimestampUrl { get; init; }

    public bool OpenRelease { get; init; }

    public InstallScope InstallScope { get; init; } = InstallScope.User;

    public bool Verbose { get; init; }

    public IReadOnlyList<string> Positionals { get; init; } = [];

    public static CliOptions Parse(string[] args)
    {
        string? projectPath = null;
        string? template = null;
        string? url = null;
        string? outputPath = null;
        string? runtimeIdentifier = null;
        string? electronPath = null;
        string? installDirectory = null;
        string? certificatePath = null;
        string? certificatePassword = null;
        string? timestampUrl = null;
        var devTools = false;
        var debug = false;
        var security = false;
        var secureBundle = false;
        var production = false;
        var sign = false;
        var openRelease = true;
        var installScope = InstallScope.User;
        var verbose = false;
        var positionals = new List<string>();

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            switch (arg)
            {
                case "--project" when index + 1 < args.Length:
                    projectPath = args[++index];
                    break;
                case "--template" when index + 1 < args.Length:
                    template = args[++index];
                    break;
                case "--url" when index + 1 < args.Length:
                    url = args[++index];
                    break;
                case "--output" when index + 1 < args.Length:
                    outputPath = args[++index];
                    break;
                case "--runtime" when index + 1 < args.Length:
                    runtimeIdentifier = args[++index];
                    break;
                case "--electron" when index + 1 < args.Length:
                    electronPath = args[++index];
                    break;
                case "--directory" when index + 1 < args.Length:
                    installDirectory = args[++index];
                    break;
                case "--scope" when index + 1 < args.Length:
                    installScope = args[++index].Equals("machine", StringComparison.OrdinalIgnoreCase)
                        ? InstallScope.Machine
                        : InstallScope.User;
                    break;
                case "--secure":
                    secureBundle = true;
                    break;
                case "--devtools":
                    devTools = true;
                    break;
                case "--debug":
                    debug = true;
                    break;
                case "--security":
                    security = true;
                    break;
                case "--production":
                    production = true;
                    break;
                case "--sign":
                    sign = true;
                    break;
                case "--cert" when index + 1 < args.Length:
                    certificatePath = args[++index];
                    break;
                case "--cert-password" when index + 1 < args.Length:
                    certificatePassword = args[++index];
                    break;
                case "--timestamp-url" when index + 1 < args.Length:
                    timestampUrl = args[++index];
                    break;
                case "--no-open":
                    openRelease = false;
                    break;
                case "--verbose":
                    verbose = true;
                    break;
                default:
                    positionals.Add(arg);
                    break;
            }
        }

        return new CliOptions
        {
            ProjectPath = projectPath,
            Template = template,
            Url = url,
            OutputPath = outputPath,
            RuntimeIdentifier = runtimeIdentifier,
            ElectronPath = electronPath,
            InstallDirectory = installDirectory,
            DevTools = devTools,
            Debug = debug,
            Security = security,
            SecureBundle = secureBundle,
            Production = production,
            Sign = sign,
            CertificatePath = certificatePath,
            CertificatePassword = certificatePassword,
            TimestampUrl = timestampUrl,
            OpenRelease = openRelease,
            InstallScope = installScope,
            Verbose = verbose,
            Positionals = positionals
        };
    }
}
