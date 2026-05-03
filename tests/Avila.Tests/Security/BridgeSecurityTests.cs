using Avila.Bridge;
using Avila.Security;
using Xunit;

namespace Avila.Tests.Security;

public sealed class BridgeSecurityTests
{
    [Fact]
    public async Task BridgeRejectsStaleTimestamp()
    {
        await using var env = BridgeTestEnvironment.Create();
        var staleTimestamp = DateTimeOffset.UtcNow.AddMinutes(-11).ToUnixTimeMilliseconds();

        var response = await env.SendAsync(
            OriginPolicy.LocalOrigin,
            "system.ping",
            new { ok = true },
            timestamp: staleTimestamp);

        Assert.False(response.Ok);
        Assert.NotNull(response.Error);
        Assert.Equal(BridgeErrorCodes.InvalidRequest, response.Error!.Code);
        Assert.Contains("timestamp", response.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BridgeRejectsInvalidCapability()
    {
        await using var env = BridgeTestEnvironment.Create();

        var response = await env.SendAsync(
            OriginPolicy.LocalOrigin,
            "system.ping",
            new { ok = true },
            capability: "not-a-real-capability");

        Assert.False(response.Ok);
        Assert.NotNull(response.Error);
        Assert.Equal(BridgeErrorCodes.InvalidCapability, response.Error!.Code);
    }

    [Fact]
    public async Task BridgeRejectsOversizedPayloads()
    {
        await using var env = BridgeTestEnvironment.Create(manifest =>
        {
            manifest.Security.MaxPayloadBytes = 256;
        });

        var response = await env.SendAsync(
            OriginPolicy.LocalOrigin,
            "system.ping",
            new { blob = new string('x', 1024) });

        Assert.False(response.Ok);
        Assert.NotNull(response.Error);
        Assert.Equal(BridgeErrorCodes.PayloadTooLarge, response.Error!.Code);
    }

    [Fact]
    public async Task RemoteOriginCannotTouchBridge()
    {
        await using var env = BridgeTestEnvironment.Create();

        var response = await env.SendAsync(
            "https://evil.example",
            "app.apiVersion",
            new { ok = true });

        Assert.False(response.Ok);
        Assert.NotNull(response.Error);
        Assert.Equal(BridgeErrorCodes.InvalidOrigin, response.Error!.Code);
    }
}
