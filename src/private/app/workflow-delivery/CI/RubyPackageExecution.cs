using System.Security.Cryptography;
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

    internal static async Task<RubyPackageRun> RunAsync(CiPlan plan, RubyPackageRequest request,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        CancellationToken token)
    {
        ResultCollector.Collect(plan, []);
        foreach (string path in new[] { request.Checkout, request.Scratch, request.Gem,
                     request.Nbgv })
            if (!Path.IsPathFullyQualified(path))
                throw new InvalidDataException(
                    "Ruby gem execution requires prepared native paths.");
        if (plan.Scope != RubyFactsAssembler.Scope || !Directory.Exists(request.Scratch) ||
            Directory.EnumerateFileSystemEntries(request.Scratch).Any())
            throw new InvalidDataException("Ruby gem execution requires fresh external scratch.");
        string checkoutRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.Checkout));
        string scratchRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.Scratch));
        if (scratchRoot == checkoutRoot || scratchRoot.StartsWith(
                checkoutRoot + Path.DirectorySeparatorChar, PathComparison))
            throw new InvalidDataException("Gem consumer scratch must be outside the checkout.");
        GitRevision revision = await new GitReader(checkoutRoot).ReadAsync(plan.Candidate, token);
        GitMaterialization checkout = await GitMaterialization.BindAsync(checkoutRoot, revision,
            token);
        if (revision.Commit != plan.Candidate)
            throw new InvalidDataException("Ruby gem plan requires its complete native candidate.");
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
        var results = new Dictionary<CheckKey, CheckResult>();
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
            string archive = Path.Combine(state, "original.gem");
            RubyOriginalGem? original = null;
            foreach (string kind in RubyPackageChecks.Kinds)
            {
                CheckSpec check = checks.Single(item =>
                    item.Key == (build.Key with { Check = kind }));
                CheckStatus status;
                if (token.IsCancellationRequested) status = CheckStatus.Cancelled;
                else if (check.Prerequisites.Any(key =>
                             !results.TryGetValue(key, out var previous) ||
                             previous.Status != CheckStatus.Passed)) status = CheckStatus.Skipped;
                else try
                    {
                        if (kind == RubyPackageChecks.Build)
                        {
                            RubySourceGemFacts source = await RubySourceCollector.ReadSourceAsync(
                                checkout, subject.EntryPoint, request.Facts, request.Nbgv,
                                ExecuteAsync, token);
                            if (source.Gem.Name != "hcoona-release-smoke-ruby" ||
                                source.Gem.Version != subject.ExpectedVersion ||
                                source.Gem.Platform != "ruby")
                                throw new InvalidDataException(
                                    "Native gem source identity differs.");
                            await RunAsync(new(request.Facts.Ruby,
                                Path.Combine(checkoutRoot, subject.Directory),
                                [request.Gem, "build",
                                    Path.Combine(checkoutRoot, subject.EntryPoint),
                                    "--output", archive], request.Facts.DeadlineSeconds,
                                request.Facts.Environment, InheritEnvironment: false));
                            if (!File.Exists(archive))
                                throw new InvalidDataException(
                                    "Native gem build omitted its output.");
                            original = new(subject, archive, new FileInfo(archive).Length,
                                Convert.ToHexStringLower(SHA256.HashData(
                                    await File.ReadAllBytesAsync(
                                    archive, token))));
                            await InspectAsync(original);
                            gems.Add(original);
                        }
                        else if (kind == RubyPackageChecks.Contents)
                            await InspectAsync(original!);
                        else
                        {
                            RequireOriginal(original!);
                            string consumer = Directory.CreateDirectory(Path.Combine(state,
                                "consumer")).FullName;
                            var environment = new Dictionary<string, string?>(
                                request.Facts.Environment,
                                StringComparer.Ordinal)
                            {
                                ["GEM_HOME"] = Path.Combine(consumer, "gems"),
                                ["GEM_PATH"] = Path.Combine(consumer, "gems"),
                                ["RUBYLIB"] = null,
                                ["RUBYOPT"] = null,
                            };
                            await RunAsync(new(request.Facts.Ruby, consumer,
                                [request.Gem, "install", archive, "--local", "--no-document"],
                                request.Facts.DeadlineSeconds, environment,
                                InheritEnvironment: false));
                            await RunAsync(new(request.Facts.Ruby, consumer,
                                ["-e", "require 'hcoona_release_smoke_ruby'; " +
                                    "abort 'wrong installed identity' unless " +
                                    "HcoonaReleaseSmokeRuby.project_id == " +
                                    "'hcoona-release-smoke-ruby' " +
                                    "&& HcoonaReleaseSmokeRuby::VERSION == ARGV.fetch(0)",
                                    subject.ExpectedVersion], request.Facts.DeadlineSeconds,
                                environment, InheritEnvironment: false));
                            RequireOriginal(original!);
                        }
                        status = CheckStatus.Passed;
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        status = CheckStatus.Cancelled;
                    }
                    catch (Exception error) when (error is IOException or
                        InvalidDataException or InvalidOperationException)
                    {
                        failures.Add(new(check.Key, error.Message));
                        status = CheckStatus.Failed;
                    }
                results.Add(check.Key, new(plan.Candidate, check.Key, status));

                async Task RunAsync(NativeCommand command)
                {
                    NativeCommandResult result = await ExecuteAsync(command, token);
                    token.ThrowIfCancellationRequested();
                    if (!result.Succeeded || result.Error is not null)
                        throw new IOException("Native gem command failed: " +
                            $"{result.Termination}, exit {result.ExitCode}. " +
                            $"{result.Error} {result.Stderr}");
                }

                async Task<NativeCommandResult> ExecuteAsync(NativeCommand command,
                    CancellationToken cancellation)
                {
                    NativeCommandResult result = await execute(command, cancellation);
                    commands.Add(new(check.Key, command, result));
                    return result;
                }

                async Task InspectAsync(RubyOriginalGem value)
                {
                    RequireOriginal(value);
                    var collector = new RubyNativeFactCollector(request.Facts, ExecuteAsync);
                    RubyArchiveFacts facts = await collector.ArchiveAsync(value.Path, token);
                    string[] expected = ["LICENSE", "README.md",
                        "lib/hcoona_release_smoke_ruby.rb",
                        "lib/hcoona_release_smoke_ruby/version.rb"];
                    string[] actual = facts.Contents.Where(path => path !=
                        "lib/hcoona_release_smoke_ruby/_workflow_delivery_provenance.json")
                        .ToArray();
                    if (facts.Gem.Name != "hcoona-release-smoke-ruby" ||
                        facts.Gem.Version != subject.ExpectedVersion ||
                        facts.Gem.Platform != "ruby" ||
                        facts.Gem.RuntimeDependencies.Length != 0 ||
                        !actual.Order(StringComparer.Ordinal).SequenceEqual(
                            expected.Order(StringComparer.Ordinal)))
                        throw new InvalidDataException(
                            "Native gem archive differs from its subject.");
                    RequireOriginal(value);
                }
            }
        }
        return new(results.Values.ToArray(), commands.ToArray(), gems.ToArray(),
            failures.ToArray());
    }

    private static void RequireOriginal(RubyOriginalGem original)
    {
        if (!File.Exists(original.Path) || new FileInfo(original.Path).Length != original.Length ||
            Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(original.Path))) !=
            original.Sha256)
            throw new InvalidDataException("Original gem changed between required checks.");
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
