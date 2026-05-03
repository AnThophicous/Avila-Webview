using System.Text.Json;
using System.Text.Json.Serialization;

namespace Avila.Bridge;

public sealed class BridgeRequest
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("command")]
    public string Command { get; set; } = "";

    [JsonPropertyName("payload")]
    public JsonElement Payload { get; set; }

    [JsonPropertyName("timestamp")]
    public long Timestamp { get; set; }

    [JsonPropertyName("capability")]
    public string Capability { get; set; } = "";
}

public sealed class BridgeResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("result")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Result { get; set; }

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BridgeError? Error { get; set; }

    public static BridgeResponse Success(string id, object? result) => new()
    {
        Id = id,
        Ok = true,
        Result = result
    };

    public static BridgeResponse Failure(string id, string code, string message, bool safe = true) => new()
    {
        Id = id,
        Ok = false,
        Error = new BridgeError(code, message, safe)
    };
}

public sealed record BridgeError(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("safe")] bool Safe);
