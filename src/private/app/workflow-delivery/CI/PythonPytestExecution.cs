using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.CI;

internal sealed record PythonPytestRunRequest(string Checkout, string Scratch, string Uv,
    string Interpreter, PythonMetadataMember[] Members, PythonPytestConfiguration Configuration,
    PythonPytestTargetSet Targets, PythonPytestOperation Operation, int DeadlineSeconds,
    IReadOnlyDictionary<string, string?> Environment);
internal sealed record PythonPytestCommand(CheckKey[] Checks, NativeCommand Command,
    NativeCommandResult Result);
internal sealed record PythonPytestFailure(CheckKey Check, string Error);
internal sealed record PythonPytestRunResult(string Candidate, CheckResult[] Results,
    PythonPytestCommand[] Commands, PythonPytestFailure[] Failures);

// Execute a finite original pytest partition; UV owns installation and pytest owns tests.
internal static class PythonPytestExecution
{
    internal static Task<PythonPytestRunResult> RunAsync(CiPlan plan,
        PythonPytestRunRequest request, CancellationToken token) =>
        RunAsync(plan, request, NativeProcess.ExecuteAsync, token);

    internal static async Task<PythonPytestRunResult> RunAsync(CiPlan plan,
        PythonPytestRunRequest request,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        CancellationToken token)
    {
        string checkout = Absolute(request.Checkout), scratch = Absolute(request.Scratch);
        _ = Absolute(request.Interpreter);
        StringComparison paths = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (request.DeadlineSeconds <= 0 || !Directory.Exists(checkout) ||
            !Directory.Exists(scratch) || Directory.EnumerateFileSystemEntries(scratch).Any() ||
            Linked(checkout) || Linked(scratch) || Within(scratch, checkout) ||
            Within(checkout, scratch))
            throw new InvalidDataException("Pytest execution requires fresh external scratch.");
        GitRevision revision = await new GitReader(checkout).ReadAsync(plan.Candidate, token);
        if (revision.Commit != plan.Candidate)
            throw new InvalidDataException("Pytest execution requires its exact candidate.");
        await GitMaterialization.BindAsync(checkout, revision, token);
        ReadChecks(plan, request, revision);
        if (plan.Checks.Length == 0) return new(plan.Candidate, [], [], []);

        var commands = new List<PythonPytestCommand>();
        var failures = new List<PythonPytestFailure>();
        var results = new List<CheckResult>();
        var environment = new Dictionary<string, string?>(request.Environment,
            StringComparer.Ordinal)
        {
            ["UV_PROJECT_ENVIRONMENT"] = Path.Combine(scratch, "test-environment"),
            ["UV_CACHE_DIR"] = Path.Combine(scratch, "uv-cache"),
            ["UV_PYTHON_DOWNLOADS"] = "never",
            ["PYTHONPATH"] = null,
            ["PYTHONHOME"] = null,
            ["VIRTUAL_ENV"] = null,
            ["PYTEST_ADDOPTS"] = null,
            ["PYTEST_PLUGINS"] = null,
            ["PYTEST_DISABLE_PLUGIN_AUTOLOAD"] = null,
        };
        string[] directories = plan.Checks.Select(item => request.Targets.Targets.Single(target =>
                item.Work.Key.Target == "pytest:" + target.Target).Member ?? ".")
            .Append(".").Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        string[] packages = directories.Select(directory => request.Members.Single(member =>
            member.Directory == directory).Name).ToArray();
        CheckKey[] keys = plan.Checks.Select(item => item.Work.Key).ToArray();
        NativeCommandResult preparation = await RunAsync(keys, ["sync", "--frozen",
            "--python", request.Interpreter, .. packages.SelectMany(name =>
                new[] { "--package", name })]);
        if (!preparation.Succeeded || preparation.Error is not null ||
            token.IsCancellationRequested)
        {
            foreach (CheckKey key in keys) Add(key, preparation);
            return Finish();
        }
        foreach (PlannedCheck item in plan.Checks)
        {
            if (token.IsCancellationRequested)
                results.Add(new(plan.Candidate, item.Work.Key, CheckStatus.Cancelled));
            else
                Add(item.Work.Key, await RunAsync([item.Work.Key], ["run", "--no-sync",
                    "python", "-m", "pytest", "-c", request.Configuration.ConfigurationFile,
                    item.Work.Dimensions["testPath"]]));
        }
        return Finish();

        async Task<NativeCommandResult> RunAsync(CheckKey[] checks, string[] arguments)
        {
            var command = new NativeCommand(request.Uv, checkout, arguments,
                request.DeadlineSeconds, environment);
            NativeCommandResult result;
            if (token.IsCancellationRequested)
                result = new(NativeTermination.Cancelled, null, "", "", 0,
                        "Command cancelled.");
            else try { result = await execute(command, token); }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    result = new(NativeTermination.Cancelled, null, "", "", 0,
                        "Command cancelled.");
                }
            commands.Add(new(checks, command, result));
            return result;
        }
        void Add(CheckKey key, NativeCommandResult result)
        {
            CheckStatus status = token.IsCancellationRequested ? CheckStatus.Cancelled :
                result.Termination switch
                {
                    NativeTermination.Cancelled => CheckStatus.Cancelled,
                    NativeTermination.TimedOut => CheckStatus.TimedOut,
                    _ => result.Succeeded && result.Error is null
                        ? CheckStatus.Passed : CheckStatus.Failed,
                };
            results.Add(new(plan.Candidate, key, status));
            if (status != CheckStatus.Passed)
                failures.Add(new(key, token.IsCancellationRequested ?
                    "Pytest execution cancelled." :
                    result.Error ??
                    $"Native pytest command {result.Termination}, exit {result.ExitCode}."));
        }
        PythonPytestRunResult Finish() => new(plan.Candidate, results.ToArray(),
            commands.ToArray(), failures.ToArray());

