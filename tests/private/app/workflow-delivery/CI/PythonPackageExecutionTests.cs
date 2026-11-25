using System.Security.Cryptography;
using System.Text.Json;
using WorkflowDelivery.CI;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;
using WorkflowDelivery.Tests.Repository;
using GitFixture = WorkflowDelivery.Tests.Repository.GitReaderTests.GitFixture;

namespace WorkflowDelivery.Tests.CI;

[TestClass]
public sealed class PythonPackageExecutionTests(TestContext context)
{
    private static readonly string[] DistributionKinds = ["python-wheel", "python-sdist"];
    private static readonly string[] ToolGroups = ["dev", "workflow-delivery-python"];
    private static GitFixture? shared;
    private static CiPlan? sharedPlan;

    [ClassInitialize]
    public static async Task InitializeAsync(TestContext classContext)
    {
        GitFixture repo = await GitFixture.CreateAsync(classContext.CancellationToken);
        try
        {
            sharedPlan = await Fixture.CommitAsync(repo);
            shared = repo;
        }
        catch { repo.Dispose(); throw; }
    }

    [ClassCleanup]
    public static void Cleanup() => shared?.Dispose();

    private static NativeTermination Termination(string statusName) => statusName switch
    {
        "Cancelled" => NativeTermination.Cancelled,
        "TimedOut" => NativeTermination.TimedOut,
        _ => NativeTermination.Exited,
    };

    internal static async Task WriteProductAsync(GitFixture repo)
    {
        await repo.SetAsync("product/pyproject.toml", "fixture native source");
        await repo.SetAsync(PythonPackageChecksTests.Unit.SourcePath, """
            schema: workflow-delivery/v3/release-unit
            release-unit: product
            builds:
              - id: distributions
                definition: python/distribution-set-v1
                entry-point: pyproject.toml
                outputs:
                  - id: wheel
                    role: primary-package
                    kind: python-wheel
                  - id: source
                    role: source-package
                    kind: python-sdist
            """);
    }

    internal static PlannedCheck[] PackageChecks() =>
        PythonPackageChecks.Expand(PythonPackageChecksTests.Unit, PythonPackageChecksTests.Members,
            new Dictionary<string, string> { ["product"] = "1.0.0" })
        .Select(check => new PlannedCheck(check, [PythonPackageChecks.Preset], [],
            [CheckOrigin.Preset])).ToArray();

