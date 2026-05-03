namespace Avila.Security;

public sealed class OriginPolicy
{
    public const string LocalOrigin = "avila://local";
    public const string VirtualHost = "app.avila.local";

    private readonly AvilaManifest _manifest;

    public OriginPolicy(AvilaManifest manifest)
    {
        _manifest = manifest;
    }

    public bool IsAllowed(string source)
    {
        var normalized = Normalize(source);
        if (normalized == LocalOrigin)
        {
            return _manifest.Security.AllowedOrigins.Any(origin => Normalize(origin) == LocalOrigin);
        }

        if (!_manifest.Security.AllowRemoteContent)
        {
            return false;
        }

        return _manifest.Security.AllowedOrigins.Any(origin =>
            Normalize(origin).Equals(normalized, StringComparison.OrdinalIgnoreCase));
    }

    public static string Normalize(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return "";
        }

        if (source.Equals(LocalOrigin, StringComparison.OrdinalIgnoreCase))
        {
            return LocalOrigin;
        }

        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri))
        {
            return "";
        }

        if (uri.Host.Equals(VirtualHost, StringComparison.OrdinalIgnoreCase))
        {
            return LocalOrigin;
        }

        if (uri.IsFile)
        {
            return LocalOrigin;
        }

        return uri.IsDefaultPort
            ? $"{uri.Scheme}://{uri.Host}"
            : $"{uri.Scheme}://{uri.Host}:{uri.Port}";
    }
}
