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
        Func<CiPlan, NodeRunRequest, CancellationToken, Task<NodeRunResult>>? runNode = null)
    {
        try
        {
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
    Converters = new[] { typeof(CheckStatusJsonConverter), typeof(NativeTerminationJsonConverter) },
    WriteIndented = true,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
)]
[JsonSerializable(typeof(PlanRequest))]
[JsonSerializable(typeof(CiComparison))]
[JsonSerializable(typeof(ControlTestInputRequest))]
[JsonSerializable(typeof(ControlTestInputResponse))]
[JsonSerializable(typeof(NodePlanRequest))]
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