    [TestMethod]
    public async Task CompleteRecipeRetainsOriginalsAndBothIndependentConsumers()
    {
        using var fixture = Fixture.Shared(shared!, sharedPlan!);
        fixture.Native.Respond = command => command.InheritEnvironment
            ? PackageNative.Failure("Inherited runner environment.") : null;
        PythonPackageRunResult result = await fixture.RunAsync(context.CancellationToken);

        Assert.IsTrue(ResultCollector.Collect(fixture.Plan, result.Results).Satisfied,
            string.Join("; ", result.Failures.Select(failure => failure.Error)));
        Assert.HasCount(4, result.Results);
        Assert.HasCount(2, result.Distributions);
        CollectionAssert.AreEquivalent(DistributionKinds,
            result.Distributions.Select(item => item.Output.Kind).ToArray());
        foreach (PythonOriginalDistribution original in result.Distributions)
        {
            Assert.AreEqual("product", original.Unit);
            Assert.AreEqual("distributions", original.Build);
            Assert.AreEqual(8L, original.Length);
            Assert.AreEqual(Convert.ToHexStringLower(SHA256.HashData("original"u8)),
                original.Sha256);
            Assert.AreEqual("original", await File.ReadAllTextAsync(original.Path,
                context.CancellationToken));
        }

        NativeCommand[] builds = result.Commands.Select(item => item.Command)
            .Where(command => command.Arguments[0] == "build").ToArray();
        NativeCommand sourceBuild = builds.Single(command =>
            !command.Arguments.Contains("--wheel"));
        NativeCommand gitFree = builds.Single(command => command.Arguments.Contains("--wheel"));
        Assert.Contains(result.Distributions.Single(
            item => item.Output.Kind == "python-sdist").Path, gitFree.Arguments);
        Assert.Contains("--offline", gitFree.Arguments);
        Assert.Contains("--no-create-gitignore", gitFree.Arguments);
        Assert.Contains("build-dependency-check", gitFree.Arguments);
        Assert.Contains("build-dependency-check", sourceBuild.Arguments);
        Assert.Contains("--no-build-isolation", sourceBuild.Arguments);
        Assert.IsNull(gitFree.Environment!["GIT_DIR"]);
        Assert.IsFalse(gitFree.Directory.StartsWith(fixture.Repo.Directory,
            StringComparison.Ordinal));

        string originalWheel = result.Distributions.Single(
            item => item.Output.Kind == "python-wheel").Path;
        string rebuiltWheel = Path.Combine(
            gitFree.Arguments[Array.IndexOf(gitFree.Arguments, "--out-dir") + 1], "rebuilt.whl");
        Assert.AreNotEqual(InstalledWith(PythonPackageChecks.Wheel, originalWheel),
            InstalledWith(PythonPackageChecks.Sdist, rebuiltWheel));

        PythonPackageCommand sourcePreparation = Assert.ContainsSingle(result.Commands.Where(item =>
            item.Check.Check == PythonPackageChecks.Build && item.Command.Arguments[0] == "sync"));
        CollectionAssert.AreEquivalent(ToolGroups,
            sourcePreparation.Command.Arguments.Zip(sourcePreparation.Command.Arguments.Skip(1))
                .Where(pair => pair.First == "--only-group")
                .Select(pair => pair.Second).ToArray());
        Assert.DoesNotContain("--no-editable", sourcePreparation.Command.Arguments);

        PythonPackageCommand conversion = result.Commands.Single(item =>
            item.Check.Check == PythonPackageChecks.Sdist && item.Command.Arguments[0] == "sync");
        Assert.Contains("--no-editable", conversion.Command.Arguments);
        Assert.Contains("--no-build-isolation", conversion.Command.Arguments);
        int conversionIndex = Array.IndexOf(result.Commands, conversion);
        Assert.IsLessThan(conversionIndex, Array.FindIndex(result.Commands, item =>
            item.Check.Check == PythonPackageChecks.Wheel &&
            item.Command.Arguments.Contains("consume")));
        Assert.IsLessThan(Array.FindIndex(result.Commands, item => item.Command == gitFree),
            conversionIndex);

        string InstalledWith(string check, string wheel)
        {
            string[] arguments = Assert.ContainsSingle(result.Commands.Where(item =>
                item.Check.Check == check && item.Command.Arguments[0] == "pip"))
                .Command.Arguments;
            Assert.AreEqual("install", arguments[1]);
            Assert.AreEqual(wheel, arguments[^1]);
            Assert.Contains("--offline", arguments);
            Assert.Contains("--no-cache", arguments);
            Assert.AreEqual("--python", arguments[2]);
            return arguments[3];
        }
    }

    [TestMethod]
    [DataRow("Failed")]
    [DataRow("Cancelled")]
    [DataRow("TimedOut")]
    public async Task FailedSdistReconciliationPreservesOriginalsAndCompletedWheel(
        string statusName)
    {
        using var fixture = Fixture.Shared(shared!, sharedPlan!);
        fixture.Native.Respond = command =>
            command.Arguments[0] == "sync" && command.Arguments.Contains("--no-editable")
                ? PackageNative.Failure("controlled failure", Termination(statusName)) : null;
        PythonPackageRunResult result = await fixture.RunAsync(context.CancellationToken);

        Assert.AreEqual(Enum.Parse<CheckStatus>(statusName), result.Results.Single(item =>
            item.Key.Check == PythonPackageChecks.Sdist).Status);
        Assert.IsTrue(result.Results.Where(item => item.Key.Check != PythonPackageChecks.Sdist)
            .All(item => item.Status == CheckStatus.Passed));
        Assert.HasCount(2, result.Distributions);
        Assert.Contains(PythonPackageChecks.Wheel,
            result.Commands.Where(item => item.Command.Arguments.Contains("consume"))
                .Select(item => item.Check.Check));
        Assert.IsEmpty(result.Commands.Where(item =>
            item.Check.Check == PythonPackageChecks.Sdist && item.Command.Arguments[0] != "sync"));
    }

