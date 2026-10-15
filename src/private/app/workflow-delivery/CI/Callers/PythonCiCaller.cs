using System.IO.Compression;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.CI.Callers;

internal sealed record PythonCiCallerContext(MaterializedEndpoint Basis,
    MaterializedEndpoint Candidate, bool Full, string ScopePath, string Application, string
        ControlSources);
internal sealed record PythonCiCallerReadback(PythonMetadataMember[] Members, string Interpreter,
    PythonPytestConfiguration Configuration, PythonPytestTargetSet Targets,
        PythonPytestOperation Operation);
internal sealed record PythonCiAuxiliary(bool NativeNode, bool AzureBundle);

internal static class PythonCiCaller
{
    internal const string Scope = "python/native-paired-facts-v1";

    internal static async Task<PythonCiCallerContext> MaterializeAsync(string root,
        string scopePath, string transfer, string directory, CancellationToken token)
    {
        string application = CiCallerTransfer.Application(transfer);
        string archive = Path.Combine(transfer, "python-control.zip");
        if (!File.Exists(archive)) throw new
            InvalidDataException("Candidate Python helper archive is missing.");
        CiCallerEndpoints endpoints = await CiCallerTransfer.MaterializeAsync(root, scopePath,
            directory, false, token);
        string sources = CiCallerTransfer.Fresh(Path.Combine(directory, "control-source"));
        ZipFile.ExtractToDirectory(archive, sources);
        var context = new PythonCiCallerContext(endpoints.Basis, endpoints.Candidate,
            endpoints.Full,
            scopePath, application, Path.Combine(sources,
                "src/private/app/workflow-delivery/Native/Python"));
        CiCallerTransfer.Write(Path.Combine(directory, "context.json"), context,
            TransferJson.Default.PythonCiCallerContext);
        CiCallerTransfer.Output("basis_directory", context.Basis.Directory);
        CiCallerTransfer.Output("candidate_directory", context.Candidate.Directory);
        CiCallerTransfer.Output("basis_separate", (context.Basis !=
            context.Candidate).ToString().ToLowerInvariant());
        return context;
    }

    internal static async Task<PythonGroupReadback> PlanAsync(string directory, string scopePath,
        PythonQueryTools tools, CancellationToken token)
    {
        PythonCiCallerContext context = Context(directory);
        GitMaterialization candidate = await GitMaterialization.BindAsync(
            context.Candidate.Directory, context.Candidate.Reference, token);
        string version = (await candidate.ReadOptionalTextAsync(".python-version", token)
            ?? throw new
                InvalidDataException("Missing committed Python runtime selection.")).Trim();
        if (version.Length == 0) throw new InvalidDataException("Empty Python runtime selection.");
        var request = new PythonGroupPreparationRequest(context.Basis, context.Candidate,
            context.Full, scopePath, context.ControlSources,
            CiCallerTransfer.Fresh(Path.Combine(directory, "query-preparation")), tools,
            new("ubuntu-latest", "python-" + version, new() { ["python"] = version }), 900);
        CiCallerTransfer.Write(Path.Combine(directory, "preparation-request.json"), request,
            TransferJson.Default.PythonGroupPreparationRequest);
        PythonGroupPreparationResult preparation = await
            PythonGroupPreparation.PrepareAsync(request, token);
        CiCallerTransfer.Write(Path.Combine(directory, "preparation.json"), preparation,
            TransferJson.Default.PythonGroupPreparationResult);
        CiCallerTransfer.Write(Path.Combine(directory, "group-request.json"), preparation.Request,
            TransferJson.Default.PythonGroupRequest);
        PythonGroupReadback readback = await PythonGroupReader.ReadAsync(preparation.Request,
            token);
        CiCallerTransfer.Plan(readback.Plan, context.Basis.Reference,
            context.Candidate.Reference, Scope);
        CiCallerTransfer.Write(Path.Combine(directory, "readback.json"),
            new PythonCiCallerReadback(readback.Members, readback.Interpreter,
                readback.Configuration,
                readback.Targets, readback.Operation), TransferJson.Default.PythonCiCallerReadback);
        CiCallerTransfer.Write(Path.Combine(directory, "plan.json"), readback.Plan,
            TransferJson.Default.CiPlan);
        PythonCiAuxiliary auxiliary = Auxiliary(readback.Plan);
        CiCallerTransfer.Output("selected", (readback.Plan.Checks.Length !=
            0).ToString().ToLowerInvariant());
        CiCallerTransfer.Output("native_node", auxiliary.NativeNode.ToString().ToLowerInvariant());
        return readback;
    }

    internal static PythonCiAuxiliary Auxiliary(CiPlan plan)
    {
        string[] targets = plan.Checks.Where(item => item.Work.Key.Check == "python/pytest-v1")
            .Select(item => item.Work.Dimensions.TryGetValue("testPath", out string? path) ? path :
                throw new InvalidDataException("Selected pytest check has no target.")).ToArray();
        return new(targets.Contains("tests/eng/test_legacy_release_contract.py"),
            targets.Contains("src/private/app/azureauth-credprovider/python/tests"));
    }

