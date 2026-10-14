using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Release;
using GitFixture = WorkflowDelivery.Tests.Repository.GitReaderTests.GitFixture;

namespace WorkflowDelivery.Tests.Release;

[TestClass]
public sealed class PythonPreparationTests(TestContext context)
{
    private static readonly string[] DistributionKinds = ["python-wheel", "python-sdist"];

    // Request and native-answer rows read one committed repository; each row still owns
    // fresh scratch and runs the complete preparation.
    private static GitFixture? shared;
    private static string sharedRevision = "";

    [ClassInitialize]
    public static async Task InitializeAsync(TestContext classContext)
    {
        GitFixture repo = await GitFixture.CreateAsync(classContext.CancellationToken);
        try
        {
            sharedRevision = await Fixture.CommitAsync(repo, null);
            shared = repo;
        }
        catch { repo.Dispose(); throw; }
    }

    [ClassCleanup]
    public static void Cleanup() => shared?.Dispose();

    [TestMethod]
    public async Task CompletePairAndConsumersQualifyRelease()
    {
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);
        PythonPreparationResult result = await fixture.RunAsync(context.CancellationToken);
        Assert.IsEmpty(result.Errors);
        ReleaseCompletion.Validate(result.Prepared);
        Assert.HasCount(2, result.Prepared.Files);
        CollectionAssert.AreEquivalent(DistributionKinds,
            result.Prepared.Files.Select(file => file.Kind).ToArray());
        Assert.IsTrue(result.Prepared.Checks.All(check =>
            check.Status == ReleaseCheckStatus.Passed));
        foreach (ReleaseFile file in result.Prepared.Files)
        {
            Assert.AreEqual("hcoona-release-smoke-python", file.Name);
            Assert.AreEqual("1.0.0", file.Version);
            Assert.AreEqual(8L, file.Size);
            Assert.AreEqual(Convert.ToHexStringLower(SHA256.HashData("original"u8)), file.Sha256);
            Assert.IsFalse(Path.IsPathFullyQualified(file.Path));
        }
        foreach (ReleaseCommand command in result.Commands)
        {
            Assert.IsFalse(command.Command.InheritEnvironment);
            var environment = command.Command.Environment!;
            Assert.AreEqual(result.Prepared.Subject.SelectedRef, environment["GITHUB_REF"]);
            Assert.AreEqual(result.Prepared.Subject.Revision, environment["GITHUB_SHA"]);
            Assert.StartsWith(fixture.Scratch, environment["HOME"]!);
            Assert.IsFalse(environment.Keys.Any(key => key is "GITHUB_TOKEN" or "GH_TOKEN" or
                "ACTIONS_ID_TOKEN_REQUEST_TOKEN" or "ACTIONS_ID_TOKEN_REQUEST_URL"));
        }
        Assert.HasCount(2, result.Commands.Where(command =>
            command.Command.Arguments.Contains("consume")));
        ReleaseCommand conversion = Assert.ContainsSingle(result.Commands.Where(command =>
            command.Command.Arguments.Contains("--wheel")));
        Assert.AreEqual("sdist-consumer", conversion.Check);
        Assert.Contains("--offline", conversion.Command.Arguments);
        Assert.Contains("--no-build-isolation", conversion.Command.Arguments);
        Assert.Contains("build-dependency-check", conversion.Command.Arguments);
        Assert.AreEqual(fixture.Subject.Revision, result.Native["commit"]);
        Assert.IsFalse(result.Native.Values.Any(value => value.Contains(fixture.Repo.Directory,
            StringComparison.Ordinal)));
    }

    [TestMethod]
    [DataRow("buddy", "testpypi", "refs/heads/development", false, true)]
    [DataRow("official", "pypi", "refs/heads/main", false, true)]
    [DataRow("official", "pypi", "refs/heads/development", true, true)]
    [DataRow("buddy", "testpypi", "refs/tags/preview", true, true)]
    [DataRow("official", "pypi", "refs/heads/development", false, false)]
    [DataRow("buddy", "testpypi", "refs/tags/preview", false, false)]
    [DataRow("buddy", "pypi", "refs/heads/main", false, false)]
    [DataRow("official", "testpypi", "refs/heads/main", true, false)]
    [DataRow("buddy", "testpypi", "refs/heads/", false, false)]
    [DataRow("official", "pypi", "", true, false)]
    public async Task ChannelRefAndDryRunRemainIndependent(string channel, string destination,
        string selectedRef, bool dryRun, bool accepted)
    {
        using var fixture = Fixture.Shared(shared!, sharedRevision);
        fixture.Subject = fixture.Subject with { Channel = channel, Destination = destination,
            SelectedRef = selectedRef, DryRun = dryRun };
        PythonPreparationResult result = await fixture.RunAsync(context.CancellationToken);
        Assert.AreEqual(accepted ? ReleaseCheckStatus.Passed : ReleaseCheckStatus.Failed,
            result.Prepared.Checks[0].Status);
        Assert.AreEqual(accepted ? 2 : 0, result.Prepared.Files.Length);
        if (!accepted) Assert.IsEmpty(result.Commands);
        Assert.AreEqual(fixture.Subject, result.Prepared.Subject);
    }

    [TestMethod]
    [DataRow("version", "2.0.0")]
    [DataRow("commit", "0000000000000000000000000000000000000000")]
    [DataRow("implementation", "pypy")]
    [DataRow("interpreter_version", "3.14.2")]
    [DataRow("dependencies", "runtime")]
    [DataRow("local_version", "g1234567")]
    [DataRow("public_release", "false")]
    public async Task NativeFactsCannotQualifyAnotherProduct(string field, string value)
    {
        using var fixture = Fixture.Shared(shared!, sharedRevision);
        fixture.Subject = fixture.Subject with { Channel = "official", Destination = "pypi",
            SelectedRef = "refs/heads/main" };
        fixture.ChangeFacts = facts => facts[field] = field switch
        {
            "public_release" => JsonValue.Create(false),
            "dependencies" => new JsonArray(value),
            _ => JsonValue.Create(value),
        };
        PythonPreparationResult result = await fixture.RunAsync(context.CancellationToken);
        Assert.AreEqual(ReleaseCheckStatus.Failed, result.Prepared.Checks[0].Status);
        Assert.IsEmpty(result.Prepared.Files);
        Assert.DoesNotContain("build", result.Commands.Select(command => command.Check).ToArray());
        Assert.Contains("native Python Release identity", string.Join(";", result.Errors));
    }

    [TestMethod]
    public async Task DryRunRetainsNativeLocalVersionWithoutTransformingIt()
    {
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);
        fixture.Subject = fixture.Subject with { DryRun = true, SelectedRef = "refs/tags/preview" };
        fixture.Version = "1.0.0+g1234567";
        fixture.ChangeFacts = facts => { facts["local_version"] = "g1234567";
            facts["public_release"] = false; };
        PythonPreparationResult result = await fixture.RunAsync(context.CancellationToken);
        Assert.IsEmpty(result.Errors);
        Assert.HasCount(2, result.Prepared.Files);
        Assert.IsTrue(result.Prepared.Files.All(file => file.Version == fixture.Version));
        Assert.AreEqual(fixture.Version, result.Native["version"]);
    }

    [TestMethod]
    [DataRow("sync", "Exited", "Failed")]
    [DataRow("build", "Cancelled", "Cancelled")]
    [DataRow("archive", "TimedOut", "TimedOut")]
    [DataRow("consume", "Exited", "Failed")]
    public async Task FailedNativePhaseRetainsActualResultAndStops(string argument,
        string terminationText, string statusText)
    {
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);
        NativeTermination termination = Enum.Parse<NativeTermination>(terminationText);
        fixture.Override = command => command.Arguments.Contains(argument) ?
            new(termination, termination == NativeTermination.Exited ? 1 : null,
                "partial output", "native diagnostic", 0.5, null) : null;
        PythonPreparationResult result = await fixture.RunAsync(context.CancellationToken);
        ReleaseCommand failed = Assert.ContainsSingle(result.Commands.Where(command =>
            !command.Result.Succeeded));
        Assert.AreEqual(termination, failed.Result.Termination);
        Assert.AreEqual("partial output", failed.Result.Stdout);
        Assert.AreEqual("native diagnostic", failed.Result.Stderr);
        Assert.AreEqual(failed, result.Commands[^1]);
        Assert.AreEqual(Enum.Parse<ReleaseCheckStatus>(statusText),
            result.Prepared.Checks.Single(check => check.Id == failed.Check).Status);
        Assert.IsTrue(result.Prepared.Checks.SkipWhile(check => check.Id != failed.Check).Skip(1)
            .All(check => check.Status == ReleaseCheckStatus.Skipped));
        Assert.IsEmpty(result.Prepared.Files);
    }

    [TestMethod]
    [DataRow("quality")]
    [DataRow("outputs")]
    [DataRow("input")]
    public async Task UnsupportedCommittedRecipeStopsBeforeNativeWork(string defect)
    {
        using var fixture = await Fixture.CreateAsync(context.CancellationToken, defect);
        PythonPreparationResult result = await fixture.RunAsync(context.CancellationToken);
        Assert.AreEqual(ReleaseCheckStatus.Failed, result.Prepared.Checks[0].Status);
        Assert.IsEmpty(result.Commands);
        Assert.IsEmpty(result.Prepared.Files);
        Assert.IsNotEmpty(result.Errors);
    }

    [TestMethod]
    [DataRow("archive")]
    [DataRow("consume")]
    public async Task ChangedOriginalCannotQualifyRelease(string phase)
    {
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);
        fixture.Override = command =>
        {
            if (command.Arguments.Contains(phase))
                File.WriteAllText(Path.Combine(fixture.Scratch, "package", "archives",
                    "original.whl"), "changed!");
            return null;
        };
        PythonPreparationResult result = await fixture.RunAsync(context.CancellationToken);
        Assert.IsEmpty(result.Prepared.Files);
        Assert.Contains("bytes changed", string.Join(";", result.Errors));
        Assert.IsTrue(result.Commands.All(command => command.Result.Succeeded));
        Assert.Contains(ReleaseCheckStatus.Failed,
            result.Prepared.Checks.Select(check => check.Status).ToArray());
    }

    [TestMethod]
    public async Task SelectedToolMismatchStopsPreparation()
    {
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);
        fixture.Override = command => command.Arguments.Contains("ls") ?
            Fixture.Success("[]") : null;
        PythonPreparationResult result = await fixture.RunAsync(context.CancellationToken);
        Assert.AreEqual(ReleaseCheckStatus.Failed, result.Prepared.Checks[0].Status);
        Assert.IsEmpty(result.Prepared.Files);
        Assert.DoesNotContain("build", result.Commands.Select(command => command.Check).ToArray());
    }

    [TestMethod]
    public async Task CancellationBeforeWorkProducesCompleteChecks()
    {
        using var fixture = await Fixture.CreateAsync(context.CancellationToken);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        PythonPreparationResult result = await fixture.RunAsync(cancelled.Token);
        Assert.HasCount(6, result.Prepared.Checks);
        Assert.AreEqual(ReleaseCheckStatus.Cancelled, result.Prepared.Checks[0].Status);
        Assert.IsTrue(result.Prepared.Checks.Skip(1).All(check =>
            check.Status == ReleaseCheckStatus.Skipped));
        Assert.IsEmpty(result.Commands);
        Assert.IsEmpty(result.Prepared.Files);
    }

    private sealed class Fixture(GitFixture repo, ReleaseSubject subject,
        bool ownsRepository = true) : IDisposable
    {
        internal GitFixture Repo { get; } = repo;
        internal ReleaseSubject Subject { get; set; } = subject;
        internal string Scratch { get; } = Directory.CreateTempSubdirectory("python-release-")
            .FullName;
        internal string Version { get; set; } = "1.0.0";
        internal Action<JsonObject>? ChangeFacts { get; set; }
        internal Func<NativeCommand, NativeCommandResult?>? Override { get; set; }

        internal static async Task<Fixture> CreateAsync(CancellationToken token,
            string? defect = null)
        {
            GitFixture repo = await GitFixture.CreateAsync(token);
            try { return new(repo, SubjectFor(await CommitAsync(repo, defect))); }
            catch { repo.Dispose(); throw; }
        }

        // Reads a class-owned committed repository; the row owns only its scratch.
        internal static Fixture Shared(GitFixture repo, string revision) =>
            new(repo, SubjectFor(revision), ownsRepository: false);

        private const string Declaration = "product/workflow-delivery.release-unit.yml";

        private static ReleaseSubject SubjectFor(string revision) => new(revision,
            "refs/heads/development", "product", Declaration, "buddy", "testpypi", false);

        internal static async Task<string> CommitAsync(GitFixture repo, string? defect)
        {
            foreach (string input in new[] { "pyproject.toml", "uv.lock", "mise.toml",
                "mise.lock", "global.json", ".config/dotnet-tools.json",
                "src/private/app/workflow-delivery/Native/Python/packages.py",
                "product/pyproject.toml" })
                if (defect != "input" || input != "uv.lock") await repo.SetAsync(input);
            await repo.SetAsync("product/workflow-delivery.quality.yml", """
                schema: workflow-delivery/v3/quality-selection
                ecosystems:
                  python:
                    preset: python/hcoona-release-smoke-python-v1
                """.Replace("python/hcoona-release-smoke-python-v1", defect == "quality"
                    ? "python/unknown-v1" : "python/hcoona-release-smoke-python-v1",
                        StringComparison.Ordinal));
            await repo.SetAsync(Declaration, """
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
                """.Replace("python-sdist", defect == "outputs" ? "other-kind" :
                    "python-sdist", StringComparison.Ordinal));
            string revision = await repo.CommitAsync();
            await repo.GitAsync("reset", "--hard", revision);
            return revision;
        }

        internal Task<PythonPreparationResult> RunAsync(CancellationToken token) =>
            PythonPreparation.PrepareAsync(new(Subject, Repo.Directory, Scratch, Repo.Directory),
                ExecuteAsync, token);

        private async Task<NativeCommandResult> ExecuteAsync(NativeCommand command,
            CancellationToken token)
        {
            if (Override?.Invoke(command) is { } result) return result;
            string[] arguments = command.Arguments;
            if (arguments.Contains("--is-shallow-repository")) return Success("false");
            if (arguments.Contains("ls"))
            {
                string tool = arguments[^1], version = tool == "python" ? "3.14.3" : "0.12.24";
                return Success(JsonSerializer.Serialize(new[] { new { version,
                    requested_version = tool == "python" ? "3.14" : version, active = true,
                    source = new { type = "mise.toml", path = Path.Combine(Repo.Directory,
                        "mise.toml") } } }));
            }
            if (arguments.Contains("which")) return Success(Path.Combine(Scratch, arguments[^1]));
            if (arguments.Contains("--short")) return Success("0.12.24");
            if (arguments.Contains("identity")) return Success(JsonSerializer.Serialize(new
                { name = "hcoona-release-smoke-python", version = Version }));
            if (arguments.Contains("version-facts"))
            {
                var facts = new JsonObject { ["name"] = "hcoona-release-smoke-python",
                    ["version"] = Version, ["commit"] = Subject.Revision,
                    ["public_release"] = true, ["local_version"] = null,
                    ["dependencies"] = new JsonArray(), ["implementation"] = "cpython",
                    ["interpreter_version"] = "3.14.3" };
                ChangeFacts?.Invoke(facts);
                return Success(facts.ToJsonString());
            }
            if (arguments[0] == "build")
            {
                string directory = arguments[Array.IndexOf(arguments, "--out-dir") + 1];
                await File.WriteAllTextAsync(Path.Combine(directory, "original.whl"),
                    "original", token);
                if (!arguments.Contains("--wheel"))
                    await File.WriteAllTextAsync(Path.Combine(directory, "original.tar.gz"),
                        "original", token);
            }
            if (arguments.Contains("outputs"))
                return Success(JsonSerializer.Serialize(Directory.GetFiles(arguments[3])
                    .Select(path => new { kind = path.EndsWith(".whl", StringComparison.Ordinal)
                        ? "python-wheel" : "python-sdist", path,
                        length = new FileInfo(path).Length, sha256 = Convert.ToHexStringLower(
                            SHA256.HashData(File.ReadAllBytes(path))) })));
            return Success("{}");
        }

        internal static NativeCommandResult Success(string stdout) =>
            new(NativeTermination.Exited, 0, stdout, "", 0.1, null);

        public void Dispose()
        {
            if (ownsRepository) Repo.Dispose();
            Directory.Delete(Scratch, recursive: true);
        }
    }
}
