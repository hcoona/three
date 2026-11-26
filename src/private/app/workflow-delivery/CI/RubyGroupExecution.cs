using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.CI;

internal sealed record RubyGroupRunRequest(string Checkout, string Scratch, RubyFactTools Facts,
    string Gem, string Bundle, string Nbgv, RubyRevisionInputs Candidate);
// Installs retain each selected suite's native bundle preparation, including successes.
internal sealed record RubyGroupRSpec(CheckKey Check, RubyRSpecRun Run,
    RubyPreparationCommand[] Installs);
internal sealed record RubyGroupRun(CheckResult[] Results, RubyGroupRSpec[] RSpec,
    RubyPackageRun Packages, CiOutcome Outcome);

// Two adopted recipes consume the original plan; native tools retain their own semantics.
internal static class RubyGroupExecution
{
    internal static Task<RubyGroupRun> RunAsync(CiPlan plan, RubyGroupRunRequest request,
        CancellationToken token) => RunAsync(plan, request, NativeProcess.ExecuteAsync, token);

    internal static async Task<RubyGroupRun> RunAsync(CiPlan plan, RubyGroupRunRequest request,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        CancellationToken token)
    {
        plan.Validate();
        if (plan.Scope != RubyFactsAssembler.Scope ||
            request.Candidate.Facts.Revision.Commit != plan.Candidate)
            throw new InvalidDataException("Ruby execution requires its original candidate facts.");
        (CiPlan suitePlan, CiPlan packagePlan) = plan.Partition(item =>
            item.Work.Key.Check == RubyProjectChecks.RSpec);
        const string pathMessage = "Ruby execution requires prepared native paths.";
        foreach (string path in new[] { request.Gem, request.Bundle, request.Nbgv,
                     request.Facts.Ruby, request.Facts.Helper })
            if (!Path.IsPathFullyQualified(path))
                throw new InvalidDataException(pathMessage);
        (string checkout, string scratch) = OwnedPath.RequireFreshExternalScratch(
            request.Checkout, request.Scratch, "Ruby execution requires fresh external scratch.",
            pathMessage);
        if (!GitReader.IsCommitId(plan.Candidate))
            throw new InvalidDataException("Ruby execution requires the exact original plan.");
        GitMaterialization bound = await GitMaterialization.BindAsync(checkout, plan.Candidate,
            token);
        var suites = new List<(PlannedCheck Check, RubyBundleFacts Bundle)>();
        foreach (PlannedCheck check in suitePlan.Checks)
        {
            if (!check.Origins.SequenceEqual([CheckOrigin.Preset]) ||
                check.QualityPresets is not [RubyProjectChecks.Preset or
                    RubyPackageChecks.RSpecPreset] ||
                check.Work.Key.Variant != "default" || check.Work.Package is not null ||
                check.Work.Dimensions.Count != 0 || check.Work.Prerequisites.Length != 0)
                throw new InvalidDataException("Unsupported adopted Ruby execution recipe.");
            string directory = Path.GetFullPath(Path.Combine(checkout, check.Work.Key.Target));
            RubyBundleFacts[] contexts = request.Candidate.Bundles.Where(bundle =>
                Path.GetDirectoryName(Path.GetFullPath(bundle.Gemfile)) == directory).ToArray();
            if (contexts.Length != 1)
                throw new InvalidDataException("Selected RSpec bundle is unresolved.");
            suites.Add((check, contexts[0]));
        }
        foreach (PlannedCheck check in packagePlan.Checks)
        {
            if (!check.Origins.SequenceEqual([CheckOrigin.Preset]) ||
                check.QualityPresets is not [RubyPackageChecks.Preset or
                    RubyPackageChecks.RSpecPreset] ||
                !RubyPackageChecks.Kinds.Contains(check.Work.Key.Check, StringComparer.Ordinal))
                throw new InvalidDataException("Unsupported adopted Ruby execution recipe.");
            PackageTarget subject = check.Work.Package ?? throw new InvalidDataException(
                "Ruby gem check lacks its original subject.");
            if (subject.Definition != RubyPackageChecks.Build ||
                subject.Outputs is not [{ Role: "primary-package", Kind: "ruby-gem" }] ||
                RubyPackageChecks.Kinds.Any(kind => !packagePlan.Checks.Any(other =>
                    other.Work.Key == (check.Work.Key with { Check = kind }) &&
                    ImpactPlanner.SamePackage(other.Work.Package, subject))))
                throw new InvalidDataException("Incomplete original Ruby gem obligations.");
        }
        var results = new List<CheckResult>();
        var runs = new List<RubyGroupRSpec>();
        foreach (var suite in suites)
        {
            CheckStatus status;
            if (token.IsCancellationRequested) status = CheckStatus.Cancelled;
            else
            {
                try
                {
                    // Planning reads only locks; a selected suite installs every frozen bundle
                    // it governs, including nested lock contexts its specs consume.
                    var installs = new List<RubyPreparationCommand>();
                    RubyRSpecRun run = await InstallAsync(suite.Check.Work.Key.Target,
                        installs) ??
                        await RubyRSpecExecution.RunAsync(suite.Check.Work.Key,
                        new(checkout, request.Facts.Ruby, request.Bundle,
                            suite.Bundle.BundlerVersion, suite.Bundle.Gemfile, scratch,
                            request.Facts.DeadlineSeconds, request.Facts.Environment), execute,
                        token);
                    runs.Add(new(suite.Check.Work.Key, run, installs.ToArray()));
                    status = run.Status;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                { status = CheckStatus.Cancelled; }
            }
            results.Add(new(plan.Candidate, suite.Check.Work.Key, status));
        }
        async Task<RubyRSpecRun?> InstallAsync(string target,
            List<RubyPreparationCommand> installs)
        {
            string[] native = request.Candidate.Facts.Native.Graph.Projects
                .Select(project => project.Directory).ToArray();
            foreach (RubyBundleFacts bundle in request.Candidate.Bundles.Where(bundle =>
                         RubyFactsAssembler.Governs(target, Path.GetRelativePath(checkout,
                             Path.GetDirectoryName(Path.GetFullPath(bundle.Gemfile))!)
                             .Replace('\\', '/'), native))
                         .OrderBy(bundle => bundle.Gemfile, StringComparer.Ordinal))
                if (await InstallBundleAsync(bundle, installs) is { } failed) return failed;
            return null;
        }
        async Task<RubyRSpecRun?> InstallBundleAsync(RubyBundleFacts bundle,
            List<RubyPreparationCommand> installs)
        {
            var environment = new Dictionary<string, string?>(request.Facts.Environment,
                StringComparer.Ordinal)
            {
                ["BUNDLE_GEMFILE"] = bundle.Gemfile,
                ["BUNDLE_FROZEN"] = "1",
            };
            var command = new NativeCommand(request.Facts.Ruby,
                Path.GetDirectoryName(bundle.Gemfile)!,
                [request.Bundle, "_" + bundle.BundlerVersion + "_", "install"],
                request.Facts.DeadlineSeconds, environment, InheritEnvironment: false);
            NativeCommandResult result = await execute(command, token);
            installs.Add(new(command, result));
            if (token.IsCancellationRequested)
                return new(command, result, null, CheckStatus.Cancelled,
                    "Native Ruby bundle installation was cancelled.");
            return result.Succeeded && result.Error is null ? null : new(command, result, null,
                CheckStatus.Failed, "Native Ruby bundle installation failed: " +
                NativeDiagnostic.Describe(result));
        }
        RubyPackageRun gem;
        if (packagePlan.Checks.Length == 0) gem = new([], [], [], []);
        else if (token.IsCancellationRequested) gem = new(packagePlan.Checks.Select(check =>
            new CheckResult(plan.Candidate, check.Work.Key, CheckStatus.Cancelled)).ToArray(),
            [], [], []);
        else gem = await RubyPackageExecution.RunAsync(packagePlan,
            new(checkout, Directory.CreateDirectory(Path.Combine(scratch, "packages")).FullName,
                request.Facts, request.Gem, request.Nbgv), bound, execute, token);
        results.AddRange(gem.Results);
        CheckResult[] observed = results.ToArray();
        return new(observed, runs.ToArray(), gem, ResultCollector.CollectValidated(plan, observed));
    }
}
