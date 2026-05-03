using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Avila.Packager;
using Avila.Security;
using Xunit;

namespace Avila.Tests.Security;

public sealed class SecureBundleTests
{
    [Fact]
    public async Task SecureBundleVerifiesAndLoadsProject()
    {
        using var workspace = new TempWorkspace();
        var manifest = CreateManifest();
        SeedProject(workspace.Root, manifest);

        using var signer = RSA.Create(3072);
        var publicKey = SecureBundleCrypto.ExportPublicKeyBase64(signer);
        var writer = new SecureBundleWriter();
        var result = await writer.WriteAsync(
            new AvilaProject(workspace.Root, Path.Combine(workspace.Root, "avila.json"), manifest),
            Path.Combine(workspace.Root, "dist"),
            removeSourceMaps: true,
            signer,
            publicKey);

        var verification = await SecureBundleReader.VerifyAsync(result.BundlePath, publicKey);
        Assert.Equal(4, verification.FileCount);
        Assert.True(File.Exists(result.ManifestPath));
        Assert.True(File.Exists(result.SignaturePath));
        Assert.True(File.Exists(result.PublicKeyPath));
        Assert.Equal(result.BundleSha256, verification.BundleSha256);

        var project = await SecureBundleReader.LoadProjectAsync(result.BundlePath, publicKey);
        Assert.True(project.BundlePath is not null);
        Assert.Equal("Avila Secure App", project.Manifest.App.Name);
        Assert.True(File.Exists(Path.Combine(project.RootPath, "src", "index.html")));
    }

    [Fact]
    public async Task SecureBundleRejectsTamperedContent()
    {
        using var workspace = new TempWorkspace();
        var manifest = CreateManifest();
        SeedProject(workspace.Root, manifest);

        using var signer = RSA.Create(3072);
        var publicKey = SecureBundleCrypto.ExportPublicKeyBase64(signer);
        var writer = new SecureBundleWriter();
        var result = await writer.WriteAsync(
            new AvilaProject(workspace.Root, Path.Combine(workspace.Root, "avila.json"), manifest),
            Path.Combine(workspace.Root, "dist"),
            removeSourceMaps: true,
            signer,
            publicKey);

        using (var archive = ZipFile.Open(result.BundlePath, ZipArchiveMode.Update))
        {
            var entry = archive.GetEntry("src/index.html") ?? throw new InvalidOperationException("Bundle entry missing.");
            entry.Delete();
            var replacement = archive.CreateEntry("src/index.html");
            await using var stream = replacement.Open();
            await using var writerStream = new StreamWriter(stream);
            await writerStream.WriteAsync("<!doctype html><html><body>tampered</body></html>");
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => SecureBundleReader.VerifyAsync(result.BundlePath, publicKey));
    }

    [Fact]
    public async Task SecureBundleRejectsUnexpectedFiles()
    {
        using var workspace = new TempWorkspace();
        var manifest = CreateManifest();
        SeedProject(workspace.Root, manifest);

        using var signer = RSA.Create(3072);
        var publicKey = SecureBundleCrypto.ExportPublicKeyBase64(signer);
        var writer = new SecureBundleWriter();
        var result = await writer.WriteAsync(
            new AvilaProject(workspace.Root, Path.Combine(workspace.Root, "avila.json"), manifest),
            Path.Combine(workspace.Root, "dist"),
            removeSourceMaps: true,
            signer,
            publicKey);

        using (var archive = ZipFile.Open(result.BundlePath, ZipArchiveMode.Update))
        {
            var extra = archive.CreateEntry("payload/extra.txt");
            await using var stream = extra.Open();
            await using var writerStream = new StreamWriter(stream);
            await writerStream.WriteAsync("unexpected");
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => SecureBundleReader.VerifyAsync(result.BundlePath, publicKey));
    }

    private static void SeedProject(string root, AvilaManifest manifest)
    {
        Directory.CreateDirectory(Path.Combine(root, "src"));
        File.WriteAllText(Path.Combine(root, "avila.json"), JsonSerializer.Serialize(manifest, ManifestLoader.JsonOptions));
        File.WriteAllText(Path.Combine(root, "src", "index.html"), "<!doctype html><html><body>Hello</body></html>");
        File.WriteAllText(Path.Combine(root, "src", "styles.css"), "body{background:#111;color:#eee;}");
        File.WriteAllText(Path.Combine(root, "src", "index.js"), "console.log('hello');");
    }

    private static AvilaManifest CreateManifest()
    {
        return new AvilaManifest
        {
            Mode = "appview",
            App = new AppManifest
            {
                Id = "com.example.secure",
                Name = "Avila Secure App",
                Version = "1.0.0",
                Entry = "src/index.html",
                Icon = ""
            }
        };
    }
}
