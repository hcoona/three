using System.Security;
using System.Text.Json;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.CI;

internal sealed record DotNetRunRequest(string Checkout, string Scratch, int DeadlineSeconds);
internal sealed record DotNetCommandObservation(CheckKey Key, NativeCommand Command,
    NativeCommandResult Result);
internal sealed record DotNetNativeOutput(CheckKey Key, string Path);
internal sealed record DotNetCheckFailure(CheckKey Key, string Error);
internal sealed record DotNetBatchCommandObservation(CheckKey[] Keys, NativeCommand Command,
    NativeCommandResult Result);
internal sealed record DotNetTestObservation(CheckKey Key, string Overall, string Build,
    string GetTargetPath, string Test, string Log);
internal sealed record DotNetRunResult(string Candidate, CheckResult[] Results,
    DotNetNativeOutput[] Outputs, DotNetCheckFailure[] Failures)
{
    public DotNetBatchCommandObservation? BatchBuild { get; init; }
    public DotNetBatchCommandObservation? TestBatch { get; init; }
    public DotNetTestObservation[] Tests { get; init; } = [];
}

// MSBuild owns the selected build/test batches and their scheduling; MTP/VSTest own tests.
internal static class DotNetExecution
{
    internal static Task<DotNetRunResult> RunAsync(CiPlan plan, DotNetRunRequest request,
        CancellationToken token) => RunAsync(plan, request, NativeProcess.ExecuteAsync, token);

    internal static Task<DotNetRunResult> RunAsync(CiPlan plan, DotNetRunRequest request,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        CancellationToken token) => RunAsync(plan, request, null, execute, token);

    // The group has already bound this exact checkout for the original plan.
    internal static Task<DotNetRunResult> RunBoundAsync(CiPlan plan, DotNetRunRequest request,
        GitMaterialization bound, CancellationToken token) =>
        RunAsync(plan, request, bound, NativeProcess.ExecuteAsync, token);