        bool Within(string child, string parent) => child.Equals(parent, paths) ||
            child.StartsWith(parent + Path.DirectorySeparatorChar, paths);
    }

    internal static void ReadChecks(CiPlan plan, PythonPytestRunRequest request,
        GitRevision revision)
    {
        ResultCollector.Collect(plan, []);
        if (plan.Scope != PythonFactsAssembler.Scope || plan.Candidate != revision.Commit ||
            request.Targets.Revision != plan.Candidate)
            throw new InvalidDataException("Conflicting native pytest candidate or scope.");
        if (request.Members.Any(member => member is null) ||
            request.Targets.Targets.Any(target => target is null))
            throw new InvalidDataException(
                "Native pytest membership and targets must not contain null entries.");
        if (request.Members.Count(member => member.Directory == ".") != 1 ||
            request.Members.Select(member => member.Name).Distinct(StringComparer.Ordinal).Count()
                != request.Members.Length ||
            request.Members.Select(member => member.Directory).Distinct(StringComparer.Ordinal)
                .Count() != request.Members.Length)
            throw new InvalidDataException("Ambiguous native pytest membership.");
        foreach (PythonMetadataMember member in request.Members)
            ImpactPlanner.RequireText(member.Name, "native Python member name");
        PythonPytestTargetSet bound = PythonPytestTargets.Bind(request.Configuration, revision,
            request.Members.Select(member => new PythonProjectDependencies(member.Directory,
                member.Name, [], [])).ToArray());
        if (bound.ConfigurationFile != request.Targets.ConfigurationFile ||
            bound.Targets.Length != request.Targets.Targets.Length ||
            bound.Targets.Zip(request.Targets.Targets).Any(pair =>
                pair.First.Target != pair.Second.Target ||
                pair.First.Member != pair.Second.Member ||
                !pair.First.Inputs.SequenceEqual(pair.Second.Inputs)))
            throw new InvalidDataException("Native pytest target transfer differs from candidate.");
        CheckSpec[] expected = PythonPytestChecks.Expand(request.Configuration, bound,
            request.Operation);
        foreach (PlannedCheck item in plan.Checks)
        {
            CheckSpec? check = expected.SingleOrDefault(check => check.Key == item.Work.Key);
            if (check is null || !item.Origins.SequenceEqual([CheckOrigin.NativeRetained]) ||
                item.QualityPresets.Length != 0 || item.Work.Package is not null ||
                item.Work.Runner != check.Runner || item.Work.Required != check.Required ||
                !item.Work.Prerequisites.SequenceEqual(check.Prerequisites) ||
                item.Work.Dimensions.Count != check.Dimensions.Count ||
                item.Work.Dimensions.Any(pair => !check.Dimensions.TryGetValue(pair.Key,
                    out string? value) || pair.Value != value))
                throw new InvalidDataException("Conflicting native pytest check contract.");
        }
    }

    private static string Absolute(string path) => Path.IsPathFullyQualified(path)
        ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(path))
        : throw new InvalidDataException("Pytest execution paths must be absolute.");
    private static bool Linked(string path)
    {
        for (DirectoryInfo? directory = new(path); directory is not null;
             directory = directory.Parent)
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) return true;
        return false;
    }
}
