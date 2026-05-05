namespace Avila.Runtime;

public static partial class BundleSeal
{
    private static string _publicKeyBase64 = "";

    public static string PublicKeyBase64 => _publicKeyBase64;

    public static bool IsConfigured => !string.IsNullOrWhiteSpace(_publicKeyBase64);
}
