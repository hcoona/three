using WorkflowDelivery.Ecosystems;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.CI;

internal sealed record RubyPackageRequest(string Checkout, string Scratch, RubyFactTools Facts,
    string Gem, string Nbgv);
internal sealed record RubyPackageCommand(CheckKey Check, NativeCommand Command,
    NativeCommandResult Result);
internal sealed record RubyOriginalGem(PackageTarget Subject, string Path, long Length,
    string Sha256);
internal sealed record RubyPackageFailure(CheckKey Check, string Error);
internal sealed record RubyPackageRun(CheckResult[] Results, RubyPackageCommand[] Commands,
    RubyOriginalGem[] Gems, RubyPackageFailure[] Failures);

// Finite adopted gem orchestration. RubyGems owns build/archive/install semantics.
internal static class RubyPackageExecution
{
    internal static Task<RubyPackageRun> RunAsync(CiPlan plan, RubyPackageRequest request,
        CancellationToken token) => RunAsync(plan, request, NativeProcess.ExecuteAsync, token);

    internal static Task<RubyPackageRun> RunAsync(CiPlan plan, RubyPackageRequest request,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        CancellationToken token) => RunAsync(plan, request, null, execute, token);

    // The group may pass its binding of this exact checkout for the original plan.
    internal static async Task<RubyPackageRun> RunAsync(CiPlan plan, RubyPackageRequest request,
        GitMaterialization? bound,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        CancellationToken token)
    {
        if (bound is null) plan.Validate();
        const string pathMessage = "Ruby gem execution requires prepared native paths.";
        _ = OwnedPath.Absolute(request.Gem, pathMessage);
        _ = OwnedPath.Absolute(request.Nbgv, pathMessage);
        if (plan.Scope != RubyFactsAssembler.Scope)
            throw new InvalidDataException("Unsupported Ruby gem execution scope.");
        (string checkoutRoot, string scratchRoot) = OwnedPath.RequireFreshExternalScratch(
            request.Checkout, request.Scratch,
            "Ruby gem execution requires fresh external scratch.", pathMessage);
        if (!GitReader.IsCommitId(plan.Candidate))
            throw new InvalidDataException("Ruby gem plan requires its complete native candidate.");
        GitMaterialization checkout = await GitMaterialization.ReuseOrBindAsync(bound,
            checkoutRoot, plan.Candidate, token);
        CheckSpec[] checks = plan.Checks.Where(check => RubyPackageChecks.Kinds.Contains(
            check.Work.Key.Check, StringComparer.Ordinal)).Select(check => check.Work).ToArray();
        foreach (CheckSpec check in checks)
        {
            PackageTarget subject = check.Package ?? throw new InvalidDataException(
                "Ruby gem check lacks its native package subject.");
            if (subject.Definition != RubyPackageChecks.Build ||
                subject.Outputs is not [{ Role: "primary-package", Kind: "ruby-gem" }])
                throw new InvalidDataException("Unsupported Ruby gem package outputs.");
            if (RubyPackageChecks.Kinds.Any(kind => !checks.Any(other =>
                    other.Key == (check.Key with { Check = kind }) &&
                    ImpactPlanner.SamePackage(other.Package, subject))))
                throw new InvalidDataException("Incomplete adopted Ruby gem check set.");
        }
        var runner = new CheckExecutionRunner(plan, token);
        var commands = new List<RubyPackageCommand>();
        var gems = new List<RubyOriginalGem>();
        var failures = new List<RubyPackageFailure>();
        int ordinal = 0;
        foreach (CheckSpec build in checks.Where(check =>
                     check.Key.Check == RubyPackageChecks.Build))
        {
            PackageTarget subject = build.Package!;
            string state = Directory.CreateDirectory(Path.Combine(scratchRoot,
                "gem-" + ordinal++)).FullName;
            RubyReleaseUnit unit = ReleaseUnitCatalog.Ruby(subject.Unit);
            RubyPackageOperations operations = ReleaseUnitCatalog.RubyPackage(unit, request.Facts,
                request.Gem, request.Nbgv, state);
            RubyGemArchive? original = null;
            foreach (string kind in RubyPackageChecks.Kinds)
            {
                CheckSpec check = checks.Single(item =>
                    item.Key == (build.Key with { Check = kind }));
                await runner.RunAsync(check, async () =>
                {
                    try
                    {
                        if (kind == RubyPackageChecks.Build)
                        {
                            RubySourceGemFacts source = await RubySourceCollector.ReadSourceAsync(
                                checkout, subject.EntryPoint, request.Facts, request.Nbgv,
                                ExecuteAsync, token);
                            original = await operations.BuildAsync(checkout, subject.Directory,
                                subject.EntryPoint, subject.ExpectedVersion, source, ExecuteAsync,
                                token);
                            gems.Add(new(subject, original.Path, original.Length, original.Sha256));
                        }
                        else if (kind == RubyPackageChecks.Contents)
                            await operations.InspectAsync(checkout, subject.Directory, original!,
                                subject.ExpectedVersion, release: false, ExecuteAsync, token);
                        else
                            await operations.ConsumeAsync(
                                Path.GetFullPath(Path.Combine(checkoutRoot, subject.Directory)),
                                original!, subject.ExpectedVersion, release: false, ExecuteAsync,
                                token);
                        return CheckStatus.Passed;
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        return CheckStatus.Cancelled;
                    }
                    catch (Exception error) when (error is IOException or
                        InvalidDataException or InvalidOperationException)
                    {
                        failures.Add(new(check.Key, error.Message));
                        return CheckStatus.Failed;
                    }
                });

                async Task<NativeCommandResult> ExecuteAsync(NativeCommand command,
                    CancellationToken cancellation)
                {
                    NativeCommandResult result = await execute(command, cancellation);
                    commands.Add(new(check.Key, command, result));
                    return result;
                }
            }
        }
        return new(runner.InExecutionOrder(), commands.ToArray(), gems.ToArray(),
            failures.ToArray());
    }
}
