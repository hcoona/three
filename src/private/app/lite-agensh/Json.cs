using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace LiteAgensh;

public static class Json
{
    public static JsonSerializerOptions Options { get; } =
        new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectNullableAnnotations = true,
        };

    public static JsonObject SerializePayload(object value) =>
        JsonSerializer.SerializeToNode(value, Options)?.AsObject()
        ?? throw new InvalidOperationException("Expected a JSON object.");
}

public static class Operation
{
    public static JsonObject Success(object? data = null, string message = "") =>
        new()
        {
            ["ok"] = true,
            ["code"] = "ok",
            ["message"] = message,
            ["data"] = JsonSerializer.SerializeToNode(data, Json.Options),
        };

    public static JsonObject Failure(string code, string message) =>
        new()
        {
            ["ok"] = false,
            ["code"] = code,
            ["message"] = message,
            ["data"] = null,
        };
}

public sealed class OperationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
