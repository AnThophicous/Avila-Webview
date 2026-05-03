using Microsoft.Web.WebView2.Core;
using Avila.Security;

namespace Avila.Runtime;

public sealed class SecureBundleHost : IDisposable
{
    private readonly string _contentRoot;
    private readonly string _entryPath;
    private readonly CoreWebView2 _webView;

    public SecureBundleHost(CoreWebView2 webView, string contentRoot, string entryPath)
    {
        _webView = webView;
        _contentRoot = contentRoot;
        _entryPath = NormalizeEntryPath(entryPath);
    }

    public void Attach()
    {
        _webView.AddWebResourceRequestedFilter($"https://{OriginPolicy.VirtualHost}/*", CoreWebView2WebResourceContext.All);
        _webView.WebResourceRequested += OnWebResourceRequested;
    }

    public void Dispose()
    {
        _webView.WebResourceRequested -= OnWebResourceRequested;
    }

    private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs args)
    {
        if (!Uri.TryCreate(args.Request.Uri, UriKind.Absolute, out var uri)
            || !uri.Host.Equals(OriginPolicy.VirtualHost, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var requestedPath = uri.AbsolutePath.Trim('/');
        if (string.IsNullOrWhiteSpace(requestedPath))
        {
            requestedPath = _entryPath;
        }
        else
        {
            requestedPath = requestedPath.Replace('\\', '/');
        }

        if (!TryReadBundleFile(requestedPath, out var content, out var contentType))
        {
            if (Path.HasExtension(requestedPath))
            {
                args.Response = CreateResponse(Array.Empty<byte>(), "text/plain; charset=utf-8", 404, "Not Found");
                return;
            }

            if (!TryReadBundleFile(_entryPath, out content, out contentType))
            {
                args.Response = CreateResponse(Array.Empty<byte>(), "text/plain; charset=utf-8", 404, "Not Found");
                return;
            }
        }

        args.Response = CreateResponse(content, contentType, 200, "OK");
    }

    private bool TryReadBundleFile(string relativePath, out byte[] content, out string contentType)
    {
        var resolvedPath = ResolveContentPath(relativePath);
        if (!File.Exists(resolvedPath))
        {
            content = Array.Empty<byte>();
            contentType = "application/octet-stream";
            return false;
        }

        content = File.ReadAllBytes(resolvedPath);
        contentType = GetContentType(relativePath);
        return true;
    }

    private string ResolveContentPath(string relativePath)
    {
        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        var combined = Path.GetFullPath(Path.Combine(_contentRoot, normalized));
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_contentRoot));
        if (!combined.Equals(root, StringComparison.OrdinalIgnoreCase)
            && !combined.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Secure bundle entry escapes the content root: {relativePath}");
        }

        return combined;
    }

    private CoreWebView2WebResourceResponse CreateResponse(byte[] content, string contentType, int statusCode, string reasonPhrase)
    {
        var headers = $"Content-Type: {contentType}\r\nCache-Control: no-store\r\n";
        return _webView.Environment.CreateWebResourceResponse(new MemoryStream(content, writable: false), statusCode, reasonPhrase, headers);
    }

    private static string GetContentType(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".html" or ".htm" => "text/html; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".js" or ".mjs" or ".cjs" => "application/javascript; charset=utf-8",
            ".json" => "application/json; charset=utf-8",
            ".svg" => "image/svg+xml",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".ico" => "image/x-icon",
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            ".ttf" => "font/ttf",
            ".otf" => "font/otf",
            ".map" => "application/json; charset=utf-8",
            ".txt" => "text/plain; charset=utf-8",
            _ => "application/octet-stream"
        };
    }

    private static string NormalizeEntryPath(string entryPath)
    {
        var normalized = string.IsNullOrWhiteSpace(entryPath) ? "src/index.html" : entryPath.Replace('\\', '/');
        return normalized.TrimStart('/');
    }
}
