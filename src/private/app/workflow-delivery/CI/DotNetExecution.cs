using System.Text.Json;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.CI;

internal sealed record DotNetRunRequest(string Checkout, string Scratch, int DeadlineSeconds);
internal sealed record DotNetCommandObservation(CheckKey Key, NativeCommand Command,
    NativeCommandResult Result);
internal sealed record DotNetNativeOutput(CheckKey Key, string Path);
internal sealed record DotNetCheckFailure(CheckKey Key, string Error);
internal sealed record DotNetRunResult(string Candidate, CheckResult[] Results,
    DotNetCommandObservation[] Commands, DotNetNativeOutput[] Outputs,
    DotNetCheckFailure[] Failures);

// Two native phases per retained identity. MSBuild owns references and target state;
// MTP/VSTest own launch and test verdicts. There is no application task scheduler.
internal static class DotNetExecution
{
    internal static Task<DotNetRunResult> RunAsync(CiPlan plan, DotNetRunRequest request,
        CancellationToken token) => RunAsync(plan, request, NativeProcess.ExecuteAsync, token);

    internal static async Task<DotNetRunResult> RunAsync(CiPlan plan, DotNetRunRequest request,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        CancellationToken token)
    {
        string checkout = Absolute(request.Checkout);
        string scratch = Absolute(request.Scratch);
        if (!Directory.Exists(checkout) || !Directory.Exists(scratch) ||
            Within(scratch, checkout) || Within(checkout, scratch) ||
            Directory.EnumerateFileSystemEntries(scratch).Any() || request.DeadlineSeconds <= 0)
            throw new InvalidDataException(
                "Native .NET execution requires fresh external scratch.");
        ResultCollector.Collect(plan, []);
        if (plan.Scope != DotNetFactsAssembler.Scope)
            throw new InvalidDataException("Unsupported native .NET execution scope.");
        var nodes = new Dictionary<CheckKey, DotNetGraphNode>();
        foreach (PlannedCheck item in plan.Checks)
        {
            if (!item.Origins.SequenceEqual([CheckOrigin.NativeRetained]) ||
                item.QualityPresets.Length != 0)
                throw new InvalidDataException("Unsupported selected .NET quality contract.");
            nodes.Add(item.Work.Key, DotNetChecks.Read(item.Work, checkout));
        }
        foreach (var identity in plan.Checks.GroupBy(item =>
                     (item.Work.Key.Target, item.Work.Key.Variant)))
        {
            PlannedCheck build = identity.SingleOrDefault(item =>
                item.Work.Key.Check == DotNetChecks.Build) ??
                throw new InvalidDataException("Missing native Build obligation.");
            CheckSpec[] complete = DotNetChecks.Expand(build.Work.Key.Target,
                nodes[build.Work.Key]);
            if (identity.Count() != complete.Length || complete.Any(check =>
                    !nodes.TryGetValue(check.Key, out DotNetGraphNode? node) ||
                    node.Dimension != nodes[build.Work.Key].Dimension ||
                    node.TestCapability != nodes[build.Work.Key].TestCapability))
                throw new InvalidDataException("Incomplete selected native identity obligations.");
        }
        GitRevision revision = await new GitReader(checkout).ReadAsync(plan.Candidate, token);
        if (revision.Commit != plan.Candidate)
            throw new InvalidDataException("Native .NET execution requires its exact candidate.");
        await GitMaterialization.BindAsync(checkout, revision, token);
        foreach (CheckKey key in nodes.Keys)
            if (!revision.Entries.Any(entry => entry.Path == key.Target &&
                    entry.ObjectType == "blob" && entry.Mode is "100644" or "100755"))
                throw new InvalidDataException("Selected native project is not committed source.");
        var results = new Dictionary<CheckKey, CheckResult>();
        var commands = new List<DotNetCommandObservation>();
        var outputs = new List<DotNetNativeOutput>();
        var failures = new List<DotNetCheckFailure>();
        int phase = 0;
        foreach (PlannedCheck build in plan.Checks.Where(item =>
                     item.Work.Key.Check == DotNetChecks.Build))
        {
            await CompleteAsync(build.Work);
            PlannedCheck? test = plan.Checks.SingleOrDefault(item =>
                item.Work.Key == (build.Work.Key with { Check = DotNetChecks.Test }));
            if (test is not null) await CompleteAsync(test.Work);
        }
        return new(plan.Candidate, plan.Checks.Select(item => results[item.Work.Key]).ToArray(),
            commands.ToArray(), outputs.ToArray(), failures.ToArray());

        async Task CompleteAsync(CheckSpec check)
        {
            CheckStatus status;
            if (token.IsCancellationRequested) status = CheckStatus.Cancelled;
            else if (check.Prerequisites.Any(key => results[key].Status != CheckStatus.Passed))
                status = CheckStatus.Skipped;
            else
            {
                try
                {
                    DotNetGraphNode node = nodes[check.Key];
                    string? testTarget = check.Key.Check == DotNetChecks.Build ? null :
                        node.TestCapability == "MTP" ? "InvokeTestingPlatform" : "VSTest";
                    string targets = "Build,GetTargetPath" +
                        (testTarget is null ? "" : "," + testTarget);
                    string queried = testTarget is null ? "Build,GetTargetPath" :
                        "GetTargetPath," + testTarget;
                    string response = Path.Combine(scratch, "phase-" + phase++ + ".json");
                    var command = new NativeCommand("dotnet", checkout,
                        ["msbuild", node.Identity.Project, "-nologo", "-noAutoResponse",
                            "-target:" + targets, "-getTargetResult:" + queried,
                            "-getResultOutputFile:" + response,
                            .. DotNetChecks.Properties(node.Identity.Globals)],
                        request.DeadlineSeconds);
                    NativeCommandResult native = await execute(command, token);
                    commands.Add(new(check.Key, command, native));
                    status = native.Termination switch
                    {
                        NativeTermination.Cancelled => CheckStatus.Cancelled,
                        NativeTermination.TimedOut => CheckStatus.TimedOut,
                        _ => native.Succeeded ? CheckStatus.Passed : CheckStatus.Failed,
                    };
                    if (status == CheckStatus.Passed)
                        outputs.Add(new(check.Key, await ReadOutputAsync(response,
                            testTarget ?? "Build", token)));
                    else failures.Add(new(check.Key, native.Error ??
                        $"Native command {native.Termination}, exit {native.ExitCode}."));
                }
                catch (OperationCanceledException) { status = CheckStatus.Cancelled; }
                catch (Exception exception) when (exception is IOException or
                    InvalidDataException or JsonException or UnauthorizedAccessException)
                {
                    failures.Add(new(check.Key, exception.Message));
                    status = CheckStatus.Failed;
                }
            }
            results.Add(check.Key, new(plan.Candidate, check.Key, status));
        }
    }

