using System.Security.Cryptography;
using System.Text;

namespace Avila.Security;

public sealed class CapabilityManager
{
    private readonly string _capability;
    private readonly byte[] _capabilityBytes;

    public CapabilityManager()
    {
        _capability = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        _capabilityBytes = Encoding.UTF8.GetBytes(_capability);
    }

    public string SessionCapability => _capability;

    public bool Verify(string? provided)
    {
        if (string.IsNullOrWhiteSpace(provided))
        {
            return false;
        }

        var providedBytes = Encoding.UTF8.GetBytes(provided);
        return providedBytes.Length == _capabilityBytes.Length
            && CryptographicOperations.FixedTimeEquals(providedBytes, _capabilityBytes);
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