    [TestMethod]
    [DataRow("original.whl")]
    [DataRow("original.tar.gz")]
    public async Task ChangedOriginalDuringSdistReconciliationStopsConsumer(string fileName)
    {
        using var fixture = Fixture.Shared(shared!, sharedPlan!);
        fixture.Native.Respond = command =>
        {
            if (command.Arguments[0] == "sync" && command.Arguments.Contains("--no-editable"))
                File.WriteAllText(Path.Combine(fixture.Scratch, "package-0", "archives", fileName),
                    "changed!");
            return null;
        };
        PythonPackageRunResult result = await fixture.RunAsync(context.CancellationToken);

        Assert.AreEqual(CheckStatus.Failed, result.Results.Single(item =>
            item.Key.Check == PythonPackageChecks.Sdist).Status);
        Assert.IsTrue(result.Results.Where(item => item.Key.Check != PythonPackageChecks.Sdist)
            .All(item => item.Status == CheckStatus.Passed));
        Assert.Contains("Original Python distribution bytes changed",
            Assert.ContainsSingle(result.Failures).Error);
    }

    [TestMethod]
    [DataRow("sync", "Failed")]
    [DataRow("sync", "Cancelled")]
    [DataRow("build", "TimedOut")]
    [DataRow("version", "Failed")]
    [DataRow("missing-output", "Failed")]
    public async Task FailedNativeBuildPreventsConsumers(string stage, string expectedStatus)
    {
        using var fixture = Fixture.Shared(shared!, sharedPlan!);
        if (stage == "missing-output")
            fixture.Native.AfterBuild = dir => File.Delete(Path.Combine(dir, "original.tar.gz"));
        else
        {
            NativeTermination termination = Termination(expectedStatus);
            fixture.Native.Respond = command => stage switch
            {
                "sync" when command.Arguments[0] == "sync" =>
                    PackageNative.Failure("controlled failure", termination),
                "build" when command.Arguments[0] == "build" =>
                    PackageNative.Failure("controlled failure", termination),
                "version" when command.Arguments.Contains("identity") =>
                    PackageNative.Success(
                        """{"name":"hcoona-release-smoke-python","version":"2.0"}"""),
                _ => null,
            };
        }
        PythonPackageRunResult result = await fixture.RunAsync(context.CancellationToken);

        Assert.AreEqual(Enum.Parse<CheckStatus>(expectedStatus), result.Results.Single(item =>
            item.Key.Check == PythonPackageChecks.Build).Status);
        Assert.IsTrue(result.Results.Where(item => item.Key.Check != PythonPackageChecks.Build)
            .All(item => item.Status == CheckStatus.Skipped));
        Assert.IsFalse(result.Commands.Any(item => item.Command.Arguments.Contains("consume")));
        if (stage == "sync")
        {
            Assert.Contains(" sync failed: ", result.Failures[0].Error);
            Assert.EndsWith("stderr: controlled failure", result.Failures[0].Error);
        }
    }

    [TestMethod]
    public async Task ExtraOrCorruptedArchiveStopsDownstreamConsumersWithoutErasingPeers()
    {
        using var extra = Fixture.Shared(shared!, sharedPlan!);
        extra.Native.AfterBuild = dir =>
        {
            if (File.Exists(Path.Combine(dir, "rebuilt.whl")))
                File.WriteAllText(Path.Combine(dir, "unexpected.txt"), "extra");
        };
        PythonPackageRunResult sdistExtra = await extra.RunAsync(context.CancellationToken);
        Assert.AreEqual(CheckStatus.Failed, sdistExtra.Results.Single(item =>
            item.Key.Check == PythonPackageChecks.Sdist).Status);
        Assert.AreEqual(CheckStatus.Passed, sdistExtra.Results.Single(item =>
            item.Key.Check == PythonPackageChecks.Wheel).Status);

        using var wheelFail = Fixture.Shared(shared!, sharedPlan!);
        wheelFail.Native.Respond = command =>
            command.Arguments.Contains("consume") &&
            command.Directory.Contains("python-wheel-consumer", StringComparison.Ordinal)
                ? PackageNative.Failure("wheel consumer failed") : null;
        PythonPackageRunResult independent = await wheelFail.RunAsync(context.CancellationToken);
        Assert.AreEqual(CheckStatus.Failed, independent.Results.Single(item =>
            item.Key.Check == PythonPackageChecks.Wheel).Status);
        Assert.AreEqual(CheckStatus.Passed, independent.Results.Single(item =>
            item.Key.Check == PythonPackageChecks.Sdist).Status);

        using var corrupt = Fixture.Shared(shared!, sharedPlan!);
        corrupt.Native.Respond = command =>
            command.Arguments.Contains("archive") ? PackageNative.Failure("bad archive") : null;
        PythonPackageRunResult blocked = await corrupt.RunAsync(context.CancellationToken);
        Assert.AreEqual(CheckStatus.Failed, blocked.Results.Single(item =>
            item.Key.Check == PythonPackageChecks.Contents).Status);
        AssertConsumersSkipped(blocked);
    }

