using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Avila.Bridge;

public sealed partial class BridgeCommandRegistry
{
    private static readonly HashSet<string> AllowedMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "GET",
        "POST",
        "PUT",
        "PATCH",
        "DELETE",
        "HEAD"
    };

    private static readonly HashSet<string> BlockedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Host",
        "Cookie",
        "Connection",
        "Content-Length",
        "Transfer-Encoding",
        "Proxy-Authorization",
        "Proxy-Connection",
        "Expect"
    };

    private void RegisterNetworkCommands()
    {
        Register("network.fetch", async (context, request, token) =>
        {
            var url = PayloadReader.GetString(request.Payload, "url", 4096);
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            {
                throw new BridgeException(BridgeErrorCodes.InvalidRequest, "network.fetch only accepts absolute http or https URLs.");
            }

            if (!IsAllowedNetworkOrigin(context, uri))
            {
                throw new BridgeException(BridgeErrorCodes.PermissionDenied, "URL origin is not allowed by network.allowedOrigins.");
            }

            if (!context.Project.Manifest.Network.AllowLan && await IsLanHostAsync(uri.Host, token).ConfigureAwait(false))
            {
                throw new BridgeException(BridgeErrorCodes.PermissionDenied, "Local network requests are blocked by network.allowLan.");
            }

            var method = PayloadReader.GetString(request.Payload, "method", 12, required: false);
            if (string.IsNullOrWhiteSpace(method))
            {
                method = "GET";
            }

            if (!AllowedMethods.Contains(method))
            {
                throw new BridgeException(BridgeErrorCodes.InvalidRequest, "HTTP method is not allowed.");
            }

            var headers = ReadHeaders(request.Payload);
            var responseType = PayloadReader.GetString(request.Payload, "responseType", 16, required: false);
            var body = ReadRequestBody(context, request.Payload);
            var timeoutMs = GetOptionalInt(request.Payload, "timeoutMs", 100, context.Project.Manifest.Network.TimeoutMs, context.Project.Manifest.Network.TimeoutMs);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(timeoutMs);

            return await FetchAsync(context, uri, method, headers, body, responseType, timeout.Token).ConfigureAwait(false);
        });
    }

    private static async Task<object?> FetchAsync(
        BridgeCommandContext context,
        Uri uri,
        string method,
        IReadOnlyDictionary<string, string> headers,
        byte[]? body,
        string responseType,
        CancellationToken cancellationToken)
    {
        using var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            Proxy = WebRequest.DefaultWebProxy
        };
        using var client = new HttpClient(handler);
        var currentUri = uri;
        HttpResponseMessage? response = null;

        for (var redirect = 0; redirect <= context.Project.Manifest.Network.RedirectLimit; redirect++)
        {
            using var message = new HttpRequestMessage(new HttpMethod(method), currentUri);
            foreach (var pair in headers)
            {
                if (!message.Headers.TryAddWithoutValidation(pair.Key, pair.Value))
                {
                    message.Content ??= new ByteArrayContent(Array.Empty<byte>());
                    message.Content.Headers.TryAddWithoutValidation(pair.Key, pair.Value);
                }
            }

            if (body is not null)
            {
                message.Content = new ByteArrayContent(body);
                if (headers.TryGetValue("Content-Type", out var contentType))
                {
                    message.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
                }
            }

            response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!IsRedirect(response.StatusCode))
            {
                break;
            }

            if (redirect == context.Project.Manifest.Network.RedirectLimit)
            {
                throw new BridgeException(BridgeErrorCodes.PermissionDenied, "Redirect limit exceeded.");
            }

            var location = response.Headers.Location;
            response.Dispose();
            if (location is null)
            {
                throw new BridgeException(BridgeErrorCodes.InvalidRequest, "Redirect response did not include a Location header.");
            }

            currentUri = location.IsAbsoluteUri ? location : new Uri(currentUri, location);
            if (!IsAllowedNetworkOrigin(context, currentUri))
            {
                throw new BridgeException(BridgeErrorCodes.PermissionDenied, "Redirect origin is not allowed by network.allowedOrigins.");
            }

            if (!context.Project.Manifest.Network.AllowLan && await IsLanHostAsync(currentUri.Host, cancellationToken).ConfigureAwait(false))
            {
                throw new BridgeException(BridgeErrorCodes.PermissionDenied, "Redirect to local network is blocked by network.allowLan.");
            }

            if (response.StatusCode == HttpStatusCode.SeeOther)
            {
                method = "GET";
                body = null;
            }
        }

        if (response is null)
        {
            throw new BridgeException(BridgeErrorCodes.InternalError, "No response was received.");
        }

        using (response)
        {
            var bytes = await ReadResponseBytesAsync(response, context.Project.Manifest.Network.MaxResponseBytes, cancellationToken).ConfigureAwait(false);
            var headersOut = response.Headers.Concat(response.Content.Headers)
                .ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
            var asBase64 = responseType.Equals("base64", StringComparison.OrdinalIgnoreCase);
            return new
            {
                url = response.RequestMessage?.RequestUri?.ToString() ?? currentUri.ToString(),
                status = (int)response.StatusCode,
                ok = response.IsSuccessStatusCode,
                headers = headersOut,
                body = asBase64 ? null : DecodeResponse(bytes, response.Content.Headers.ContentType?.CharSet),
                base64 = asBase64 ? Convert.ToBase64String(bytes) : null
            };
        }
    }

    private static IReadOnlyDictionary<string, string> ReadHeaders(JsonElement payload)
    {
        if (!payload.TryGetProperty("headers", out var headers) || headers.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return new Dictionary<string, string>();
        }

        if (headers.ValueKind != JsonValueKind.Object)
        {
            throw new BridgeException(BridgeErrorCodes.InvalidRequest, "payload.headers must be an object.");
        }

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in headers.EnumerateObject())
        {
            if (BlockedHeaders.Contains(header.Name))
            {
                throw new BridgeException(BridgeErrorCodes.PermissionDenied, $"Header is blocked: {header.Name}");
            }

            if (header.Value.ValueKind != JsonValueKind.String)
            {
                throw new BridgeException(BridgeErrorCodes.InvalidRequest, $"payload.headers.{header.Name} must be a string.");
            }

            result[header.Name] = header.Value.GetString() ?? "";
        }

        return result;
    }

    private static byte[]? ReadRequestBody(BridgeCommandContext context, JsonElement payload)
    {
        if (payload.TryGetProperty("bodyBase64", out var base64) && base64.ValueKind == JsonValueKind.String)
        {
            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(base64.GetString() ?? "");
            }
            catch (FormatException)
            {
                throw new BridgeException(BridgeErrorCodes.InvalidRequest, "payload.bodyBase64 must be valid base64.");
            }

            if (bytes.Length > context.Project.Manifest.Security.MaxPayloadBytes)
            {
                throw new BridgeException(BridgeErrorCodes.PayloadTooLarge, "Request body is larger than security.maxPayloadBytes.");
            }

            return bytes;
        }

        if (payload.TryGetProperty("body", out var body) && body.ValueKind == JsonValueKind.String)
        {
            var text = body.GetString() ?? "";
            if (Encoding.UTF8.GetByteCount(text) > context.Project.Manifest.Security.MaxPayloadBytes)
            {
                throw new BridgeException(BridgeErrorCodes.PayloadTooLarge, "Request body is larger than security.maxPayloadBytes.");
            }

            return Encoding.UTF8.GetBytes(text);
        }

        return null;
    }

    private static async Task<byte[]> ReadResponseBytesAsync(HttpResponseMessage response, int maxBytes, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read <= 0)
            {
                break;
            }

            if (output.Length + read > maxBytes)
            {
                throw new BridgeException(BridgeErrorCodes.PayloadTooLarge, "Response is larger than network.maxResponseBytes.");
            }

            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }

    private static bool IsAllowedNetworkOrigin(BridgeCommandContext context, Uri uri)
    {
        var origin = GetOrigin(uri);
        return context.Project.Manifest.Network.AllowedOrigins.Any(allowed =>
            allowed.Equals(origin, StringComparison.OrdinalIgnoreCase)
            || allowed.Equals("*", StringComparison.Ordinal)
            || (allowed.StartsWith("https://*.", StringComparison.OrdinalIgnoreCase)
                && uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)
                && uri.Host.EndsWith(allowed["https://*.".Length..], StringComparison.OrdinalIgnoreCase)));
    }

    private static string GetOrigin(Uri uri)
    {
        var defaultPort = uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ? 443 : 80;
        return uri.IsDefaultPort || uri.Port == defaultPort
            ? $"{uri.Scheme}://{uri.Host}"
            : $"{uri.Scheme}://{uri.Host}:{uri.Port}";
    }

    private static async Task<bool> IsLanHostAsync(string host, CancellationToken cancellationToken)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (IPAddress.TryParse(host, out var address))
        {
            return IsPrivateAddress(address);
        }

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
            return addresses.Any(IsPrivateAddress);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsPrivateAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return bytes[0] == 10
                || bytes[0] == 127
                || bytes[0] == 169 && bytes[1] == 254
                || bytes[0] == 172 && bytes[1] is >= 16 and <= 31
                || bytes[0] == 192 && bytes[1] == 168;
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || bytes[0] == 0xFC || bytes[0] == 0xFD;
        }

        return false;
    }

    private static bool IsRedirect(HttpStatusCode statusCode)
    {
        return statusCode is HttpStatusCode.Moved
            or HttpStatusCode.Found
            or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;
    }

    private static string DecodeResponse(byte[] bytes, string? charset)
    {
        if (!string.IsNullOrWhiteSpace(charset))
        {
            try
            {
                return Encoding.GetEncoding(charset).GetString(bytes);
            }
            catch (ArgumentException)
            {
            }
        }

        return Encoding.UTF8.GetString(bytes);
    }
}
