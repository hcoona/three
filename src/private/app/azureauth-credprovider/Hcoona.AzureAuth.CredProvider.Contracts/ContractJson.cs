using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hcoona.AzureAuth.CredProvider.Contracts;

public static class ContractJson
{
    public static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = false,
            TypeInfoResolver = ContractJsonSerializerContext.Default,
        };
        AddEnumConverters(options);
        return options;
    }

    private static void AddEnumConverters(JsonSerializerOptions options)
    {
        options.Converters.Add(new StrictCamelCaseEnumConverter<CredentialEcosystem>());
        options.Converters.Add(new StrictCamelCaseEnumConverter<CredentialOperation>());
        options.Converters.Add(new StrictCamelCaseEnumConverter<TokenAudience>());
        options.Converters.Add(new StrictCamelCaseEnumConverter<CredentialKind>());
        options.Converters.Add(new StrictCamelCaseEnumConverter<IdentityFlow>());
        options.Converters.Add(new StrictCamelCaseEnumConverter<IdentityFlowState>());
        options.Converters.Add(new StrictCamelCaseEnumConverter<InteractivePolicy>());
        options.Converters.Add(new StrictCamelCaseEnumConverter<CachePolicyMode>());
        options.Converters.Add(new StrictCamelCaseEnumConverter<CredentialResultStatus>());
        options.Converters.Add(new StrictCamelCaseEnumConverter<CredentialErrorKind>());
        options.Converters.Add(new StrictCamelCaseEnumConverter<AdapterProtocol>());
        options.Converters.Add(new StrictCamelCaseEnumConverter<AdapterHostExitCode>());
        options.Converters.Add(new StrictCamelCaseEnumConverter<ConfigurationChangeOperation>());
        options.Converters.Add(new StrictCamelCaseEnumConverter<ConfigurationTargetKind>());
        options.Converters.Add(new StrictCamelCaseEnumConverter<ConfigurationScope>());
        options.Converters.Add(new StrictCamelCaseEnumConverter<ConfigurationAtomicityPolicy>());
        options.Converters.Add(new StrictCamelCaseEnumConverter<ConfigurationRollbackPolicy>());
        options.Converters.Add(new StrictCamelCaseEnumConverter<ConfigurationPlanState>());
        options.Converters.Add(
            new StrictCamelCaseEnumConverter<ConfigurationManifestCommitPolicy>()
        );
        options.Converters.Add(
            new StrictCamelCaseEnumConverter<ConfigurationDeclarationPreservation>()
        );
        options.Converters.Add(
            new StrictCamelCaseEnumConverter<ConfigurationTemporaryContainerKind>()
        );
        options.Converters.Add(new StrictCamelCaseEnumConverter<DoctorCheckStatus>());
        options.Converters.Add(new StrictCamelCaseEnumConverter<DoctorCheckSeverity>());
        options.Converters.Add(new StrictCamelCaseEnumConverter<KeyringHelperMode>());
        options.Converters.Add(new StrictCamelCaseEnumConverter<KeyringHelperIntegrityPlatform>());
        options.Converters.Add(
            new StrictCamelCaseEnumConverter<KeyringOwnerValidationRequirement>()
        );
        options.Converters.Add(new StrictCamelCaseEnumConverter<KeyringSymlinkPolicy>());
        options.Converters.Add(new StrictCamelCaseEnumConverter<KeyringDigestPolicy>());
        options.Converters.Add(new StrictCamelCaseEnumConverter<ContractBreakingChangeKind>());
    }

    private sealed class StrictCamelCaseEnumConverter<TEnum> : JsonConverter<TEnum>
        where TEnum : struct, Enum
    {
        private static readonly Dictionary<string, TEnum> ValuesByWireName =
            CreateValuesByWireName();
        private static readonly Dictionary<TEnum, string> WireNamesByValue =
            ValuesByWireName.ToDictionary(pair => pair.Value, pair => pair.Key);

        public override TEnum Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options
        )
        {
            if (reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException($"Expected string enum value for {typeof(TEnum).Name}.");
            }

            string? value = reader.GetString();
            if (value is null || !ValuesByWireName.TryGetValue(value, out TEnum enumValue))
            {
                throw new JsonException(
                    $"Unsupported enum value '{value}' for {typeof(TEnum).Name}."
                );
            }

            return enumValue;
        }

        public override void Write(
            Utf8JsonWriter writer,
            TEnum value,
            JsonSerializerOptions options
        )
        {
            if (!WireNamesByValue.TryGetValue(value, out string? wireName))
            {
                long numericValue = Convert.ToInt64(value, CultureInfo.InvariantCulture);
                throw new JsonException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Unsupported enum value '{numericValue}' for {typeof(TEnum).Name}."
                    )
                );
            }

            writer.WriteStringValue(wireName);
        }

        private static Dictionary<string, TEnum> CreateValuesByWireName()
        {
            var valuesByWireName = new Dictionary<string, TEnum>(StringComparer.Ordinal);
            foreach (TEnum value in Enum.GetValues<TEnum>())
            {
                string name =
                    Enum.GetName(value)
                    ?? throw new JsonException($"Unnamed enum value in {typeof(TEnum).Name}.");
                valuesByWireName.Add(JsonNamingPolicy.CamelCase.ConvertName(name), value);
            }

            return valuesByWireName;
        }
    }
}
