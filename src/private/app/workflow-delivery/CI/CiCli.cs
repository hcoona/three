using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using WorkflowDelivery.CI.Callers;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.CI;

internal sealed record CiCliOverrides(
    Func<CiPlan, NodeRunRequest, CancellationToken, Task<NodeRunResult>>? RunNode = null,
    Func<CiPlan, DotNetRunRequest, CancellationToken, Task<DotNetGroupRunResult>>? RunDotNet = null,
    Func<CiPlan, PythonPackageRunRequest, CancellationToken, Task<PythonPackageRunResult>>?
        RunPythonPackage = null,
    Func<CiPlan, PythonGroupRunRequest, CancellationToken, Task<PythonGroupRunResult>>?
        RunPythonGroup = null,
    Func<PythonGroupRequest, CancellationToken, Task<PythonGroupReadback>>? PlanPythonGroup = null,
    Func<PythonGroupPreparationRequest, CancellationToken, Task<PythonGroupPreparationResult>>?
        PreparePythonGroup = null,
    Func<RubyGroupPreparationRequest, CancellationToken, Task<RubyGroupPreparationResult>>?
        PrepareRubyGroup = null,
    Func<RubyGroupRequest, CancellationToken, Task<RubyGroupReadback>>? PlanRubyGroup = null,
    Func<CiPlan, RubyGroupRunRequest, CancellationToken, Task<RubyGroupRun>>? RunRubyGroup = null);