    [TestMethod]
    public async Task OriginalArchiveByteChangeBlocksBothConsumers()
    {
        using var fixture = Fixture.Shared(shared!, sharedPlan!);
        fixture.Native.Respond = command =>
        {
            if (!command.Arguments.Contains("archive")) return null;
            File.WriteAllText(command.Arguments[3], "changed!");
            return PackageNative.Success();
        };

        PythonPackageRunResult result = await fixture.RunAsync(context.CancellationToken);

        Assert.IsTrue(Assert.ContainsSingle(result.Commands.Where(item =>
            item.Command.Arguments.Contains("archive"))).Result.Succeeded);
        Assert.AreEqual(CheckStatus.Passed, result.Results.Single(item =>
            item.Key.Check == PythonPackageChecks.Build).Status);
        Assert.AreEqual(CheckStatus.Failed, result.Results.Single(item =>
            item.Key.Check == PythonPackageChecks.Contents).Status);
        Assert.Contains("Original Python distribution bytes changed",
            Assert.ContainsSingle(result.Failures).Error);
        AssertConsumersSkipped(result);
    }

    [TestMethod]
    public async Task UnreportedSourceBuildFileBlocksAllConsumers()
    {
        using var fixture = Fixture.Shared(shared!, sharedPlan!);
        fixture.Native.AfterBuild = dir => File.WriteAllText(
            Path.Combine(dir, "unexpected.txt"), "extra");
        fixture.Native.IncludeOutput = path => Path.GetFileName(path) != "unexpected.txt";

        PythonPackageRunResult result = await fixture.RunAsync(context.CancellationToken);

        Assert.AreEqual(CheckStatus.Failed, result.Results.Single(item =>
            item.Key.Check == PythonPackageChecks.Build).Status);
        Assert.Contains("Incomplete original Python output set",
            Assert.ContainsSingle(result.Failures).Error);
        Assert.IsEmpty(result.Distributions);
        AssertConsumersSkipped(result);
    }

    private static void AssertConsumersSkipped(PythonPackageRunResult result)
    {
        foreach (string check in new[] { PythonPackageChecks.Wheel, PythonPackageChecks.Sdist })
            Assert.AreEqual(CheckStatus.Skipped,
                result.Results.Single(item => item.Key.Check == check).Status);
        Assert.IsFalse(result.Commands.Any(item => item.Command.Arguments.Contains("consume")));
    }

