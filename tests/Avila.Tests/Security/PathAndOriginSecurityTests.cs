using System.Diagnostics;
using Avila.Bridge;
using Avila.Security;
using Xunit;

namespace Avila.Tests.Security;

public sealed class PathAndOriginSecurityTests
{
    [Theory]
    [InlineData("file:///C:/Windows/System32")]
    [InlineData("data:text/plain,hello")]
    [InlineData("javascript:alert(1)")]
    [InlineData("custom://app")]
    public void BrowserUrlsRequireHttpOrHttps(string url)
    {
        using var scope = new TempWorkspace();

        var manifest = new AvilaManifest
        {
            Mode = "browser-app",
            App = new AppManifest
            {
                Id = "com.example.browser",
                Name = "Browser App",
                Entry = "",
                Icon = ""
            },
            Browser = new BrowserManifest
            {
                Url = url,
                AllowedOrigins = [url],
                Navigation = true
            }
        };

        var project = new AvilaProject(scope.Root, Path.Combine(scope.Root, "avila.json"), manifest);
        var validation = ManifestLoader.Validate(project);

        Assert.Contains(validation.Errors, issue => issue.Code == "BROWSER_URL_INVALID");
    }

    [Fact]
    public void OriginPolicyTreatsFileAsLocalAndRejectsCustomSchemes()
    {
        var manifest = new AvilaManifest
        {
            Security = new SecurityManifest
            {
                AllowRemoteContent = false,
                AllowedOrigins = [OriginPolicy.LocalOrigin]
            }
        };

        var policy = new OriginPolicy(manifest);

        Assert.Equal(OriginPolicy.LocalOrigin, OriginPolicy.Normalize("file:///C:/temp/readme.txt"));
        Assert.Equal(string.Empty, OriginPolicy.Normalize("data:text/plain,hello"));
        Assert.Equal(string.Empty, OriginPolicy.Normalize("javascript:alert(1)"));
        Assert.Equal(string.Empty, OriginPolicy.Normalize("custom://app"));
        Assert.True(policy.IsAllowed("avila://local"));
        Assert.False(policy.IsAllowed("custom://app"));
    }

    [Fact]
    public void SafePathRejectsTraversal()
    {
        using var scope = new TempWorkspace();

        Assert.Throws<UnauthorizedAccessException>(() =>
            SafePath.ResolveInside(scope.Root, "..\\outside.txt"));
    }

    [Fact]
    public async Task FsBridgeRejectsJunctionTraversal()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await using var env = BridgeTestEnvironment.Create(manifest =>
        {
            manifest.Permissions["fs.readText"] = true;
        });

        var appRoot = env.Context.Runtime.GetPath("app");
        var outsideRoot = Path.Combine(env.Project.RootPath, "outside");
        Directory.CreateDirectory(outsideRoot);
        await File.WriteAllTextAsync(Path.Combine(outsideRoot, "secret.txt"), "top-secret");

        var junctionPath = Path.Combine(appRoot, "link");
        CreateDirectoryJunction(junctionPath, outsideRoot);

        var response = await env.SendAsync(
            OriginPolicy.LocalOrigin,
            "fs.readText",
            new { root = "app", path = "link/secret.txt" });

        Assert.False(response.Ok);
        Assert.NotNull(response.Error);
        Assert.Equal(BridgeErrorCodes.PermissionDenied, response.Error!.Code);
        Assert.Contains("reparse point", response.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProcessBridgeRejectsCwdTraversal()
    {
        await using var env = BridgeTestEnvironment.Create(manifest =>
        {
            manifest.Process.AllowedCommands = ["cmd.exe"];
            manifest.Process.AllowedCwdRoots = ["app"];
        });

        var response = await env.SendAsync(
            OriginPolicy.LocalOrigin,
            "process.spawn",
            new
            {
                command = "cmd.exe",
                args = new[] { "/c", "echo", "ok" },
                cwdRoot = "app",
                cwd = "..\\outside",
                timeoutMs = 1000
            });

        Assert.False(response.Ok);
        Assert.NotNull(response.Error);
        Assert.Equal(BridgeErrorCodes.PermissionDenied, response.Error!.Code);
    }

    private static void CreateDirectoryJunction(string junctionPath, string targetPath)
    {
        if (Directory.Exists(junctionPath))
        {
            Directory.Delete(junctionPath, recursive: true);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c mklink /J \"{junctionPath}\" \"{targetPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start mklink.");
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Could not create junction:{Environment.NewLine}{process.StandardOutput.ReadToEnd()}{Environment.NewLine}{process.StandardError.ReadToEnd()}");
        }
    }
}
