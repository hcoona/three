using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;

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

    internal static bool IsExpected(Exception exception) =>
        exception
            is OperationException
                or IOException
                or InvalidOperationException
                or JsonException
                or SqliteException
                or ArgumentException
                or TimeoutException
                or UnauthorizedAccessException
                or System.ComponentModel.Win32Exception;

    internal static void AddSettlementDetails(
        JsonArray details, string? worker, string? reservationId, string phase, Exception exception
    )
    {
        try
        {
            // Later settlement can rethrow this exception and replace its original stack.
            details.Add(new JsonObject
            {
                ["worker"] = worker,
                ["reservationId"] = reservationId,
                ["phase"] = phase,
                ["error"] = ExceptionDetails(exception),
            });
        }
        catch (Exception diagnosticFailure) when (IsExpected(diagnosticFailure))
        {
            // Diagnostic retention must not replace the original failure or settlement.
        }
    }

    internal static JsonObject ExceptionDetails(Exception exception)
    {
        JsonObject details = new()
        {
            ["type"] = exception.GetType().FullName,
            ["message"] = exception.Message,
            ["stackTrace"] = exception.StackTrace,
            ["phase"] = exception.Data["liteagensh.phase"] as string,
            ["batchId"] = exception.Data["liteagensh.batchId"] is long batch
                ? JsonValue.Create(batch) : null,
        };
        if (exception is OperationException operation)
        {
            details["operationCode"] = operation.Code;
        }
        if (exception is SqliteException sqlite)
        {
            details["sqliteErrorCode"] = sqlite.SqliteErrorCode;
            details["sqliteExtendedErrorCode"] = sqlite.SqliteExtendedErrorCode;
        }
        if (exception is AggregateException aggregate)
        {
            details["innerExceptions"] = new JsonArray(
                aggregate.InnerExceptions.Select(error => (JsonNode)ExceptionDetails(error))
                    .ToArray()
            );
        }
        else if (exception.InnerException is { } inner)
        {
            details["innerException"] = ExceptionDetails(inner);
        }
        return details;
    }
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

public sealed class OperationException(
    string code, string message, Exception? innerException = null
) : Exception(message, innerException)
{
    public string Code { get; } = code;
}
