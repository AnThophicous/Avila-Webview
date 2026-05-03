using Avila.Core;
using Avila.Diagnostics;
using Avila.Packager;
using Avila.Security;
using System.Diagnostics;

var command = args.Length == 0 ? "help" : args[0].ToLowerInvariant();
var options = CliOptions.Parse(args.Skip(1).ToArray());
var logger = new SafeLogger(verbose: options.Verbose);

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
    var result = await new PackageService(logger).PackageAsync(cliOptions.ProjectPath);
    Console.WriteLine($"Package created: {result.ExePath}");
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

    Console.WriteLine("Security audit");
    Console.WriteLine($"Project: {project.Manifest.App.Name}");
    Console.WriteLine($"Manifest: {(validation.Errors.Any() ? "fail" : "ok")} ({validation.Errors.Count()} errors, {validation.Warnings.Count()} warnings)");
    Console.WriteLine($"Policy: {(policy.HasErrors ? "fail" : "ok")} ({policy.Issues.Count} issues)");
    Console.WriteLine($"Package: {(packageIssues.Length > 0 ? "fail" : "ok")} ({packageIssues.Length} blocked files)");

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

    if (validation.Errors.Any() || validation.Warnings.Any() || policy.Issues.Any() || packageIssues.Length > 0)
    {
        Environment.ExitCode = 2;
    }
}

async Task PublishReleaseAsync(CliOptions cliOptions)
{
    var result = await new ReleaseService(logger).PublishAsync(
        outputDirectory: cliOptions.OutputPath,
        sign: cliOptions.Sign,
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

    var distBytes = Directory.EnumerateFiles(package.DistDirectory, "*", SearchOption.AllDirectories)
        .Sum(file => new FileInfo(file).Length);

    Console.WriteLine($"Project: {build.Project.Manifest.App.Name}");
    Console.WriteLine($"Build time: {buildTimer.Elapsed.TotalMilliseconds:N0} ms");
    Console.WriteLine($"Package time: {packageTimer.Elapsed.TotalMilliseconds:N0} ms");
    Console.WriteLine($"Package size: {distBytes / 1024d / 1024d:N2} MB");
    Console.WriteLine(ReportFormatter.Format(package.Report));
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
    var modeArgs = mode == "dev" && cliOptions.DevTools ? "--devtools" : "";
    var runner = new ProcessRunner(logger);

    ProcessResult result;
    if (ProjectLocator.TryFindRepositoryRoot(out var repoRoot))
    {
        var runtimeProject = Path.Combine(repoRoot, "src", "Avila.Runtime", "Avila.Runtime.csproj");
        var arguments = $"run --project \"{runtimeProject}\" -- --project \"{projectPath}\" --mode {mode} {modeArgs}";
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
  avila run [--project <path>]
  avila build [--project <path>]
  avila package [--project <path>]
  avila check [--project <path>] [--security]
  avila audit [--project <path>]
  avila publish [--sign] [--output <path>] [--runtime <rid>]
  avila benchmark [--project <path>]
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

    public bool DevTools { get; init; }

    public bool Security { get; init; }

    public bool Sign { get; init; }

    public bool Verbose { get; init; }

    public IReadOnlyList<string> Positionals { get; init; } = [];

    public static CliOptions Parse(string[] args)
    {
        string? projectPath = null;
        string? template = null;
        string? url = null;
        string? outputPath = null;
        string? runtimeIdentifier = null;
        var devTools = false;
        var security = false;
        var sign = false;
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
                case "--devtools":
                    devTools = true;
                    break;
                case "--security":
                    security = true;
                    break;
                case "--sign":
                    sign = true;
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
            DevTools = devTools,
            Security = security,
            Sign = sign,
            Verbose = verbose,
            Positionals = positionals
        };
    }
}
