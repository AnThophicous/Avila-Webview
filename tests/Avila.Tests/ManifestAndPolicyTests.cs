using Avila.Security;
using Xunit;

namespace Avila.Tests;

public sealed class ManifestAndPolicyTests
{
    [Fact]
    public void BrowserAppWithoutUrlIsInvalid()
    {
        using var scope = new TempWorkingDirectory();
        var root = scope.Root;

        var manifest = new AvilaManifest
        {
            Mode = "browser-app",
            App = new AppManifest
            {
                Id = "com.example.test",
                Name = "Test App",
                Entry = "",
                Icon = ""
            },
            Browser = new BrowserManifest
            {
                Url = "",
                Navigation = true,
                AllowedOrigins = []
            }
        };

        var project = new AvilaProject(root, Path.Combine(root, "avila.json"), manifest);
        var validation = ManifestLoader.Validate(project);

        Assert.Contains(validation.Errors, issue => issue.Code == "BROWSER_URL_REQUIRED");
    }

    [Fact]
    public void BrowserAppKeepsNativeFetchSeparateFromRemoteNavigation()
    {
        using var scope = new TempWorkingDirectory();
        var root = scope.Root;

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
                Url = "https://meusite.com",
                Navigation = true,
                AllowedOrigins = ["https://meusite.com"]
            },
            Security = new SecurityManifest
            {
                AllowRemoteContent = false
            },
            Permissions = new Dictionary<string, bool>(StringComparer.Ordinal)
            {
                ["browser.navigate"] = true,
                ["network.fetch"] = true
            },
            Network = new NetworkManifest
            {
                AllowedOrigins = ["https://api.meusite.com"]
            }
        };

        var project = new AvilaProject(root, Path.Combine(root, "avila.json"), manifest);
        var resolution = PolicyResolver.Resolve(project, "production");

        Assert.False(resolution.HasErrors);
        Assert.True(resolution.Effective.RemoteNavigationAllowed);
        Assert.True(resolution.Effective.NativeFetchAllowed);
    }

    private sealed class TempWorkingDirectory : IDisposable
    {
        private readonly string _originalDirectory = Directory.GetCurrentDirectory();
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"avila-tests-{Guid.NewGuid():N}");

        public string Root => _root;

        public TempWorkingDirectory()
        {
            Directory.CreateDirectory(_root);
            Directory.SetCurrentDirectory(_root);
        }

        public void Dispose()
        {
            Directory.SetCurrentDirectory(_originalDirectory);
            try
            {
                if (Directory.Exists(_root))
                {
                    Directory.Delete(_root, recursive: true);
                }
            }
            catch
            {
            }
        }
    }
}
