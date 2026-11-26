using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Destinations;
using WorkflowDelivery.Release;
using GitFixture = WorkflowDelivery.Tests.Repository.GitReaderTests.GitFixture;

namespace WorkflowDelivery.Tests.Release;

[TestClass]
public sealed class PythonPreparationTests(TestContext context)
{
    private static readonly string[] DistributionKinds = ["python-wheel", "python-sdist"];
    private static readonly string[] GitStatusArguments =
        ["--no-optional-locks", "status", "--porcelain", "--untracked-files=all"];

    // End-to-end rows read one committed repository; each row still owns fresh scratch and
    // runs the complete preparation, which writes only to scratch (the control row checks the
    // checkout stays clean). A committed recipe defect keeps a private repository. Pure rule
    // rows run on their entry points.
    private static GitFixture? shared;
    private static string sharedRevision = "";
    private const string Revision = "0123456789abcdef0123456789abcdef01234567";
    private const string Identity = "Unsupported native Python Release identity.";
    // Pure identity rows only need one interpreter answer; the root lock supplies it.
    private static readonly string Interpreter = RepositoryToolSelection.Locked("python");

    [ClassInitialize]
    public static async Task InitializeAsync(TestContext classContext)
    {
        GitFixture repo = await GitFixture.CreateAsync(classContext.CancellationToken);
        try
        {
            sharedRevision = await Fixture.CommitAsync(repo, includeNbgv: true);
            shared = repo;
        }
        catch { repo.Dispose(); throw; }
    }

    [ClassCleanup]
    public static void Cleanup() => shared?.Dispose();

