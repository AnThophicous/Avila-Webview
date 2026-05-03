using Avila.Bridge;
using Avila.Packager;
using Avila.Security;
using Xunit;

namespace Avila.Tests.Security;

public sealed class NetworkAndPackageSecurityTests
{
    [Fact]
    public async Task NetworkFetchBlocksLocalNetworkWhenLanIsDisabled()
    {
        await using var env = BridgeTestEnvironment.Create(manifest =>
        {
            manifest.Permissions["network.fetch"] = true;
            manifest.Network.AllowLan = false;
            manifest.Network.AllowedOrigins = ["http://127.0.0.1:5050"];
        });

        var response = await env.SendAsync(
            OriginPolicy.LocalOrigin,
            "network.fetch",
            new { url = "http://127.0.0.1:5050/ping" });

        Assert.False(response.Ok);
        Assert.NotNull(response.Error);
        Assert.Equal(BridgeErrorCodes.PermissionDenied, response.Error!.Code);
        Assert.Contains("local network", response.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NetworkFetchBlocksRedirectToDisallowedOrigin()
    {
        await using var server = MiniHttpServer.Start(_ => HttpResponseSpec.Redirect("http://127.0.0.1:65534/final"));

        await using var env = BridgeTestEnvironment.Create(manifest =>
        {
            manifest.Permissions["network.fetch"] = true;
            manifest.Network.AllowLan = true;
            manifest.Network.AllowedOrigins = [$"http://127.0.0.1:{server.Port}"];
        });

        var response = await env.SendAsync(
            OriginPolicy.LocalOrigin,
            "network.fetch",
            new { url = $"http://127.0.0.1:{server.Port}/start" });

        Assert.False(response.Ok);
        Assert.NotNull(response.Error);
        Assert.Equal(BridgeErrorCodes.PermissionDenied, response.Error!.Code);
        Assert.Contains("Redirect origin", response.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("file:///C:/Windows/System32/drivers/etc/hosts")]
    [InlineData("data:text/plain,hello")]
    [InlineData("javascript:alert(1)")]
    [InlineData("custom://app")]
    public async Task NetworkFetchRejectsUnsupportedSchemes(string url)
    {
        await using var env = BridgeTestEnvironment.Create(manifest =>
        {
            manifest.Permissions["network.fetch"] = true;
            manifest.Network.AllowLan = true;
            manifest.Network.AllowedOrigins = [];
        });

        var response = await env.SendAsync(
            OriginPolicy.LocalOrigin,
            "network.fetch",
            new { url });

        Assert.False(response.Ok);
        Assert.NotNull(response.Error);
        Assert.Equal(BridgeErrorCodes.InvalidRequest, response.Error!.Code);
    }

    [Fact]
    public void PackageScanFindsBlockedFiles()
    {
        using var workspace = new TempWorkspace();
        var dist = Path.Combine(workspace.Root, "dist");
        Directory.CreateDirectory(dist);
        Directory.CreateDirectory(Path.Combine(dist, "nested"));

        File.WriteAllText(Path.Combine(dist, ".env"), "SECRET=1");
        File.WriteAllText(Path.Combine(dist, "secrets.json"), "{}");
        File.WriteAllText(Path.Combine(dist, "appsettings.Development.json"), "{}");
        File.WriteAllText(Path.Combine(dist, "nested", "sample.pdb"), "pdb");
        File.WriteAllText(Path.Combine(dist, "nested", "sample.xml"), "xml");

        var blocked = PackageService.FindDisallowedPackageFiles(dist)
            .Select(Path.GetFileName)
            .ToArray();

        Assert.Contains(".env", blocked);
        Assert.Contains("secrets.json", blocked);
        Assert.Contains("appsettings.Development.json", blocked);
        Assert.Contains("sample.pdb", blocked);
        Assert.Contains("sample.xml", blocked);
    }
}