    internal static async Task<CiOutcome> ExecuteAsync(string directory, string? miseData,
        string? mise, string? node, string? pwsh, CancellationToken token,
        Func<CiPlan, PythonGroupRunRequest, CancellationToken, Task<PythonGroupRunResult>>?
            execute = null)
    {
        PythonCiCallerContext context = Context(directory);
        PythonCiCallerReadback readback = CiCallerTransfer.Read(Path.Combine(directory,
            "readback.json"),
            TransferJson.Default.PythonCiCallerReadback);
        CiPlan plan = CiCallerTransfer.Read(Path.Combine(directory, "plan.json"),
            TransferJson.Default.CiPlan);
        CiCallerTransfer.Plan(plan, context.Basis.Reference, context.Candidate.Reference, Scope);
        PythonGroupPreparationResult preparation = CiCallerTransfer.Read(
            Path.Combine(directory, "preparation.json"),
                TransferJson.Default.PythonGroupPreparationResult);
        PythonGroupEndpoint candidate = preparation.Request.Candidate;
        if (candidate.Checkout != context.Candidate ||
            preparation.Request.Basis.Checkout != context.Basis || preparation.Request.Full !=
                context.Full)
            throw new InvalidDataException("Prepared Python candidate differs from execution.");
        PythonCiAuxiliary selected = Auxiliary(plan);
        var environment = new Dictionary<string, string?>(candidate.Environment,
            StringComparer.Ordinal);
        if (selected.AzureBundle)
        {
            Prepend(environment, pwsh);
            foreach (string name in new[] { "DOTNET_ROOT", "DOTNET_ROOT_X64", "DOTNET_ROOT_ARM64" })
                if (Environment.GetEnvironmentVariable(name) is { } value) environment[name] =
                    value;
        }
        if (selected.NativeNode)
        {
            if (miseData is null || !Directory.Exists(CiCallerTransfer.Absolute(miseData)))
                throw new InvalidDataException("Selected tests require prepared native mise data.");
            Prepend(environment, mise); Prepend(environment, node);
            environment["MISE_DATA_DIR"] = miseData;
            environment["MISE_TASK_RUN_AUTO_INSTALL"] = "false";
            environment["MISE_EXEC_AUTO_INSTALL"] = "0";
            await CiCallerTransfer.RunAsync(new(CiCallerTransfer.Absolute(mise!),
                context.Candidate.Directory,
                ["trust", Path.Combine(context.Candidate.Directory, "mise.toml")], 30,
                environment, InheritEnvironment: false), directory, "mise-trust", token);
        }
        environment["PYTHONSAFEPATH"] = null;
        PythonRepositoryRequest collection = candidate.Collection;
        var request = new PythonGroupRunRequest(new(context.Candidate.Directory,
            CiCallerTransfer.Fresh(Path.Combine(directory, "execution")), collection.Graph.Uv,
            readback.Interpreter, readback.Members, readback.Configuration, readback.Targets,
            readback.Operation, 1800, environment), collection.PackageHelper);
        CiCallerTransfer.Write(Path.Combine(directory, "execution-request.json"), request,
            TransferJson.Default.PythonGroupRunRequest);
        PythonGroupRunResult result = await (execute ?? PythonGroupExecution.RunAsync)(plan,
            request, token);
        CiCallerTransfer.Write(Path.Combine(directory, "result.json"), result,
            TransferJson.Default.PythonGroupRunResult);
        CiCallerTransfer.Write(Path.Combine(directory, "results.json"), result.Results,
            TransferJson.Default.CheckResultArray);
        if (result.Candidate != plan.Candidate)
            throw new InvalidDataException("Python execution returned a different candidate.");
        CiOutcome outcome = CiCallerTransfer.Outcome(plan, result.Outcome);
        CiCallerTransfer.Write(Path.Combine(directory, "outcome.json"), outcome,
            TransferJson.Default.CiOutcome);
        return outcome;
    }

    private static void Prepend(Dictionary<string, string?> environment, string? executable)
    {
        if (executable is null || !File.Exists(CiCallerTransfer.Absolute(executable)))
            throw new InvalidDataException("Selected auxiliary native executable is unavailable.");
        environment["PATH"] = string.Join(Path.PathSeparator, new[] {
            Path.GetDirectoryName(executable)! }
            .Concat((environment["PATH"] ??
                "").Split(Path.PathSeparator)).Distinct(StringComparer.Ordinal));
    }

    private static PythonCiCallerContext Context(string directory) => CiCallerTransfer.Read(
        Path.Combine(directory, "context.json"), TransferJson.Default.PythonCiCallerContext);
}