internal static class CiCli
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static int? Run(string[] args, TextWriter output, TextWriter error,
        CiCliOverrides? overrides = null)
    {
        if (NativePreparationQualification.Run(args, output) is { } nativeStatus)
            return nativeStatus;
        if (ControlCiCaller.Run(args, output) is { } controlStatus) return controlStatus;
        if (LocalPythonCiCaller.Run(args, output, error) is { } localStatus)
            return localStatus;
        if (CiCallerCli.Run(args, output, error) is { } callerStatus) return callerStatus;
        if (args.Length < 2 || args[0] != "ci") return null;

        CancellationToken token = CancellationToken.None;
        switch (args)
        {
            case ["ci", "comparison", var eventName, var eventPath, var testedCandidate]:
                using (JsonDocument payload = JsonDocument.Parse(
                    File.ReadAllText(eventPath, StrictUtf8)))
                {
                    Write(GitHubComparison.Resolve(eventName, payload.RootElement, testedCandidate),
                        TransferJson.Default.CiComparison);
                }
                return 0;
            case ["ci", "control-inputs", var controlInputPath]:
                Write(ControlTestInputs.ReadAsync(
                    Read(controlInputPath, TransferJson.Default.ControlTestInputRequest,
                        "Missing control input request."), token).GetAwaiter().GetResult(),
                    TransferJson.Default.ControlTestInputResponse);
                return 0;
            case ["ci", "plan", var requestPath]:
                Write(ImpactPlanner.Plan(
                    Read(requestPath, TransferJson.Default.PlanRequest,
                        "Missing planning request.")),
                    TransferJson.Default.CiPlan);
                return 0;
            case ["ci", "plan-node", var nodeRequestPath]:
                Write(NodePlanning.PlanAsync(
                    Read(nodeRequestPath, TransferJson.Default.NodePlanRequest,
                        "Missing Node planning request."), token).GetAwaiter().GetResult(),
                    TransferJson.Default.CiPlan);
                return 0;
            case ["ci", "plan-node-group", var groupRequestPath]:
                Write(NodeGroupReader.ReadAsync(
                    Read(groupRequestPath, TransferJson.Default.NodeGroupRequest,
                        "Missing Node group request."), token).GetAwaiter().GetResult(),
                    TransferJson.Default.NodeGroupReadback);
                return 0;
            case ["ci", "run-node", var executionPlanPath, var executionRequestPath]:
                {
                    CiPlan plan = Read(executionPlanPath, TransferJson.Default.CiPlan,
                        "Missing Node execution plan.");
                    NodeRunRequest request = Read(executionRequestPath,
                        TransferJson.Default.NodeRunRequest, "Missing Node execution request.");
                    CiPlan selected = NodeExecution.SelectRuntime(plan, request.Runtime);
                    NodeRunResult result = (overrides?.RunNode ?? NodeExecution.RunAsync)(
                        plan, request, token).GetAwaiter().GetResult();
                    Write(result, TransferJson.Default.NodeRunResult);
                    return result.Candidate == plan.Candidate &&
                        result.Runtime == request.Runtime &&
                        ResultCollector.Collect(selected, result.Results).Satisfied ? 0 : 1;
                }
            case ["ci", "run-dotnet", var dotnetPlanPath, var dotnetRequestPath]:
                {
                    CiPlan plan = Read(dotnetPlanPath, TransferJson.Default.CiPlan,
                        "Missing native .NET execution plan.");
                    DotNetRunRequest request = Read(dotnetRequestPath,
                        TransferJson.Default.DotNetRunRequest,
                        "Missing native .NET execution request.");
                    DotNetGroupRunResult result =
                        (overrides?.RunDotNet ?? DotNetGroupExecution.RunAsync)(
                            plan, request, token).GetAwaiter().GetResult();
                    Write(result, TransferJson.Default.DotNetGroupRunResult);
                    return result.Candidate == plan.Candidate &&
                        GroupExit(plan, result.Outcome) == 0 ? 0 : 1;
                }
            case ["ci", "prepare-python-group", var preparationPath]:
                Write((overrides?.PreparePythonGroup ?? PythonGroupPreparation.PrepareAsync)(
                    Read(preparationPath, TransferJson.Default.PythonGroupPreparationRequest,
                        "Missing native Python preparation request."), token)
                    .GetAwaiter().GetResult(),
                    TransferJson.Default.PythonGroupPreparationResult);
                return 0;
            case ["ci", "plan-python-group", var pythonGroupPath]:
                Write((overrides?.PlanPythonGroup ?? PythonGroupReader.ReadAsync)(
                    Read(pythonGroupPath, TransferJson.Default.PythonGroupRequest,
                        "Missing native Python group request."), token).GetAwaiter().GetResult(),
                    TransferJson.Default.PythonGroupReadback);
                return 0;
            case ["ci", "run-python-group", var pythonGroupPlanPath, var pythonGroupRequestPath]:
                {
                    CiPlan plan = Read(pythonGroupPlanPath, TransferJson.Default.CiPlan,
                        "Missing native Python group plan.");
                    PythonGroupRunRequest request = Read(pythonGroupRequestPath,
                        TransferJson.Default.PythonGroupRunRequest,
                        "Missing native Python group request.");
                    PythonGroupRunResult result = (overrides?.RunPythonGroup ??
                        PythonGroupExecution.RunAsync)(plan, request, token)
                        .GetAwaiter().GetResult();
                    Write(result, TransferJson.Default.PythonGroupRunResult);
                    return result.Candidate == plan.Candidate &&
                        GroupExit(plan, result.Outcome) == 0 ? 0 : 1;
                }
            case ["ci", "run-python-package", var pythonPlanPath, var pythonRequestPath]:
                {
                    CiPlan plan = Read(pythonPlanPath, TransferJson.Default.CiPlan,
                        "Missing native Python package plan.");
                    PythonPackageRunRequest request = Read(pythonRequestPath,
                        TransferJson.Default.PythonPackageRunRequest,
                        "Missing native Python package request.");
                    PythonPackageRunResult result = (overrides?.RunPythonPackage ??
                        PythonPackageExecution.RunAsync)(plan, request, token)
                        .GetAwaiter().GetResult();
                    Write(result, TransferJson.Default.PythonPackageRunResult);
                    return result.Candidate == plan.Candidate &&
                        ResultCollector.Collect(plan, result.Results).Satisfied ? 0 : 1;
                }
            case ["ci", "prepare-ruby-group", var rubyPreparationPath]:
                Write((overrides?.PrepareRubyGroup ?? RubyGroupPreparation.PrepareAsync)(
                    Read(rubyPreparationPath, TransferJson.Default.RubyGroupPreparationRequest,
                        "Missing native Ruby preparation request."), token)
                    .GetAwaiter().GetResult(),
                    TransferJson.Default.RubyGroupPreparationResult);
                return 0;
            case ["ci", "plan-ruby-group", var rubyGroupPath]:
                Write((overrides?.PlanRubyGroup ?? RubyGroupReader.ReadAsync)(
                    Read(rubyGroupPath, TransferJson.Default.RubyGroupRequest,
                        "Missing native Ruby group request."), token).GetAwaiter().GetResult(),
                    TransferJson.Default.RubyGroupReadback);
                return 0;
            case ["ci", "run-ruby-group", var rubyPlanPath, var rubyRequestPath]:
                {
                    CiPlan plan = Read(rubyPlanPath, TransferJson.Default.CiPlan,
                        "Missing native Ruby group plan.");
                    RubyGroupRunRequest request = Read(rubyRequestPath,
                        TransferJson.Default.RubyGroupRunRequest,
                        "Missing native Ruby execution request.");
                    RubyGroupRun result = (overrides?.RunRubyGroup ?? RubyGroupExecution.RunAsync)(
                        plan, request, token).GetAwaiter().GetResult();
                    Write(result, TransferJson.Default.RubyGroupRun);
                    return GroupExit(plan, result.Outcome);
                }
            case ["ci", "result", var planPath, var resultsPath]:
                {
                    CiPlan plan = Read(planPath, TransferJson.Default.CiPlan, "Missing CI plan.");
                    CheckResult[] results = Read(resultsPath, TransferJson.Default.CheckResultArray,
                        "Missing CI results.");
                    CiOutcome outcome = ResultCollector.Collect(plan, results);
                    Write(outcome, TransferJson.Default.CiOutcome);
                    return outcome.Satisfied ? 0 : 1;
                }
            default:
                return null;
        }

        void Write<T>(T value, JsonTypeInfo<T> type) =>
            output.WriteLine(JsonSerializer.Serialize(value, type));
    }

    private static T Read<T>(string path, JsonTypeInfo<T> type, string missingMessage) =>
        JsonSerializer.Deserialize(File.ReadAllText(path, StrictUtf8), type)
            ?? throw new InvalidDataException(missingMessage);

    private static int GroupExit(CiPlan plan, CiOutcome outcome) =>
        CiCallerTransfer.Outcome(plan, outcome).Satisfied ? 0 : 1;
}