    private static async Task<string> ReadOutputAsync(string response, string obligation,
        CancellationToken token)
    {
        await using var file = File.OpenRead(response);
        using JsonDocument document = await JsonDocument.ParseAsync(file,
            cancellationToken: token);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("TargetResults", out JsonElement targets) ||
            targets.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Missing native target results.");
        JsonElement output = Required("GetTargetPath");
        Required(obligation);
        if (!output.TryGetProperty("Items", out JsonElement items) ||
            items.ValueKind != JsonValueKind.Array || items.GetArrayLength() != 1)
            throw new InvalidDataException("Ambiguous or missing native GetTargetPath output.");
        JsonElement item = items[0];
        if (item.ValueKind != JsonValueKind.Object ||
            !item.TryGetProperty("Identity", out JsonElement identity) ||
            identity.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(identity.GetString()) ||
            !item.TryGetProperty("FullPath", out JsonElement fullPath) ||
            fullPath.ValueKind != JsonValueKind.String ||
            !Path.IsPathFullyQualified(fullPath.GetString()!))
            throw new InvalidDataException("Invalid native GetTargetPath coordinate.");
        string path = Path.GetFullPath(fullPath.GetString()!);
        if (!File.Exists(path) ||
            (File.GetAttributes(path) &
                (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            throw new InvalidDataException("Native GetTargetPath is not a regular file.");
        return path;

        JsonElement Required(string target)
        {
            if (!targets.TryGetProperty(target, out JsonElement result) ||
                result.ValueKind != JsonValueKind.Object ||
                !result.TryGetProperty("Result", out JsonElement verdict) ||
                verdict.ValueKind != JsonValueKind.String || verdict.GetString() != "Success")
                throw new InvalidDataException("Missing or failed native target: " + target);
            return result;
        }
    }

    private static string Absolute(string path) => Path.IsPathFullyQualified(path)
        ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)) :
        throw new InvalidDataException("Native .NET paths must be absolute.");

    private static bool Within(string child, string parent) => child.Equals(parent,
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase :
            StringComparison.Ordinal) ||
        child.StartsWith(parent + Path.DirectorySeparatorChar, OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
