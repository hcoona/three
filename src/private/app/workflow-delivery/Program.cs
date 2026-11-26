using System.Text.Json;
using System.Text.Json.Serialization;
using WorkflowDelivery.CI;
using WorkflowDelivery.CI.Callers;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;
using WorkflowDelivery.Release;
using WorkflowDelivery.Destinations;

namespace WorkflowDelivery;

internal static class Program
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = new System.Text.UTF8Encoding(false, true);
        return Run(args, Console.Out, Console.Error);
    }

    internal static int Run(string[] args, TextWriter output, TextWriter error)
    {
        try
        {
            if (args is ["native-message-schemas", var schemaDirectory])
            {
                NativeMessageSchemas.Write(schemaDirectory);
                return 0;
            }
            if (ReleaseCli.Run(args, output) is { } releaseStatus) return releaseStatus;
            if (CiCli.Run(args, output, error) is { } ciStatus) return ciStatus;
            error.WriteLine(
                "Usage: WorkflowDelivery ci "
                    + "caller-<control|dotnet|node|python|ruby|local-python> ..."
                    + " | ci native-preparation-plan <root> <scope.json> <directory>"
                    + " | release job-inputs <ecosystem> <operation>"
                    + " | release prepare-python-index-tools <request.json>"
                    + " | release <prepare|observe|describe|publish|complete>-<target> ..."
                    + " | native-message-schemas <directory>"
            );
            return 2;
        }
        catch (PythonGroupPreparationException exception)
        {
            exception.WriteDiagnostics(error);
            return 2;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or
            JsonException or ArgumentException or UnauthorizedAccessException or TimeoutException or
            Nerdbank.GitVersioning.GitException or KeyNotFoundException)
        {
            RunnerLog.Write(error, exception.Message, CiCallerTransfer.MessagePrefix);
            return 2;
        }
    }

}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    Converters = new[] { typeof(CheckStatusJsonConverter), typeof(NativeTerminationJsonConverter),
        typeof(CheckOriginJsonConverter), typeof(ReleaseCheckStatusJsonConverter),
        typeof(ReleaseFileStateJsonConverter), typeof(ReleaseBusinessStateJsonConverter),
        typeof(RubySourceStateJsonConverter), typeof(StoreStateJsonConverter) },
    WriteIndented = true,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
)]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(ScopeCoordinate[]))]
[JsonSerializable(typeof(CiCallerEndpoints))]
[JsonSerializable(typeof(CiCallerControl))]
[JsonSerializable(typeof(CiCallerCommand))]
[JsonSerializable(typeof(PythonCiCallerContext))]
[JsonSerializable(typeof(RubyCiCallerContext))]
[JsonSerializable(typeof(NodeCiCallerContext))]
[JsonSerializable(typeof(PythonCiCallerReadback))]
[JsonSerializable(typeof(RubyCiCallerReadback))]
[JsonSerializable(typeof(GithubNpmToolSelection))]
[JsonSerializable(typeof(NpmPreparationRequest))]
[JsonSerializable(typeof(NpmPreparationResult))]
[JsonSerializable(typeof(NuGetPreparationRequest))]
[JsonSerializable(typeof(NuGetPreparationResult))]
[JsonSerializable(typeof(PythonPreparationRequest))]
[JsonSerializable(typeof(PythonPreparationResult))]
[JsonSerializable(typeof(PythonIndexTools))]
[JsonSerializable(typeof(PythonIndexToolRequest))]
[JsonSerializable(typeof(PythonIndexToolResult))]
[JsonSerializable(typeof(PythonPublisherTools))]
[JsonSerializable(typeof(PythonIndexObservationResult))]
[JsonSerializable(typeof(PythonPublicationResult))]
[JsonSerializable(typeof(PythonCompletionResult))]
[JsonSerializable(typeof(GithubReleasePreparationRequest))]
[JsonSerializable(typeof(GithubReleasePreparationResult))]
[JsonSerializable(typeof(GithubReleaseObservationResult))]
[JsonSerializable(typeof(GithubReleasePublicationResult))]
[JsonSerializable(typeof(GithubReleaseCompletionResult))]
[JsonSerializable(typeof(RubyPreparationRequest))]
[JsonSerializable(typeof(RubyPreparationResult))]
[JsonSerializable(typeof(RubySourceTools))]
[JsonSerializable(typeof(RubyObservationResult))]
[JsonSerializable(typeof(RubyPublisherTools))]
[JsonSerializable(typeof(RubyPublicationResult))]
[JsonSerializable(typeof(RubyCompletionResult))]
[JsonSerializable(typeof(NuGetObservationResult))]
[JsonSerializable(typeof(NuGetPublicationResult))]
[JsonSerializable(typeof(NuGetCompletionResult))]
[JsonSerializable(typeof(NpmjsObservationResult))]
[JsonSerializable(typeof(ReleaseDryRun))]
[JsonSerializable(typeof(BrowserStorePreparationRequest))]
[JsonSerializable(typeof(BrowserStorePreparationResult))]
[JsonSerializable(typeof(StoreObservation))]
[JsonSerializable(typeof(BrowserStorePublicationResult))]
[JsonSerializable(typeof(BrowserStoreCompletionResult))]
[JsonSerializable(typeof(ReleaseOutcome))]
[JsonSerializable(typeof(GithubNpmObservationResult))]
[JsonSerializable(typeof(GithubNpmPublicationResult))]
[JsonSerializable(typeof(NpmjsPublicationResult))]
[JsonSerializable(typeof(ControlTestInputRequest))]
[JsonSerializable(typeof(DotNetGraphRequest))]
[JsonSerializable(typeof(DotNetGraphResponse))]
[JsonSerializable(typeof(KeyValuePair<string, string>[]))]
[JsonSerializable(typeof(DotNetRunRequest))]
[JsonSerializable(typeof(DotNetBuildRequest))]
[JsonSerializable(typeof(DotNetBuildResponse))]
[JsonSerializable(typeof(DotNetTestRequest))]
[JsonSerializable(typeof(DotNetTestEntryResult))]
[JsonSerializable(typeof(DotNetRunResult))]
[JsonSerializable(typeof(DotNetGroupRequest))]
[JsonSerializable(typeof(DotNetGroupReadback))]
[JsonSerializable(typeof(DotNetGroupRunResult))]
[JsonSerializable(typeof(PythonGroupRunRequest))]
[JsonSerializable(typeof(PythonGroupRunResult))]
[JsonSerializable(typeof(PythonGroupRequest))]
[JsonSerializable(typeof(PythonGroupReadback))]
[JsonSerializable(typeof(PythonGroupPreparationRequest))]
[JsonSerializable(typeof(PythonGroupPreparationResult))]
[JsonSerializable(typeof(PythonPreparationCommand[]))]
[JsonSerializable(typeof(RubyGroupPreparationRequest))]
[JsonSerializable(typeof(RubyFactRequest))]
[JsonSerializable(typeof(RubyGroupPreparationResult))]
[JsonSerializable(typeof(RubyGroupRequest))]
[JsonSerializable(typeof(RubyGroupReadback))]
[JsonSerializable(typeof(RubyGroupRunRequest))]
[JsonSerializable(typeof(RubyGroupRun))]
[JsonSerializable(typeof(NodeGroupRequest))]
[JsonSerializable(typeof(NodeGroupReadback))]
[JsonSerializable(typeof(Dictionary<string, string[]>),
    TypeInfoPropertyName = "RetainedDirectories")]
