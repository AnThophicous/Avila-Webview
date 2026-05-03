using Avila.Packager;
using Avila.Security;
using Xunit;

namespace Avila.Tests;

public sealed class BrowserAppScaffolderTests
{
    [Fact]
    public async Task BrowserAppScaffoldCreatesBrowserManifestAndNoBrandIcon()
    {
        using var scope = new TempWorkingDirectory();

        var projectPath = await new ProjectScaffolder().InitAsync(
            "My Site",
            template: "browser-app",
            url: "https://meusite.com");

        var project = await ManifestLoader.LoadProjectAsync(projectPath);

        Assert.Equal("browser-app", project.Manifest.Mode);
        Assert.Equal("", project.Manifest.App.Icon);
        Assert.Equal("https://meusite.com", project.Manifest.Browser.Url);
        Assert.Contains("https://meusite.com", project.Manifest.Browser.AllowedOrigins);
        Assert.True(project.Manifest.Permissions["browser.navigate"]);
        Assert.False(File.Exists(Path.Combine(projectPath, "assets", "icon.ico")));
        Assert.True(File.Exists(Path.Combine(projectPath, "README.md")));
        Assert.False(File.Exists(Path.Combine(projectPath, "src", "main.js")));
    }

    private sealed class TempWorkingDirectory : IDisposable
    {
        private readonly string _originalDirectory = Directory.GetCurrentDirectory();
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"avila-tests-{Guid.NewGuid():N}");

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
