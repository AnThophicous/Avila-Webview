using System.Text;
using System.Text.Json;
using Avila.Security;

namespace Avila.Bridge;

public sealed class BridgeHost
{
    private static readonly JsonSerializerOptions ResponseJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly BridgeCommandContext _context;
    private readonly BridgeCommandRegistry _registry;
    private readonly PermissionPolicy _permissions;
    private readonly OriginPolicy _origins;
    private readonly CapabilityManager _capabilities;

    public BridgeHost(
        BridgeCommandContext context,
        BridgeCommandRegistry registry,
        CapabilityManager capabilities)
    {
        _context = context;
        _registry = registry;
        _capabilities = capabilities;
        _permissions = new PermissionPolicy(context.Project.Manifest);
        _origins = new OriginPolicy(context.Project.Manifest);
    }

    public async Task<string> HandleMessageAsync(string source, string json, CancellationToken cancellationToken = default)
    {
        BridgeResponse response;
        var requestId = "unknown";

        try
        {
            EnforcePayloadLimit(json);
            var request = DeserializeRequest(json);
            requestId = request.Id;
            ValidateRequestShape(request);
            ValidateSecurity(source, request);

            if (!_registry.TryGet(request.Command, out var command))
            {
                throw new BridgeException(BridgeErrorCodes.UnknownCommand, $"Command {request.Command} is not registered.");
            }

            var normalizedOrigin = OriginPolicy.Normalize(source);
            if (normalizedOrigin != OriginPolicy.LocalOrigin && !command.AllowRemote)
            {
                throw new BridgeException(BridgeErrorCodes.PermissionDenied, $"Command {request.Command} is not allowed for remote content.");
            }

            if (command.RequiresPermission && !_permissions.IsAllowed(request.Command))
            {
                throw new BridgeException(BridgeErrorCodes.PermissionDenied, $"Command {request.Command} is not allowed.");
            }

            _context.Diagnostics.CountBridgeCall(request.Command);
            _context.Logger.Trace($"bridge call: {request.Command}");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_context.Project.Manifest.Security.BridgeTimeoutMs);
            var result = await command.Handler(_context, request, timeout.Token).ConfigureAwait(false);
            response = BridgeResponse.Success(request.Id, result);
        }
        catch (BridgeException exception)
        {
            _context.Diagnostics.CountError(exception.Code);
            response = BridgeResponse.Failure(requestId, exception.Code, exception.Message, exception.Safe);
        }
        catch (TimeoutException)
        {
            _context.Diagnostics.CountError(BridgeErrorCodes.Timeout);
            response = BridgeResponse.Failure(requestId, BridgeErrorCodes.Timeout, "Command timed out.");
        }
        catch (OperationCanceledException)
        {
            _context.Diagnostics.CountError(BridgeErrorCodes.Timeout);
            response = BridgeResponse.Failure(requestId, BridgeErrorCodes.Timeout, "Command was cancelled or timed out.");
        }
        catch (Exception exception)
        {
            _context.Diagnostics.CountError(BridgeErrorCodes.InternalError);
            _context.Logger.Error(exception, "Bridge command failed");
            var message = _context.Mode.Equals("dev", StringComparison.OrdinalIgnoreCase)
                ? _context.Logger.Sanitize(exception.Message)
                : "The command failed.";
            response = BridgeResponse.Failure(requestId, BridgeErrorCodes.InternalError, message);
        }

        return JsonSerializer.Serialize(response, ResponseJsonOptions);
    }

    private void EnforcePayloadLimit(string json)
    {
        var maxPayloadBytes = _context.Project.Manifest.Security.MaxPayloadBytes;
        if (Encoding.UTF8.GetByteCount(json) > maxPayloadBytes)
        {
            throw new BridgeException(BridgeErrorCodes.PayloadTooLarge, "Bridge payload exceeds configured limit.");
        }
    }

    private static BridgeRequest DeserializeRequest(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<BridgeRequest>(json)
                ?? throw new BridgeException(BridgeErrorCodes.InvalidRequest, "Bridge request is empty.");
        }
        catch (JsonException)
        {
            throw new BridgeException(BridgeErrorCodes.InvalidRequest, "Bridge request must be valid JSON.");
        }
    }

    private static void ValidateRequestShape(BridgeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Id) || request.Id.Length > 96)
        {
            throw new BridgeException(BridgeErrorCodes.InvalidRequest, "Bridge request id is required.");
        }

        if (!request.Type.Equals("avila.invoke", StringComparison.Ordinal))
        {
            throw new BridgeException(BridgeErrorCodes.InvalidRequest, "Bridge request type is invalid.");
        }

        if (!PermissionPolicy.IsValidCommandName(request.Command))
        {
            throw new BridgeException(BridgeErrorCodes.InvalidRequest, "Bridge command name is invalid.");
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (request.Timestamp <= 0 || Math.Abs(now - request.Timestamp) > TimeSpan.FromMinutes(10).TotalMilliseconds)
        {
            throw new BridgeException(BridgeErrorCodes.InvalidRequest, "Bridge request timestamp is invalid.");
        }

        if (request.Payload.ValueKind is JsonValueKind.Undefined)
        {
            throw new BridgeException(BridgeErrorCodes.InvalidRequest, "Bridge payload is required.");
        }
    }

    private void ValidateSecurity(string source, BridgeRequest request)
    {
        if (!_origins.IsAllowed(source))
        {
            throw new BridgeException(BridgeErrorCodes.InvalidOrigin, "Bridge origin is not allowed.");
        }

        if (!_capabilities.Verify(request.Capability))
        {
            throw new BridgeException(BridgeErrorCodes.InvalidCapability, "Bridge capability is invalid.");
        }
    }
}
