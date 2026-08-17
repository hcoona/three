using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hcoona.AzureAuth.CredProvider.Contracts;

internal sealed class ContractEnumJsonConverter<TEnum> : JsonConverter<TEnum>
    where TEnum : struct, Enum
{
    private static readonly Dictionary<string, TEnum> ValuesByWireName = CreateValuesByWireName();
    private static readonly Dictionary<TEnum, string> WireNamesByValue =
        ValuesByWireName.ToDictionary(static pair => pair.Value, static pair => pair.Key);

    public override TEnum Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        if (
            reader.TokenType == JsonTokenType.String
            && reader.GetString() is { } wireName
            && ValuesByWireName.TryGetValue(wireName, out TEnum value)
        )
        {
            return value;
        }

        throw new JsonException(
            $"The JSON value could not be converted to {typeof(TEnum).FullName}."
        );
    }

    public override void Write(
        Utf8JsonWriter writer,
        TEnum value,
        JsonSerializerOptions options
    )
    {
        if (!WireNamesByValue.TryGetValue(value, out string? wireName))
        {
            throw new JsonException(
                $"The value '{value}' is not declared by {typeof(TEnum).FullName}."
            );
        }

        writer.WriteStringValue(wireName);
    }

    private static Dictionary<string, TEnum> CreateValuesByWireName()
    {
        string[] names = Enum.GetNames<TEnum>();
        TEnum[] values = Enum.GetValues<TEnum>();
        var result = new Dictionary<string, TEnum>(names.Length, StringComparer.Ordinal);

        for (int index = 0; index < names.Length; index++)
        {
            result.Add(JsonNamingPolicy.CamelCase.ConvertName(names[index]), values[index]);
        }

        return result;
    }
}