[JsonSerializable(typeof(NodeRunRequest))]
[JsonSerializable(typeof(NodeRunResult))]
[JsonSerializable(typeof(CiPlan))]
[JsonSerializable(typeof(CheckResult[]))]
[JsonSerializable(typeof(DotNetToolGroup[]))]
[JsonSerializable(typeof(DotNetGroupResultSet))]
[JsonSerializable(typeof(CiOutcome))]
internal partial class TransferJson : JsonSerializerContext;

internal sealed class CheckStatusJsonConverter()
    : JsonStringEnumConverter<CheckStatus>(allowIntegerValues: false);

internal sealed class NativeTerminationJsonConverter()
    : JsonStringEnumConverter<NativeTermination>(allowIntegerValues: false);

internal sealed class CheckOriginJsonConverter()
    : JsonStringEnumConverter<CheckOrigin>(allowIntegerValues: false);

internal sealed class ReleaseCheckStatusJsonConverter()
    : JsonStringEnumConverter<ReleaseCheckStatus>(allowIntegerValues: false);

internal sealed class ReleaseFileStateJsonConverter()
    : JsonStringEnumConverter<ReleaseFileState>(allowIntegerValues: false);

internal sealed class ReleaseBusinessStateJsonConverter()
    : JsonStringEnumConverter<ReleaseBusinessState>(allowIntegerValues: false);

internal sealed class RubySourceStateJsonConverter()
    : JsonStringEnumConverter<RubySourceState>(allowIntegerValues: false);

internal sealed class StoreStateJsonConverter()
    : JsonStringEnumConverter<StoreState>(allowIntegerValues: false);
