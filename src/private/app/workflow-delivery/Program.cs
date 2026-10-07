using System.Text.Json;
using System.Text.Json.Serialization;
using WorkflowDelivery.CI;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery;

internal static class Program
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = new System.Text.UTF8Encoding(false, true);
        return Run(args, Console.Out, Console.Error);
    }

    internal static int Run(string[] args, TextWriter output, TextWriter error,
        Func<CiPlan, NodeRunRequest, CancellationToken, Task<NodeRunResult>>? runNode = null,
        Func<CiPlan, DotNetRunRequest, CancellationToken, Task<DotNetGroupRunResult>>?
            runDotNet = null)
    {
        try
        {
            if (args is ["ci", "plan-dotnet-group", var dotnetGroupPath])
            {
                DotNetGroupRequest request = JsonSerializer.Deserialize(
                    File.ReadAllText(dotnetGroupPath), TransferJson.Default.DotNetGroupRequest
                ) ?? throw new InvalidDataException("Missing native .NET group request.");
                DotNetGroupReadback result = DotNetGroupReader.ReadAsync(request,
                    CancellationToken.None).GetAwaiter().GetResult();
                output.WriteLine(JsonSerializer.Serialize(result,
                    TransferJson.Default.DotNetGroupReadback));
                return 0;
            }
            if (args is ["ci", "run-dotnet", var dotnetPlanPath, var dotnetRequestPath])
            {
                CiPlan plan = JsonSerializer.Deserialize(File.ReadAllText(dotnetPlanPath),
                    TransferJson.Default.CiPlan) ??
                    throw new InvalidDataException("Missing native .NET execution plan.");
                DotNetRunRequest request =
                    JsonSerializer.Deserialize(File.ReadAllText(dotnetRequestPath),
                    TransferJson.Default.DotNetRunRequest) ??
                    throw new InvalidDataException("Missing native .NET execution request.");
                DotNetGroupRunResult result = (runDotNet ?? DotNetGroupExecution.RunAsync)(plan,
                    request, CancellationToken.None).GetAwaiter().GetResult();
                output.WriteLine(JsonSerializer.Serialize(result,
                    TransferJson.Default.DotNetGroupRunResult));
                return result.Candidate == plan.Candidate &&
                    ResultCollector.Collect(plan, result.Results).Satisfied ? 0 : 1;
            }
            if (args is ["ci", "comparison", var eventName, var eventPath, var testedCandidate])
            {
                using JsonDocument payload = JsonDocument.Parse(File.ReadAllText(eventPath,
                    new System.Text.UTF8Encoding(false, true)));
                CiComparison comparison = GitHubComparison.Resolve(eventName, payload.RootElement,
                    testedCandidate);
                output.WriteLine(JsonSerializer.Serialize(comparison,
                    TransferJson.Default.CiComparison));
                return 0;
            }
            if (args is ["ci", "control-inputs", var controlInputPath])
            {
                ControlTestInputRequest request = JsonSerializer.Deserialize(
                    File.ReadAllText(controlInputPath, new System.Text.UTF8Encoding(false, true)),
                    TransferJson.Default.ControlTestInputRequest
                ) ?? throw new InvalidDataException("Missing control input request.");
                ControlTestInputResponse result = ControlTestInputs.ReadAsync(request,
                    CancellationToken.None).GetAwaiter().GetResult();
                output.WriteLine(JsonSerializer.Serialize(result,
                    TransferJson.Default.ControlTestInputResponse));
                return 0;
            }
            if (args is ["ci", "run-node", var executionPlanPath, var executionRequestPath])
            {
                CiPlan plan = JsonSerializer.Deserialize(
                    File.ReadAllText(executionPlanPath), TransferJson.Default.CiPlan
                ) ?? throw new InvalidDataException("Missing Node execution plan.");
                NodeRunRequest request = JsonSerializer.Deserialize(
                    File.ReadAllText(executionRequestPath), TransferJson.Default.NodeRunRequest
                ) ?? throw new InvalidDataException("Missing Node execution request.");
                CiPlan selected = NodeExecution.SelectRuntime(plan, request.Runtime);
                NodeRunResult result = (runNode ?? NodeExecution.RunAsync)(plan, request,
                    CancellationToken.None).GetAwaiter().GetResult();
                output.WriteLine(JsonSerializer.Serialize(result,
                    TransferJson.Default.NodeRunResult));
                return result.Candidate == plan.Candidate && result.Runtime == request.Runtime &&
                    ResultCollector.Collect(selected, result.Results).Satisfied ? 0 : 1;
            }
            if (args is ["ci", "plan-node-group", var groupRequestPath])
            {
                NodeGroupRequest request = JsonSerializer.Deserialize(
                    File.ReadAllText(groupRequestPath), TransferJson.Default.NodeGroupRequest
                ) ?? throw new InvalidDataException("Missing Node group request.");
                NodeGroupReadback result = NodeGroupReader.ReadAsync(request,
                    CancellationToken.None).GetAwaiter().GetResult();
                output.WriteLine(JsonSerializer.Serialize(result,
                    TransferJson.Default.NodeGroupReadback));
                return 0;
            }
            if (args is ["ci", "plan-node", var nodeRequestPath])
            {
                NodePlanRequest request = JsonSerializer.Deserialize(
                    File.ReadAllText(nodeRequestPath), TransferJson.Default.NodePlanRequest
                ) ?? throw new InvalidDataException("Missing Node planning request.");
                CiPlan plan = NodePlanning.PlanAsync(request, CancellationToken.None)
                    .GetAwaiter().GetResult();
                output.WriteLine(JsonSerializer.Serialize(plan, TransferJson.Default.CiPlan));
                return 0;
            }
            if (args is ["ci", "plan", var requestPath])
            {
                PlanRequest request = JsonSerializer.Deserialize(
                    File.ReadAllText(requestPath), TransferJson.Default.PlanRequest
                ) ?? throw new InvalidDataException("Missing planning request.");
                output.WriteLine(JsonSerializer.Serialize(ImpactPlanner.Plan(request),
                    TransferJson.Default.CiPlan));
                return 0;
            }
            if (args is ["ci", "result", var planPath, var resultsPath])
            {
                CiPlan plan = JsonSerializer.Deserialize(
                    File.ReadAllText(planPath), TransferJson.Default.CiPlan
                ) ?? throw new InvalidDataException("Missing CI plan.");
                CheckResult[] results = JsonSerializer.Deserialize(
                    File.ReadAllText(resultsPath), TransferJson.Default.CheckResultArray
                ) ?? throw new InvalidDataException("Missing CI results.");
                CiOutcome result = ResultCollector.Collect(plan, results);
                output.WriteLine(JsonSerializer.Serialize(result, TransferJson.Default.CiOutcome));
                return result.Satisfied ? 0 : 1;
            }
            error.WriteLine(
                "Usage: workflow-delivery ci plan <request.json>"
                    + " | ci comparison <event-name> <native-event.json> <tested-candidate>"
                    + " | ci control-inputs <request.json>"
                    + " | ci plan-node <request.json>"
                    + " | ci plan-node-group <request.json>"
                    + " | ci plan-dotnet-group <request.json>"
                    + " | ci run-dotnet <plan.json> <request.json>"
                    + " | ci run-node <plan.json> <request.json>"
                    + " | ci result <plan.json> <results.json>"
            );
            return 2;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or
            JsonException or ArgumentException or UnauthorizedAccessException or TimeoutException or
            Nerdbank.GitVersioning.GitException)
        {
            error.WriteLine(exception.Message);
            return 2;
        }
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    Converters = new[] { typeof(CheckStatusJsonConverter), typeof(NativeTerminationJsonConverter),
        typeof(CheckOriginJsonConverter) },
    WriteIndented = true,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
)]
[JsonSerializable(typeof(PlanRequest))]
[JsonSerializable(typeof(CiComparison))]
[JsonSerializable(typeof(ControlTestInputRequest))]
[JsonSerializable(typeof(ControlTestInputResponse))]
[JsonSerializable(typeof(DotNetGraphRequest))]
[JsonSerializable(typeof(DotNetGraphResponse))]
[JsonSerializable(typeof(KeyValuePair<string, string>[]))]
[JsonSerializable(typeof(DotNetRunRequest))]
[JsonSerializable(typeof(DotNetRunResult))]
[JsonSerializable(typeof(DotNetPackageRunResult))]
[JsonSerializable(typeof(DotNetGroupRequest))]
[JsonSerializable(typeof(DotNetGroupReadback))]
[JsonSerializable(typeof(DotNetGroupRunResult))]
[JsonSerializable(typeof(NodePlanRequest))]
[JsonSerializable(typeof(NodeGroupRequest))]
[JsonSerializable(typeof(NodeGroupReadback))]
[JsonSerializable(typeof(NodeRunRequest))]
[JsonSerializable(typeof(NodeRunResult))]
[JsonSerializable(typeof(CiPlan))]
[JsonSerializable(typeof(CheckResult[]))]
[JsonSerializable(typeof(CiOutcome))]
internal partial class TransferJson : JsonSerializerContext;

internal sealed class CheckStatusJsonConverter()
    : JsonStringEnumConverter<CheckStatus>(allowIntegerValues: false);

internal sealed class NativeTerminationJsonConverter()
    : JsonStringEnumConverter<NativeTermination>(allowIntegerValues: false);

internal sealed class CheckOriginJsonConverter()
    : JsonStringEnumConverter<CheckOrigin>(allowIntegerValues: false);
