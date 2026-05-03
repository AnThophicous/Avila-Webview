using System.Text;
using System.Text.Json;

namespace Avila.Bridge;

internal static class PayloadReader
{
    public static string GetString(JsonElement payload, string name, int maxLength, bool required = true)
    {
        if (!payload.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            if (required)
            {
                throw new BridgeException(BridgeErrorCodes.InvalidRequest, $"payload.{name} is required.");
            }

            return "";
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw new BridgeException(BridgeErrorCodes.InvalidRequest, $"payload.{name} must be a string.");
        }

        var text = value.GetString() ?? "";
        if (text.Length > maxLength)
        {
            throw new BridgeException(BridgeErrorCodes.InvalidRequest, $"payload.{name} is too long.");
        }

        return text;
    }

    public static int GetInt(JsonElement payload, string name, int min, int max)
    {
        if (!payload.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number))
        {
            throw new BridgeException(BridgeErrorCodes.InvalidRequest, $"payload.{name} must be an integer.");
        }

        if (number < min || number > max)
        {
            throw new BridgeException(BridgeErrorCodes.InvalidRequest, $"payload.{name} is out of range.");
        }

        return number;
    }

    public static bool GetBoolean(JsonElement payload, string name)
    {
        if (!payload.TryGetProperty(name, out var value) || value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new BridgeException(BridgeErrorCodes.InvalidRequest, $"payload.{name} must be a boolean.");
        }

        return value.GetBoolean();
    }

    public static bool GetOptionalBoolean(JsonElement payload, string name, bool defaultValue)
    {
        if (!payload.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return defaultValue;
        }

        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new BridgeException(BridgeErrorCodes.InvalidRequest, $"payload.{name} must be a boolean.");
        }

        return value.GetBoolean();
    }

    public static int Utf8Length(string value) => Encoding.UTF8.GetByteCount(value);
}