    [TestMethod]
    public async Task InvalidSelectedContractOrDirtyCheckoutStopsBeforeNativeEffects()
    {
        using var sharedFixture = Fixture.Shared(shared!, sharedPlan!);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            sharedFixture.RunAsync(context.CancellationToken,
                sharedFixture.Plan with { Checks = sharedFixture.Plan.Checks[..^1] }));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            sharedFixture.RunAsync(context.CancellationToken,
                sharedFixture.Plan with { Candidate = "HEAD", Checks = [] }));
        Assert.IsEmpty(sharedFixture.Native.Commands);

        using var dirty = await Fixture.CreateAsync(context.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(dirty.Repo.Directory, "product/pyproject.toml"),
            "dirty", context.CancellationToken);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            dirty.RunAsync(context.CancellationToken));
        Assert.IsEmpty(dirty.Native.Commands);
    }

    private sealed class Fixture(GitFixture repo, CiPlan plan, bool ownsRepository = true)
        : IDisposable
    {
        internal GitFixture Repo { get; } = repo;
        internal CiPlan Plan { get; } = plan;
        internal string Scratch { get; } = Directory.CreateTempSubdirectory(
            "python-package-").FullName;
        internal PackageNative Native { get; } = new();

        internal static async Task<Fixture> CreateAsync(CancellationToken token)
        {
            GitFixture repo = await GitFixture.CreateAsync(token);
            try { return new(repo, await CommitAsync(repo)); }
            catch { repo.Dispose(); throw; }
        }

        internal static Fixture Shared(GitFixture repo, CiPlan plan) =>
            new(repo, plan, ownsRepository: false);

        internal static async Task<CiPlan> CommitAsync(GitFixture repo)
        {
            await WriteProductAsync(repo);
            string candidate = await repo.CommitAsync();
            await repo.GitAsync("reset", "--hard", candidate);
            return new(candidate, candidate, PythonFactsAssembler.Scope, PackageChecks());
        }

        internal Task<PythonPackageRunResult> RunAsync(CancellationToken token,
            CiPlan? plan = null) =>
            PythonPackageExecution.RunAsync(plan ?? Plan,
                new(Repo.Directory, Scratch, "controlled-uv", Path.Combine(Scratch, "python"),
                    Path.Combine(Scratch, "helper.py"), PythonPackageChecksTests.Members, 30,
                    new Dictionary<string, string?> { ["GIT_DIR"] = "excluded-from-consumer" }),
                Native.ExecuteAsync, token);

        public void Dispose()
        {
            if (ownsRepository) Repo.Dispose();
            Directory.Delete(Scratch, recursive: true);
        }
    }

    internal sealed class PackageNative
    {
        internal List<NativeCommand> Commands { get; } = [];
        internal Func<NativeCommand, NativeCommandResult?>? Respond { get; set; }
        internal Action<string>? AfterBuild { get; set; }
        internal Func<string, bool>? IncludeOutput { get; set; }

        internal static NativeCommandResult Success(string stdout = "{}") =>
            new(NativeTermination.Exited, 0, stdout, "", 0.1, null);

        internal static NativeCommandResult Failure(string stderr,
            NativeTermination termination = NativeTermination.Exited) =>
            new(termination, 1, "", stderr, 0.1, null);

        internal async Task<NativeCommandResult> ExecuteAsync(NativeCommand command,
            CancellationToken token)
        {
            Commands.Add(command);
            if (Respond?.Invoke(command) is { } response) return response;
            string[] arguments = command.Arguments;
            if (arguments[0] == "build")
            {
                string destination = arguments[Array.IndexOf(arguments, "--out-dir") + 1];
                if (!arguments.Contains("--no-create-gitignore"))
                    await File.WriteAllTextAsync(Path.Combine(destination, ".gitignore"), "*",
                        token);
                if (arguments.Contains("--wheel"))
                    await File.WriteAllTextAsync(Path.Combine(destination, "rebuilt.whl"),
                        "rebuilt", token);
                else
                {
                    await File.WriteAllTextAsync(Path.Combine(destination, "original.whl"),
                        "original", token);
                    await File.WriteAllTextAsync(Path.Combine(destination, "original.tar.gz"),
                        "original", token);
                }
                AfterBuild?.Invoke(destination);
            }
            if (arguments.Contains("identity"))
                return Success("""{"name":"hcoona-release-smoke-python","version":"1.0.0"}""");
            if (arguments.Contains("outputs"))
            {
                string directory = arguments[3];
                return Success(JsonSerializer.Serialize(Directory.EnumerateFiles(directory)
                    .Where(path => IncludeOutput?.Invoke(path) != false).Select(path => new
                    {
                        kind = path.EndsWith(".whl", StringComparison.Ordinal)
                            ? "python-wheel" : "python-sdist",
                        path,
                        length = new FileInfo(path).Length,
                        sha256 = Convert.ToHexStringLower(SHA256.HashData(
                            File.ReadAllBytes(path))),
                    })));
            }
            return Success();
        }
    }
}
