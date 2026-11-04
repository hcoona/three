using System.Text.Json;
using System.Text.Json.Nodes;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace LiteAgensh;

internal static class ConfigurationInput
{
    internal static async Task<RunConfiguration> LoadAsync(
        string path,
        CancellationToken cancellationToken
    )
    {
        string text = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        string extension = Path.GetExtension(path);
        if (
            !extension.Equals(".yaml", StringComparison.OrdinalIgnoreCase)
            && !extension.Equals(".yml", StringComparison.OrdinalIgnoreCase)
        )
        {
            return JsonSerializer.Deserialize<RunConfiguration>(text, Json.Options)
                ?? throw new ArgumentException("Configuration must be a JSON object.");
        }

        JsonObject input;
        try
        {
            Parser parser = new(new StringReader(text));
            parser.Consume<StreamStart>();
            Dictionary<string, object?> document =
                new DeserializerBuilder()
                    .WithDuplicateKeyChecking()
                    .WithAttemptingUnquotedStringTypeDeserialization()
                    .Build()
                    .Deserialize<Dictionary<string, object?>>(parser)
                ?? throw new ArgumentException("Configuration must be a YAML mapping.");
            parser.Consume<StreamEnd>();
            string json = new SerializerBuilder().JsonCompatible().Build().Serialize(document);
            input = JsonNode.Parse(json)!.AsObject();
        }
        catch (YamlException exception)
        {
            throw new ArgumentException(
                $"Invalid YAML configuration: {exception.Message}",
                exception
            );
        }

        if (input.ContainsKey("goal"))
        {
            throw new ArgumentException("YAML configuration requires goalFile, not inline goal.");
        }

        if (
            !input.Remove("goalFile", out JsonNode? goalNode)
            || goalNode is not JsonValue goalValue
            || !goalValue.TryGetValue(out string? goalFile)
            || string.IsNullOrWhiteSpace(goalFile)
        )
        {
            throw new ArgumentException("YAML configuration requires a nonempty goalFile path.");
        }

        string directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        input["goal"] = await File.ReadAllTextAsync(
                ResolvePath(goalFile, directory),
                cancellationToken
            )
            .ConfigureAwait(false);
        RunConfiguration configuration = input.Deserialize<RunConfiguration>(Json.Options)!;
        return configuration with
        {
            RepositoryPath = ResolvePath(configuration.RepositoryPath, directory),
            StatePath = ResolvePath(configuration.StatePath, directory),
            CampaignLedgerPath = ResolvePath(configuration.CampaignLedgerPath, directory),
        };
    }

    private static string ResolvePath(string path, string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.IsPathFullyQualified(path) ? path : Path.GetFullPath(path, directory);
    }
}