    private static async Task<DotNetRunResult> RunAsync(CiPlan plan, DotNetRunRequest request,
        GitMaterialization? bound,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        CancellationToken token)
    {
        string checkout = OwnedPath.Absolute(request.Checkout, PathMessage);
        string scratch = OwnedPath.Absolute(request.Scratch, PathMessage);
        if (!Directory.Exists(checkout) || !Directory.Exists(scratch) ||
            OwnedPath.Overlap(scratch, checkout) ||
            Directory.EnumerateFileSystemEntries(scratch).Any() || request.DeadlineSeconds <= 0)
            throw new InvalidDataException(
                "Native .NET execution requires fresh external scratch.");
        ResultCollector.Collect(plan, []);
        if (plan.Scope != DotNetFactsAssembler.Scope)
            throw new InvalidDataException("Unsupported native .NET execution scope.");
        Dictionary<CheckKey, DotNetGraphNode> nodes = ReadChecks(plan, checkout);
        if (!GitReader.IsCommitId(plan.Candidate))
            throw new InvalidDataException("Native .NET execution requires its exact candidate.");
        GitRevision revision = (await GitMaterialization.ReuseOrBindAsync(bound, checkout,
            plan.Candidate, token)).Revision;
        foreach (CheckKey key in nodes.Keys)
            if (!revision.Entries.Any(entry => entry.Path == key.Target &&
                    entry.ObjectType == "blob" && entry.Mode is "100644" or "100755"))
                throw new InvalidDataException("Selected native project is not committed source.");
        var results = new Dictionary<CheckKey, CheckResult>();
        var outputs = new List<DotNetNativeOutput>();
        var failures = new List<DotNetCheckFailure>();
        var observations = new List<DotNetTestObservation>();
        PlannedCheck[] builds = plan.Checks.Where(item =>
            item.Work.Key.Check == DotNetChecks.Build).ToArray();
        DotNetBatchCommandObservation? batch = null;
        if (builds.Length != 0) await BuildAsync();
        PlannedCheck[] tests = plan.Checks.Where(item =>
            item.Work.Key.Check == DotNetChecks.Test).ToArray();
        DotNetBatchCommandObservation? testBatch = null;
        if (tests.Length != 0) await TestAsync();
        return new(plan.Candidate, plan.Checks.Select(item => results[item.Work.Key]).ToArray(),
            outputs.ToArray(), failures.ToArray())
        { BatchBuild = batch, TestBatch = testBatch, Tests = observations.ToArray() };

        async Task BuildAsync()
        {
            CheckStatus fallback = CheckStatus.Cancelled;
            Dictionary<(string Project, string Globals), DotNetBuildEntryResult>? entries = null;
            string? error = null;
            try
            {
                token.ThrowIfCancellationRequested();
                string input = Path.Combine(scratch, "build-request.json");
                string response = Path.Combine(scratch, "build-response.json");
                string wrapper = Path.Combine(scratch, "build.proj");
                var nativeRequest = new DotNetBuildRequest(builds.Select(build =>
                    nodes[build.Work.Key].Identity).ToArray(), response);
                await File.WriteAllTextAsync(input, JsonSerializer.Serialize(nativeRequest,
                    TransferJson.Default.DotNetBuildRequest), token);
                string assembly = SecurityElement.Escape(
                    typeof(DotNetBuildTask).Assembly.Location)!;
                string requestFile = SecurityElement.Escape(input)!;
                await File.WriteAllTextAsync(wrapper, $"""
                    <Project>
                      <UsingTask TaskName="WorkflowDelivery.CI.DotNetBuildTask"
                        AssemblyFile="{assembly}" />
                      <Target Name="BuildSelected">
                        <WorkflowDelivery.CI.DotNetBuildTask RequestFile="{requestFile}" />
                      </Target>
                    </Project>
                    """, token);
                var command = new NativeCommand("dotnet", checkout,
                    ["msbuild", DotNetCli.DisableBuildServers, wrapper, "-nologo",
                        "-noAutoResponse", "-target:BuildSelected"],
                    request.DeadlineSeconds);
                NativeCommandResult native = await execute(command, token);
                batch = new(builds.Select(build => build.Work.Key).ToArray(), command, native);
                fallback = native.Termination switch
                {
                    NativeTermination.Cancelled => CheckStatus.Cancelled,
                    NativeTermination.TimedOut => CheckStatus.TimedOut,
                    _ => CheckStatus.Failed,
                };
                // A successful wrapper can contain independently failed native entries.
                if (native.Succeeded)
                {
                    await using var file = File.OpenRead(response);
                    DotNetBuildResponse projection = await JsonSerializer.DeserializeAsync(file,
                        TransferJson.Default.DotNetBuildResponse, token)
                        ?? throw new InvalidDataException("Missing native build response.");
                    if (projection.Entries is null)
                        throw new InvalidDataException("Missing native build entries.");
                    entries = new(new DotNetRepositoryReader.NativeIdentityComparer());
                    foreach (DotNetBuildEntryResult entry in projection.Entries)
                        if (entry is null || !entries.TryAdd(
                            DotNetRepositoryReader.Key(entry.Identity), entry))
                            throw new InvalidDataException("Ambiguous native build identity.");
                    if (entries.Count != builds.Length || builds.Any(build =>
                        !entries.ContainsKey(DotNetRepositoryReader.Key(
                            nodes[build.Work.Key].Identity))))
                        throw new InvalidDataException(
                            "Different selected native build identities.");
                }
                else error = native.Error ??
                    $"Native build {native.Termination}, exit {native.ExitCode}.";
            }
            catch (OperationCanceledException) { fallback = CheckStatus.Cancelled; }
            catch (Exception exception) when (exception is IOException or InvalidDataException or
                JsonException or UnauthorizedAccessException)
            {
                entries = null;
                fallback = CheckStatus.Failed;
                error = exception.Message;
            }
            foreach (PlannedCheck build in builds)
            {
                CheckKey key = build.Work.Key;
                CheckStatus status = fallback;
                string? failure = error;
                if (entries is not null)
                {
                    DotNetBuildEntryResult entry = entries[
                        DotNetRepositoryReader.Key(nodes[key].Identity)];
                    try
                    {
                        if (entry.Build != "Success" || entry.GetTargetPath != "Success" ||
                            entry.Outputs is null || entry.Outputs.Length != 1 ||
                            entry.Outputs[0] is not { } output ||
                            string.IsNullOrWhiteSpace(output.Identity))
                            throw new InvalidDataException(
                                "Missing or failed native Build/GetTargetPath.");
                        outputs.Add(new(key, RegularOutput(entry.Outputs[0].FullPath)));
                        status = CheckStatus.Passed;
                    }
                    catch (Exception exception) when (exception is IOException or
                        InvalidDataException or UnauthorizedAccessException)
                    {
                        status = CheckStatus.Failed;
                        failure = exception.Message;
                    }
                }
                results.Add(key, new(plan.Candidate, key, status));
                if (status != CheckStatus.Passed)
                    failures.Add(new(key, failure ?? "Native build did not complete."));
            }
        }

        // One native session runs every runnable test entry; each entry keeps its own
        // submission result, receipt and log, so one failure cannot mask another.
        async Task TestAsync()
        {
            var runnable = new List<PlannedCheck>();
            foreach (PlannedCheck test in tests)
            {
                CheckKey key = test.Work.Key;
                if (token.IsCancellationRequested)
                    results.Add(key, new(plan.Candidate, key, CheckStatus.Cancelled));
                else if (test.Work.Prerequisites.Any(item =>
                             results[item].Status != CheckStatus.Passed))
                    results.Add(key, new(plan.Candidate, key, CheckStatus.Skipped));
                else runnable.Add(test);
            }
            if (runnable.Count == 0) return;
            CheckStatus fallback = CheckStatus.Cancelled;
            string? error = null;
            bool receipted = false;
            string receipts = Path.Combine(scratch, "test-receipts");
            try
            {
                token.ThrowIfCancellationRequested();
                string input = Path.Combine(scratch, "test-request.json");
                string wrapper = Path.Combine(scratch, "test.proj");
                var nativeRequest = new DotNetTestRequest(runnable.Select(test =>
                {
                    DotNetGraphNode node = nodes[test.Work.Key];
                    return new DotNetTestEntry(node.Identity, TestTarget(node));
                }).ToArray(), receipts);
                await File.WriteAllTextAsync(input, JsonSerializer.Serialize(nativeRequest,
                    TransferJson.Default.DotNetTestRequest), token);
                string assembly = SecurityElement.Escape(
                    typeof(DotNetTestTask).Assembly.Location)!;
                string requestFile = SecurityElement.Escape(input)!;
                await File.WriteAllTextAsync(wrapper, $"""
                    <Project>
                      <UsingTask TaskName="WorkflowDelivery.CI.DotNetTestTask"
                        AssemblyFile="{assembly}" />
                      <Target Name="TestSelected">
                        <WorkflowDelivery.CI.DotNetTestTask RequestFile="{requestFile}" />
                      </Target>
                    </Project>
                    """, token);
                // As `dotnet test` does, terminal-logger mode with VSTest MSBuild output makes
                // VSTest report results as MSBuild events of its own submission. The plain
                // console logger still writes this command's captured output.
                var command = new NativeCommand("dotnet", checkout,
                    ["msbuild", DotNetCli.DisableBuildServers, wrapper, "-nologo",
                        "-noAutoResponse", "-tl:true", "-noConsoleLogger",
                        "-logger:ConsoleLogger,Microsoft.Build;Verbosity=minimal",
                        "-target:TestSelected"],
                    request.DeadlineSeconds, new Dictionary<string, string?>
                    { ["VsTestUseMSBuildOutput"] = "true" });
                NativeCommandResult native = await execute(command, token);
                testBatch = new(runnable.Select(test => test.Work.Key).ToArray(), command,
                    native);
                fallback = native.Termination switch
                {
                    NativeTermination.Cancelled => CheckStatus.Cancelled,
                    NativeTermination.TimedOut => CheckStatus.TimedOut,
                    _ => CheckStatus.Failed,
                };
                // An interruption keeps entries whose own submissions already completed, as
                // sequential commands did; any other failed wrapper cannot promote a receipt.
                receipted = native.Succeeded || native.Termination is
                    NativeTermination.Cancelled or NativeTermination.TimedOut;
                if (!native.Succeeded)
                    error = native.Error ??
                        $"Native test batch {native.Termination}, exit {native.ExitCode}.";
            }
            catch (OperationCanceledException) { fallback = CheckStatus.Cancelled; }
            catch (Exception exception) when (exception is IOException or JsonException or
                UnauthorizedAccessException)
            {
                fallback = CheckStatus.Failed;
                error = exception.Message;
            }
            for (int index = 0; index < runnable.Count; index++)
            {
                CheckKey key = runnable[index].Work.Key;
                DotNetGraphNode node = nodes[key];
                CheckStatus status = fallback;
                string? failure = error;
                // A receipt is written only after its own submission completed natively.
                string receipt = DotNetTestTask.Receipt(receipts, index);
                if (receipted && File.Exists(receipt))
                {
                    try
                    {
                        // A completed receipt is local evidence; the caller's cancellation
                        // must not erase it after the session has already stopped.
                        DotNetTestEntryResult entry = await ReadReceiptAsync(receipt);
                        if (entry.Log is null || entry.TestTarget != TestTarget(node) ||
                            !new DotNetRepositoryReader.NativeIdentityComparer().Equals(
                                DotNetRepositoryReader.Key(entry.Identity),
                                DotNetRepositoryReader.Key(node.Identity)))
                            throw new InvalidDataException(
                                "Different selected native test identity.");
                        observations.Add(new(key, entry.Overall, entry.Build,
                            entry.GetTargetPath, entry.Test, entry.Log));
                        if (entry.Overall != "Success" || entry.Build != "Success" ||
                            entry.GetTargetPath != "Success" || entry.Test != "Success")
                        {
                            status = CheckStatus.Failed;
                            failure = $"Native submission {entry.Overall}, Build {entry.Build}, " +
                                $"GetTargetPath " +
                                $"{entry.GetTargetPath}, {entry.TestTarget} {entry.Test}." +
                                (entry.Log.Length == 0 ? "" : Environment.NewLine + entry.Log);
                        }
                        else if (entry.Outputs is null || entry.Outputs.Length != 1 ||
                                 entry.Outputs[0] is not { } output ||
                                 string.IsNullOrWhiteSpace(output.Identity))
                            throw new InvalidDataException(
                                "Ambiguous or missing native GetTargetPath output.");
                        else
                        {
                            outputs.Add(new(key, RegularOutput(output.FullPath)));
                            status = CheckStatus.Passed;
                        }
                    }
                    catch (Exception exception) when (exception is IOException or
                        InvalidDataException or JsonException or UnauthorizedAccessException)
                    {
                        status = CheckStatus.Failed;
                        failure = exception.Message;
                    }
                }
                results.Add(key, new(plan.Candidate, key, status));
                if (status != CheckStatus.Passed)
                    failures.Add(new(key, failure ?? "Native test did not complete."));
            }
        }
    }

    private static string TestTarget(DotNetGraphNode node) =>
        node.TestCapability == "MTP" ? "InvokeTestingPlatform" : "VSTest";

    private static async Task<DotNetTestEntryResult> ReadReceiptAsync(string receipt)
    {
        await using var file = File.OpenRead(receipt);
        return await JsonSerializer.DeserializeAsync(file,
            TransferJson.Default.DotNetTestEntryResult)
            ?? throw new InvalidDataException("Missing native test receipt.");
    }

    internal static Dictionary<CheckKey, DotNetGraphNode> ReadChecks(CiPlan plan, string checkout)
    {
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
        return nodes;
    }

    private static string RegularOutput(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new InvalidDataException("Invalid native GetTargetPath coordinate.");
        path = Path.GetFullPath(path);
        if (!File.Exists(path))
            throw new InvalidDataException("Native GetTargetPath is missing or not a file.");
        return path;
    }

    private const string PathMessage = "Native .NET paths must be absolute.";
}
