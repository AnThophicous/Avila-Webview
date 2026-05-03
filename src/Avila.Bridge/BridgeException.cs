namespace Avila.Bridge;

public sealed class BridgeException : Exception
{
    public BridgeException(string code, string message, bool safe = true)
        : base(message)
    {
        Code = code;
        Safe = safe;
    }

    public string Code { get; }

    public bool Safe { get; }
}

public static class BridgeErrorCodes
{
    public const string InvalidRequest = "INVALID_REQUEST";
    public const string InvalidOrigin = "INVALID_ORIGIN";
    public const string InvalidCapability = "INVALID_CAPABILITY";
    public const string PermissionDenied = "PERMISSION_DENIED";
    public const string UnknownCommand = "UNKNOWN_COMMAND";
    public const string PayloadTooLarge = "PAYLOAD_TOO_LARGE";
    public const string Timeout = "TIMEOUT";
    public const string InternalError = "INTERNAL_ERROR";
    public const string NotImplemented = "NOT_IMPLEMENTED";
}
