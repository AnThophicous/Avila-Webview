using Avila.Diagnostics;
using Xunit;

namespace Avila.Tests.Security;

public sealed class LoggerSecurityTests
{
    [Fact]
    public void SafeLoggerRedactsCommonSecrets()
    {
        var logger = new SafeLogger(sanitize: true, verbose: false);
        var input = """
authorization: Bearer secret-token
cookie: session=abc123
token=another-secret
{"password":"top-secret","apiKey":"abc123"}
""";

        var sanitized = logger.Sanitize(input);

        Assert.DoesNotContain("secret-token", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("abc123", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("another-secret", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[redacted]", sanitized, StringComparison.OrdinalIgnoreCase);
    }
}