    [TestMethod]
    public async Task CompletePairAndConsumersQualifyRelease()
    {
        using var fixture = Fixture.Shared(shared!, sharedRevision);
        ReleasePreparationResult result = await fixture.RunAsync(context.CancellationToken);
        Assert.AreEqual("", await NativeProcess.RunAsync("git", fixture.Repo.Directory,
            GitStatusArguments, context.CancellationToken),
            "Preparation must leave the shared checkout clean.");
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
    [DataRow("official", "testpypi", "refs/heads/main")]
    [DataRow("official", "pypi", "")]
    public void DryRunStillRequiresTheAdmittedDestinationAndANamedRef(string channel,
        string destination, string selectedRef)
    {
        ReleaseSubject subject = Fixture.SubjectFor(Revision) with
        {
            Channel = channel,
            Destination = destination,
            SelectedRef = selectedRef,
            DryRun = true
        };
        Assert.AreEqual(Channel, Assert.ThrowsExactly<InvalidDataException>(() =>
            PythonPreparation.Admit(subject)).Message);
    }

    private const string Channel = "Unsupported Python Release channel or ref.";

    [TestMethod]
    public async Task UnadmittedSubjectStopsBeforeNativeWork()
    {
        using var fixture = Fixture.Shared(shared!, sharedRevision);
        fixture.Subject = fixture.Subject with { Destination = "pypi" };
        ReleasePreparationResult result = await fixture.RunAsync(context.CancellationToken);
        Assert.AreEqual(ReleaseCheckStatus.Failed, result.Prepared.Checks[0].Status);
        Assert.IsEmpty(result.Prepared.Files);
        Assert.IsEmpty(result.Commands);
        Assert.AreEqual(Channel, Assert.ContainsSingle(result.Errors));
        Assert.AreEqual(fixture.Subject, result.Prepared.Subject);
    }

    [TestMethod]
    [DataRow("commit", "0000000000000000000000000000000000000000")]
    [DataRow("dependencies", "runtime")]
    [DataRow("local_version", "g1234567")]
    public void NativeFactsCannotQualifyAnotherProduct(string field, string value)
    {
        ReleaseSubject subject = Fixture.SubjectFor(Revision) with
        {
            Channel = "official",
            Destination = "pypi",
            SelectedRef = "refs/heads/main"
        };
        JsonObject facts = Fixture.Facts(Revision, "1.0.0");
        Change(facts, field, value);
        using JsonDocument document = JsonDocument.Parse(facts.ToJsonString());
        Assert.AreEqual(Identity, Assert.ThrowsExactly<InvalidDataException>(() =>
            PythonPreparation.QualifyNative(subject, official: true,
                "hcoona-release-smoke-python", "1.0.0", Interpreter, document.RootElement))
            .Message);
    }

    [TestMethod]
    [DataRow("official", false, "", "", true)]
    [DataRow("official", true, "local_version", "g1234567", false)]
    [DataRow("buddy", false, "public_release", "false", false)]
    public void NativeFactsQualifyTheirProduct(string channel, bool dryRun, string field,
        string value, bool publicRelease)
    {
        ReleaseSubject subject = Fixture.SubjectFor(Revision) with
        {
            Channel = channel,
            DryRun = dryRun
        };
        JsonObject facts = Fixture.Facts(Revision, "1.0.0");
        if (field.Length > 0) Change(facts, field, value);
        if (field == "local_version") facts["public_release"] = false;
        using JsonDocument document = JsonDocument.Parse(facts.ToJsonString());
        Assert.AreEqual((bool?)publicRelease, PythonPreparation.QualifyNative(subject,
            channel == "official", "hcoona-release-smoke-python", "1.0.0", Interpreter,
            document.RootElement));
    }

    [TestMethod]
    public void NativeNameCannotQualifyAnotherProduct()
    {
        ReleaseSubject subject = Fixture.SubjectFor(Revision);
        JsonObject facts = Fixture.Facts(Revision, "1.0.0");
        facts["name"] = "other";
        using JsonDocument document = JsonDocument.Parse(facts.ToJsonString());
        Assert.AreEqual(Identity, Assert.ThrowsExactly<InvalidDataException>(() =>
            PythonPreparation.QualifyNative(subject, official: false, "other", "1.0.0",
                Interpreter, document.RootElement)).Message);
    }

    private static void Change(JsonObject facts, string field, string value) =>
        facts[field] = field switch
        {
            "public_release" => JsonValue.Create(false),
            "dependencies" => new JsonArray(value),
            _ => JsonValue.Create(value),
        };

    // The non-public row proves production passes the admitted Official result.
    [TestMethod]
    public async Task OfficialNonPublicVersionStopsBeforeBuild()
    {
        using var fixture = Fixture.Shared(shared!, sharedRevision);
        fixture.Subject = fixture.Subject with
        {
            Channel = "official",
            Destination = "pypi",
            SelectedRef = "refs/heads/main"
        };
        fixture.ChangeFacts = facts => facts["public_release"] = false;
        ReleasePreparationResult result = await fixture.RunAsync(context.CancellationToken);
        Assert.AreEqual(ReleaseCheckStatus.Failed, result.Prepared.Checks[0].Status);
        Assert.IsEmpty(result.Prepared.Files);
        Assert.DoesNotContain("build", result.Commands.Select(command => command.Check).ToArray());
        Assert.AreEqual(Identity, Assert.ContainsSingle(result.Errors));
    }

    [TestMethod]
    public async Task DryRunRetainsNativeLocalVersionWithoutTransformingIt()
    {
        using var fixture = Fixture.Shared(shared!, sharedRevision);
        fixture.Subject = fixture.Subject with { DryRun = true, SelectedRef = "refs/tags/preview" };
        fixture.Version = "1.0.0+g1234567";
        fixture.ChangeFacts = facts =>
        {
            facts["local_version"] = "g1234567";
            facts["public_release"] = false;
        };
        ReleasePreparationResult result = await fixture.RunAsync(context.CancellationToken);
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
        using var fixture = Fixture.Shared(shared!, sharedRevision);
        NativeTermination termination = Enum.Parse<NativeTermination>(terminationText);
        fixture.Override = command => command.Arguments.Contains(argument) ?
            new(termination, termination == NativeTermination.Exited ? 1 : null,
                "partial output", "native diagnostic", 0.5, null) : null;
        ReleasePreparationResult result = await fixture.RunAsync(context.CancellationToken);
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
    [DataRow("product/workflow-delivery.quality.yml", "python/hcoona-release-smoke-python-v1",
        "python/unknown-v1", "Unsupported complete Python quality selection.")]
    [DataRow("product/workflow-delivery.release-unit.yml", "python-sdist", "other-kind",
        "Unsupported complete Python distribution set.")]
    [DataRow("uv.lock", null, null, "Missing committed native input: uv.lock")]
    public async Task UnsupportedCommittedRecipeIsRejected(string path, string? content,
        string? replacement, string message)
    {
        using var fixture = Fixture.Shared(shared!, sharedRevision);
        Dictionary<string, string> files = Fixture.Files();
        if (content is null) files.Remove(path);
        else files[path] = files[path].Replace(content, replacement, StringComparison.Ordinal);
        InvalidDataException error = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            PythonPreparation.ReadRecipeAsync(fixture.Subject, fixture.Repo.Directory,
                (name, _) => Task.FromResult(files.GetValueOrDefault(name)),
                context.CancellationToken));
        Assert.AreEqual(message, error.Message);
    }

    [TestMethod]
    [DataRow("archive")]
    [DataRow("consume")]
    public async Task ChangedOriginalCannotQualifyRelease(string phase)
    {
        using var fixture = Fixture.Shared(shared!, sharedRevision);
        fixture.Override = command =>
        {
            if (command.Arguments.Contains(phase))
                File.WriteAllText(Path.Combine(fixture.Scratch, "package", "archives",
                    "original.whl"), "changed!");
            return null;
        };
        ReleasePreparationResult result = await fixture.RunAsync(context.CancellationToken);
        Assert.IsEmpty(result.Prepared.Files);
        Assert.Contains("bytes changed", string.Join(";", result.Errors));
        Assert.IsTrue(result.Commands.All(command => command.Result.Succeeded));
        Assert.Contains(ReleaseCheckStatus.Failed,
            result.Prepared.Checks.Select(check => check.Status).ToArray());
    }

    // Each row breaks one distinct invariant between the native answer and the fixture's own
    // committed root selection; the message names the tool and the failed condition.
    [TestMethod]
    [DataRow("python", "3.12", "is not an exact release version")]
    [DataRow("uv", "0.0.0", "differs from the committed locked selection")]
    public async Task ToolOutsideCommittedSelectionStopsPreparation(string tool, string version,
        string condition)
    {
        using var fixture = Fixture.Shared(shared!, sharedRevision);
        fixture.ChangeToolAnswer = (candidate, answer) =>
        {
            if (candidate == tool && tool == "python") answer["version"] = version;
        };
        fixture.Override = command => tool == "uv" && command.Arguments.Contains("--short")
            ? Fixture.Success(version) : null;
        ReleasePreparationResult result = await fixture.RunAsync(context.CancellationToken);
        Assert.AreEqual(ReleaseCheckStatus.Failed, result.Prepared.Checks[0].Status);
        Assert.IsEmpty(result.Prepared.Files);
        Assert.DoesNotContain("build", result.Commands.Select(command => command.Check).ToArray());
        string error = Assert.ContainsSingle(result.Errors);
        Assert.Contains(" " + tool + " ", error);
        Assert.Contains(condition, error);
    }

    // The committed selection, not an application version line, decides the interpreter.
    [TestMethod]
    public async Task AnotherCommittedPythonLineQualifies()
    {
        string other = RepositoryToolSelection.NextMajor(RepositoryToolSelection.Locked("python"));
        using var fixture = await Fixture.CreateAsync(
            (path, content) => RepositoryToolSelection.RaiseMajor(path, content, "python"),
            context.CancellationToken);
        ReleasePreparationResult result = await fixture.RunAsync(context.CancellationToken);
        Assert.IsEmpty(result.Errors);
        Assert.AreEqual(other, result.Native["python"]);
    }

    [TestMethod]
    public async Task CancellationBeforeWorkProducesCompleteChecks()
    {
        using var fixture = Fixture.Shared(shared!, sharedRevision);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        ReleasePreparationResult result = await fixture.RunAsync(cancelled.Token);
        Assert.HasCount(6, result.Prepared.Checks);
        Assert.AreEqual(ReleaseCheckStatus.Cancelled, result.Prepared.Checks[0].Status);
        Assert.IsTrue(result.Prepared.Checks.Skip(1).All(check =>
            check.Status == ReleaseCheckStatus.Skipped));
        Assert.IsEmpty(result.Commands);
        Assert.IsEmpty(result.Prepared.Files);
    }

    private static readonly string[] Tested = ["native-input", "build", "contents",
        "wheel-consumer", "sdist-consumer", "tests", "original-bytes"];
    private static readonly string[] Consumers = ["wheel-consumer", "sdist-consumer"];
    private static readonly string[] LockedExport = ["--frozen", "--no-dev",
        "--no-emit-workspace", "nbgv-python"];
    private static readonly string[] SuiteSync = ["sync", "--frozen", "--package",
        "nbgv-python"];
    private static readonly string[] SuiteRun = ["pytest", "-c", "pyproject.toml",
        "src/public/lib/nbgv-python/tests"];

    [TestMethod]
    public async Task NbgvStaticVersionQualifiesOnOfficialWithLockedRuntimeAndAdoptedSuite()
    {
        using var fixture = Fixture.Shared(shared!, sharedRevision, Fixture.Nbgv);
        fixture.Subject = fixture.Subject with
        {
            Channel = "official",
            Destination = "pypi",
            SelectedRef = "refs/heads/main"
        };
        fixture.Version = "2.1.0.dev1";
        ReleasePreparationResult result = await fixture.RunAsync(context.CancellationToken);
        Assert.IsEmpty(result.Errors, string.Join("; ", result.Errors));
        ReleaseAdapters.Python.ValidatePreparation(result);
        CollectionAssert.AreEqual(Tested, result.Prepared.RequiredChecks);
        CollectionAssert.AreEqual(Tested, result.Prepared.Checks.Select(check => check.Id)
            .ToArray());
        Assert.IsTrue(result.Prepared.Files.All(file => file.Name == Fixture.Nbgv &&
            file.Version == "2.1.0.dev1"));
        Assert.AreEqual(Fixture.Nbgv, result.Native["name"]);
        Assert.IsFalse(result.Native.ContainsKey("public-release"));
        Assert.IsFalse(result.Commands.Any(command =>
            command.Command.Arguments.Contains("version-facts")));
        // Each consumer installs the hash-checked locked closure, then the original alone.
        foreach (string consumer in Consumers)
        {
            string[][] commands = [.. result.Commands.Where(command => command.Check == consumer)
                .Select(command => command.Command.Arguments)];
            int export = Array.FindIndex(commands, arguments => arguments[0] == "export");
            Assert.IsGreaterThanOrEqualTo(0, export, consumer);
            CollectionAssert.IsSubsetOf(LockedExport, commands[export]);
            string locked = commands[export][Array.IndexOf(commands[export], "--output-file") + 1];
            CollectionAssert.IsSubsetOf(new[] { "--require-hashes", "--no-deps", locked },
                commands[export + 1]);
            Assert.Contains("--no-deps", commands[export + 2]);
            Assert.Contains("consume", commands[export + 3]);
        }
        string[][] tests = [.. result.Commands.Where(command => command.Check == "tests")
            .Select(command => command.Command.Arguments)];
        Assert.HasCount(2, tests);
        CollectionAssert.IsSubsetOf(SuiteSync, tests[0]);
        CollectionAssert.IsSubsetOf(SuiteRun, tests[1]);
    }

    [TestMethod]
    public async Task NbgvFailedSuiteBlocksQualification()
    {
        using var fixture = Fixture.Shared(shared!, sharedRevision, Fixture.Nbgv);
        fixture.Version = "2.1.0.dev1";
        fixture.Override = command => command.Arguments.Contains("pytest")
            ? new(NativeTermination.Exited, 1, "1 failed", "", 0.1, null) : null;
        ReleasePreparationResult result = await fixture.RunAsync(context.CancellationToken);
        CollectionAssert.AreEqual(Tested, result.Prepared.RequiredChecks);
        CollectionAssert.AreEqual(new[] { ReleaseCheckStatus.Passed, ReleaseCheckStatus.Passed,
            ReleaseCheckStatus.Passed, ReleaseCheckStatus.Passed, ReleaseCheckStatus.Passed,
            ReleaseCheckStatus.Failed, ReleaseCheckStatus.Skipped },
            result.Prepared.Checks.Select(check => check.Status).ToArray());
        Assert.IsEmpty(result.Prepared.Files);
        Assert.IsNotEmpty(result.Errors);
    }

    [TestMethod]
    [DataRow("", "")]
    [DataRow("public_release", "true")]
    [DataRow("dependencies", "packaging>=99")]
    public void NbgvStaticFactsQualifyOnlyTheirProduct(string field, string value)
    {
        ReleaseSubject subject = Fixture.SubjectFor(Revision, Fixture.Nbgv);
        JsonObject facts = Fixture.StaticFacts("2.1.0.dev1");
        if (field.Length > 0) Change(facts, field, value);
        using JsonDocument document = JsonDocument.Parse(facts.ToJsonString());
        if (field.Length == 0)
            Assert.IsNull(PythonPreparation.QualifyNative(subject, false, Fixture.Nbgv,
                "2.1.0.dev1", Interpreter, document.RootElement));
        else
            Assert.AreEqual(Identity, Assert.ThrowsExactly<InvalidDataException>(() =>
                PythonPreparation.QualifyNative(subject, false, Fixture.Nbgv, "2.1.0.dev1",
                    Interpreter, document.RootElement)).Message);
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
        internal Action<string, JsonObject>? ChangeToolAnswer { get; set; }

        internal static async Task<Fixture> CreateAsync(Func<string, string, string> changeConfig,
            CancellationToken token)
        {
            GitFixture repo = await GitFixture.CreateAsync(token);
            try
            {
                return new(repo, SubjectFor(await CommitAsync(repo, changeConfig)));
            }
            catch { repo.Dispose(); throw; }
        }

        // Reads a class-owned committed repository; the row owns only its scratch.
        internal static Fixture Shared(GitFixture repo, string revision, string unit = Smoke) =>
            new(repo, SubjectFor(revision, unit), ownsRepository: false);

        internal const string Smoke = "hcoona-release-smoke-python";
        internal const string Nbgv = "nbgv-python";
        private const string NbgvDirectory = "src/public/lib/" + Nbgv;

        private static string Declaration(string directory) =>
            directory + "/workflow-delivery.release-unit.yml";

        internal static ReleaseSubject SubjectFor(string revision, string unit = Smoke) =>
            new(revision, "refs/heads/development", unit,
                Declaration(unit == Nbgv ? NbgvDirectory : "product"), "buddy", "testpypi", false);

        internal static async Task<string> CommitAsync(GitFixture repo,
            Func<string, string, string>? changeConfig = null, bool includeNbgv = false)
        {
            Dictionary<string, string> files = Files();
            if (includeNbgv) AddProduct(files, NbgvDirectory, Nbgv);
            foreach ((string path, string content) in files)
                await repo.SetAsync(path, changeConfig is null ? content
                    : changeConfig(path, content));
            string revision = await repo.CommitAsync();
            await repo.GitAsync("reset", "--hard", revision);
            return revision;
        }

        // The committed files of the product, keyed by repository path. The root tool
        // configuration is the actual committed one, so the fake follows its updates.
        internal static Dictionary<string, string> Files()
        {
            var files = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["mise.toml"] = RepositoryToolSelection.MiseToml,
                ["mise.lock"] = RepositoryToolSelection.MiseLock,
            };
            foreach (string input in new[] { "pyproject.toml", "uv.lock", "global.json",
                ".config/dotnet-tools.json",
                "src/private/app/workflow-delivery/Native/Python/packages.py" })
                files.Add(input, "fixture");
            AddProduct(files, "product", Smoke);
            return files;
        }

        private static void AddProduct(Dictionary<string, string> files, string directory,
            string unit)
        {
            files.Add(directory + "/pyproject.toml", "fixture");
            files.Add(directory + "/workflow-delivery.quality.yml", """
                schema: workflow-delivery/v3/quality-selection
                ecosystems:
                  python:
                    preset: python/hcoona-release-smoke-python-v1
                """);
            files.Add(Declaration(directory), """
                schema: workflow-delivery/v3/release-unit
                release-unit: hcoona-release-smoke-python
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
                """.Replace(Smoke, unit, StringComparison.Ordinal));
        }

        internal static JsonObject Facts(string revision, string version,
            string? interpreter = null) => new()
            {
                ["name"] = "hcoona-release-smoke-python",
                ["version"] = version,
                ["commit"] = revision,
                ["public_release"] = true,
                ["local_version"] = null,
                ["dependencies"] = new JsonArray(),
                ["implementation"] = "cpython",
                ["interpreter_version"] = interpreter ?? Interpreter,
            };

        // Hatchling's static version facts of the nbgv-python distribution.
        internal static JsonObject StaticFacts(string version, string? interpreter = null) =>
            new()
            {
                ["name"] = Nbgv,
                ["version"] = version,
                ["local_version"] = null,
                ["dependencies"] = new JsonArray("packaging>=24.0"),
                ["implementation"] = "cpython",
                ["interpreter_version"] = interpreter ?? Interpreter,
            };

        // The native mise answer for the fixture's own committed root selection.
        private string Selected(string tool) => RepositoryToolSelection.Locked(
            File.ReadAllText(Path.Combine(Repo.Directory, "mise.lock")), tool);

        private string ToolAnswer(string tool)
        {
            var value = new JsonObject
            {
                ["version"] = Selected(tool),
                ["requested_version"] = RepositoryToolSelection.Requested(
                    File.ReadAllText(Path.Combine(Repo.Directory, "mise.toml")), tool),
                ["active"] = true,
                ["source"] = new JsonObject
                {
                    ["type"] = "mise.toml",
                    ["path"] = Path.Combine(Repo.Directory, "mise.toml"),
                },
            };
            ChangeToolAnswer?.Invoke(tool, value);
            return new JsonArray(value).ToJsonString();
        }

        internal Task<ReleasePreparationResult> RunAsync(CancellationToken token) =>
            PythonPreparation.PrepareAsync(new(Subject, Repo.Directory, Scratch,
                MiseDataDirectory: Repo.Directory),
                ExecuteAsync, token);

        private async Task<NativeCommandResult> ExecuteAsync(NativeCommand command,
            CancellationToken token)
        {
            if (Override?.Invoke(command) is { } result) return result;
            string[] arguments = command.Arguments;
            if (arguments.Contains("--is-shallow-repository")) return Success("false");
            if (arguments.Contains("ls")) return Success(ToolAnswer(arguments[^1]));
            if (arguments.Contains("which")) return Success(Path.Combine(Scratch, arguments[^1]));
            if (arguments.Contains("--short"))
                return Success(Selected("uv"));
            if (arguments.Contains("identity")) return Success(JsonSerializer.Serialize(new
            { name = Subject.Unit, version = Version }));
            if (arguments.Contains("static-version-facts"))
            {
                JsonObject facts = StaticFacts(Version, Selected("python"));
                ChangeFacts?.Invoke(facts);
                return Success(facts.ToJsonString());
            }
            if (arguments.Contains("version-facts"))
            {
                JsonObject facts = Facts(Subject.Revision, Version, Selected("python"));
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
                    .Select(path => new
                    {
                        kind = path.EndsWith(".whl", StringComparison.Ordinal)
                        ? "python-wheel" : "python-sdist",
                        path,
                        length = new FileInfo(path).Length,
                        sha256 = Convert.ToHexStringLower(
                            SHA256.HashData(File.ReadAllBytes(path)))
                    })));
            return Success("{}");
        }

        internal static NativeCommandResult Success(string stdout) =>
            new(NativeTermination.Exited, 0, stdout, "", 0.1, null);

        public void Dispose()
        {
            if (ownsRepository) Repo.Dispose();
            PreparationCheckout.Delete(Scratch);
        }
    }
}
