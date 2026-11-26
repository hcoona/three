using System.Text.Json;
using System.Text.Json.Nodes;

namespace LiteAgensh;

internal static class ToolArguments
{
    public static void Fields(JsonObject arguments, params ReadOnlySpan<string> allowed)
    {
        foreach (KeyValuePair<string, JsonNode?> field in arguments)
        {
            bool permitted = false;
            foreach (string candidate in allowed)
            {
                if (string.Equals(field.Key, candidate, StringComparison.Ordinal))
                {
                    permitted = true;
                    break;
                }
            }

            if (!permitted)
            {
                throw Invalid($"Unknown field '{field.Key}'.");
            }
        }
    }

    public static string Text(JsonObject arguments, string field)
    {
        if (
            arguments[field] is not JsonValue value
            || !value.TryGetValue<string>(out string? text)
            || string.IsNullOrWhiteSpace(text)
        )
        {
            throw Invalid($"'{field}' must be a nonempty string.");
        }

        return text;
    }

    public static string? OptionalText(JsonObject arguments, string field, bool allowEmpty = false)
    {
        if (!arguments.ContainsKey(field))
        {
            return null;
        }

        if (!allowEmpty)
        {
            return Text(arguments, field);
        }

        if (arguments[field] is not JsonValue value || !value.TryGetValue<string>(out string? text))
        {
            throw Invalid($"'{field}' must be a string.");
        }

        return text;
    }

    public static long Integer(JsonObject arguments, string field, long minimum = 1)
    {
        if (!TryInteger(arguments[field], out long number) || number < minimum)
        {
            throw Invalid($"'{field}' must be a JSON integer >= {minimum}.");
        }

        return number;
    }

    private static bool TryInteger(JsonNode? node, out long number)
    {
        number = 0;
        if (node is not JsonValue value || value.GetValueKind() != JsonValueKind.Number)
        {
            return false;
        }

        if (value.TryGetValue<JsonElement>(out JsonElement element))
        {
            return element.TryGetInt64(out number);
        }

        if (value.TryGetValue<long>(out number))
        {
            return true;
        }

        if (value.TryGetValue<int>(out int intValue))
        {
            number = intValue;
            return true;
        }

        return JsonSerializer.SerializeToElement(value, Json.Options).TryGetInt64(out number);
    }

    public static bool Boolean(JsonObject arguments, string field)
    {
        if (!arguments.ContainsKey(field))
        {
            return false;
        }

        if (arguments[field] is not JsonValue value || !value.TryGetValue<bool>(out bool boolean))
        {
            throw Invalid($"'{field}' must be a boolean.");
        }

        return boolean;
    }

    public static string[] Strings(JsonObject arguments, string field, bool required = false)
    {
        if (!arguments.ContainsKey(field) && !required)
        {
            return [];
        }

        if (arguments[field] is not JsonArray array)
        {
            throw Invalid($"'{field}' must be an array of strings.");
        }

        string[] items = new string[array.Count];
        for (int index = 0; index < array.Count; index++)
        {
            if (array[index] is not JsonValue value || !value.TryGetValue<string>(out string? text))
            {
                throw Invalid($"'{field}' must contain strings.");
            }

            items[index] = text;
        }

        return items;
    }

    public static long[] PositiveIntegers(JsonObject arguments, string field)
    {
        if (!arguments.TryGetPropertyValue(field, out JsonNode? value))
        {
            return [];
        }

        if (value is not JsonArray array)
        {
            throw Invalid($"'{field}' must be an array of positive integer IDs.");
        }

        long[] items = new long[array.Count];
        for (int index = 0; index < array.Count; index++)
        {
            if (!TryInteger(array[index], out long id) || id <= 0)
            {
                throw Invalid($"'{field}' must be an array of positive integer IDs.");
            }

            items[index] = id;
        }

        return items;
    }

    public static (long Cursor, int Limit) Pagination(JsonObject arguments) =>
        (arguments.ContainsKey("cursor") ? Integer(arguments, "cursor", 0) : 0, Limit(arguments));

    public static int Limit(JsonObject arguments)
    {
        long limit = arguments.ContainsKey("limit") ? Integer(arguments, "limit") : 50;
        if (limit > 200)
        {
            throw Invalid("'limit' must be between 1 and 200.");
        }

        return (int)limit;
    }

    public static OperationException Invalid(string message) => new("invalid_arguments", message);

    public static OperationException Missing(string kind, long id) =>
        new("not_found", $"{kind} {id} does not exist.");
}
