using System.Text.Json;
using System.Text.Json.Serialization;
using WorkflowDelivery.CI;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery;

internal static class Program
{
    private static int Main(string[] args) => Run(args, Console.Out, Console.Error);

    internal static int Run(string[] args, TextWriter output, TextWriter error)
    {
        try
        {
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
                    + " | ci plan-node <request.json>"
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
    Converters = new[] { typeof(CheckStatusJsonConverter) },
    WriteIndented = true,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
)]
[JsonSerializable(typeof(PlanRequest))]
[JsonSerializable(typeof(NodePlanRequest))]
[JsonSerializable(typeof(CiPlan))]
[JsonSerializable(typeof(CheckResult[]))]
[JsonSerializable(typeof(CiOutcome))]
internal partial class TransferJson : JsonSerializerContext;

internal sealed class CheckStatusJsonConverter()
    : JsonStringEnumConverter<CheckStatus>(allowIntegerValues: false);
