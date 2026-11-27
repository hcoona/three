using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using WorkflowDelivery.Destinations;
using WorkflowDelivery.Ecosystems;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Release;
using YamlDotNet.RepresentationModel;

namespace WorkflowDelivery.Tests.Release;

// The bash Release callers (npm Buddy, Python, Ruby and GitHub Release) share the NuGet
// caller's handled verification boundary: an observation exit of 1 is a typed
// Conflict/Unknown result that still reaches completion, and any higher exit fails the step
// before completion.
[TestClass]
public sealed class BashWorkflowCallerTests(TestContext context)
{
    // The receive step is identical for every row of one caller, so it runs once per
    // ecosystem here; each row verifies from its own copy of the received tree.
    private static readonly Dictionary<string, Fixture> Received = new(StringComparer.Ordinal);

    [ClassInitialize]
    public static async Task InitializeAsync(TestContext classContext)
    {
        try
        {
            foreach (string ecosystem in new[] { "npm", "python", "ruby", "github-release" })
            {
                var fixture = new Fixture(ecosystem);
                Received.Add(ecosystem, fixture);
                NativeCommandResult received = await Step(fixture,
                    "Receive transferred preparation", fixture.Environment(),
                    classContext.CancellationToken);
                if (!received.Succeeded)
                    throw new AssertFailedException(ecosystem + " receive failed: " +
                        received.Stderr);
                Assert.IsTrue(Directory.Exists(Path.Combine(fixture.Root, "verification")));
            }
        }
        catch { Cleanup(); throw; }
    }

    [ClassCleanup]
    public static void Cleanup()
    {
        foreach (Fixture fixture in Received.Values) fixture.Dispose();
        Received.Clear();
    }

    [TestMethod]
    [DataRow("npm", "Exact", 0, "Satisfied")]
    [DataRow("npm", "Conflict", 1, "Unsatisfied")]
    [DataRow("python", "Exact", 0, "Satisfied")]
    [DataRow("python", "Conflict", 1, "Unsatisfied")]
    [DataRow("ruby", "Exact", 0, "Satisfied")]
    [DataRow("ruby", "Conflict", 1, "Unsatisfied")]
    [DataRow("github-release", "Exact", 0, "Satisfied")]
    [DataRow("github-release", "Conflict", 1, "Unsatisfied")]
    // One handled exit-1 row per caller; ReleaseStateTests maps each typed state.
    public async Task ActualVerificationStepRetainsTypedRegistryOutcome(string ecosystem,
        string state, int expectedExit, string expectedState)
    {
        using var fixture = Fixture.CopyOf(Received[ecosystem]);
        fixture.WriteFinal(Enum.Parse<ReleaseFileState>(state));
        var environment = fixture.Environment();
        // The actual observe command exits 1 for a handled conflict or unknown state.
        environment["CONTROLLED_OBSERVATION_EXIT"] = state == "Exact" ? "0" : "1";

        NativeCommandResult result = await Step(fixture, fixture.VerifyStep, environment,
            context.CancellationToken);

        Assert.AreEqual(expectedExit, result.ExitCode, result.Stderr);
        ReleaseOutcome outcome = fixture.Outcome();
        Assert.AreEqual(Enum.Parse<ReleaseBusinessState>(expectedState), outcome.State);
        Assert.AreEqual(fixture.Prepared.Subject, outcome.Subject);
        Assert.IsEmpty(outcome.Uploads);
    }

    [TestMethod]
    [DataRow("npm")]
    [DataRow("python")]
    [DataRow("ruby")]
    [DataRow("github-release")]
    public async Task ActualVerificationStepStopsOnUnhandledObservationFailure(string ecosystem)
    {
        using var fixture = Fixture.CopyOf(Received[ecosystem]);
        fixture.WriteFinal(ReleaseFileState.Exact);
        var environment = fixture.Environment();
        // Exit 2 is an unhandled native failure, not a typed destination state.
        environment["CONTROLLED_OBSERVATION_EXIT"] = "2";

        NativeCommandResult result = await Step(fixture, fixture.VerifyStep, environment,
            context.CancellationToken);

        Assert.AreEqual(2, result.ExitCode, result.Stderr);
        Assert.IsFalse(File.Exists(Path.Combine(fixture.Root, "verification/outcome.json")));
    }

    [TestMethod]
    [DataRow("npm", "Exact", 0, "Satisfied")]
    [DataRow("npm", "Absent", 1, "Unsatisfied")]
    [DataRow("python", "Exact", 0, "Satisfied")]
    [DataRow("python", "Absent", 1, "Unsatisfied")]
    [DataRow("ruby", "Exact", 0, "Satisfied")]
    [DataRow("ruby", "Absent", 1, "Unsatisfied")]
    public async Task OfficialVerificationStepFoldsPrimaryAndGithubAnchorState(string ecosystem,
        string anchorState, int expectedExit, string expectedState)
    {
        using var fixture = new Fixture(ecosystem, official: true);
        NativeCommandResult received = await Step(fixture, "Receive transferred preparation",
            fixture.Environment(), context.CancellationToken);
        Assert.IsTrue(received.Succeeded, received.Stderr);
        fixture.WriteFinal(ReleaseFileState.Exact);
        fixture.WriteAnchorFinal(Enum.Parse<ReleaseFileState>(anchorState));
        var environment = fixture.Environment();

        NativeCommandResult result = await Step(fixture, fixture.VerifyStep, environment,
            context.CancellationToken);

        Assert.AreEqual(expectedExit, result.ExitCode, result.Stderr);
        ReleaseOutcome outcome = fixture.Outcome();
        Assert.AreEqual(Enum.Parse<ReleaseBusinessState>(expectedState), outcome.State);
        Assert.AreEqual(fixture.Prepared.Subject, outcome.Subject);
    }

    private static Task<NativeCommandResult> Step(Fixture fixture, string name,
        Dictionary<string, string?> environment, CancellationToken token)
    {
        // Only the native destination observation is controlled. The actual workflow
        // shell and typed application phase/completion commands run unchanged.
        const string shim = """
            workflow_dotnet() {
              shift
              "$WORKFLOW_DOTNET" "$WORKFLOW_APP" "$@"
            }
            mise() {
              shift 4
              if [ "$3" = "$CONTROLLED_OPERATION" ]; then
                cat "$RUNNER_TEMP/controlled-final.json"
                return @@EXIT@@
              fi
              if [ "$3" = "observe-github-anchor" ] \
                && [ -f "$RUNNER_TEMP/controlled-anchor-final.json" ]; then
                cp "$RUNNER_TEMP/controlled-anchor-final.json" "$6"
                return "${CONTROLLED_ANCHOR_EXIT:-0}"
              fi
              workflow_dotnet "$@"
            }
            """;
        // The workflow is an embedded resource so that native CI scope selects this test
        // whenever the caller workflow changes.
        var yaml = new YamlStream();
        using (Stream? resource = typeof(BashWorkflowCallerTests).Assembly
            .GetManifestResourceStream(fixture.Workflow))
        using (var reader = new StreamReader(resource ?? throw new AssertFailedException(
            "Missing embedded caller workflow: " + fixture.Workflow)))
            yaml.Load(reader);
        var document = (YamlMappingNode)yaml.Documents[0].RootNode;
        var jobs = (YamlMappingNode)document.Children["jobs"];
        var selected = (YamlMappingNode)jobs.Children[fixture.Job];
        var steps = (YamlSequenceNode)selected.Children["steps"];
        YamlMappingNode step = steps.Children.Cast<YamlMappingNode>().Single(value =>
            value.Children.TryGetValue("name", out YamlNode? label) && label.ToString() == name);
        // Match the Actions bash invocation: the closest explicit "shell: bash" (step, job,
        // then workflow default) adds pipefail; an unset shell runs "bash -e".
        string? shell = Shell(step) ?? Shell(Defaults(selected)) ?? Shell(Defaults(document));
        Assert.IsTrue(shell is null or "bash", "Unsupported caller shell: " + shell);
        string[] flags = shell is null ? ["-e"] : ["--noprofile", "--norc", "-eo", "pipefail"];
        // A CRLF checkout of this source must not leak carriage returns into the shim.
        string script = (shim.Replace("@@EXIT@@", "\"${CONTROLLED_OBSERVATION_EXIT:-0}\"",
            StringComparison.Ordinal) + "\n" + step.Children["run"]).ReplaceLineEndings("\n");
        return NativeProcess.ExecuteAsync(new(Bash(), fixture.Root, [.. flags, "-c", script],
            60, environment), token);
    }

    private static YamlMappingNode? Defaults(YamlMappingNode node) =>
        node.Children.TryGetValue("defaults", out YamlNode? defaults) &&
            ((YamlMappingNode)defaults).Children.TryGetValue("run", out YamlNode? run)
            ? (YamlMappingNode)run : null;

    private static string? Shell(YamlMappingNode? node) =>
        node is not null && node.Children.TryGetValue("shell", out YamlNode? shell)
            ? shell.ToString() : null;

    // Windows process creation searches the system directories (the WSL launcher) before
    // PATH, while Actions runs "shell: bash" with the Git Bash found on PATH.
    private static string Bash()
    {
        if (!OperatingSystem.IsWindows()) return "bash";
        string system = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Windows);
        return (System.Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => Resolve(directory.Trim('"')))
            .Where(directory => directory is not null && !directory.StartsWith(system,
                StringComparison.OrdinalIgnoreCase))
            .Select(directory => Path.Combine(directory!, "bash.exe"))
            .FirstOrDefault(File.Exists) ?? throw new AssertFailedException(
                "Git Bash is unavailable on PATH.");

        static string? Resolve(string directory)
        {
            try { return Path.GetFullPath(directory); }
            catch (Exception exception) when (exception is ArgumentException or
                NotSupportedException or PathTooLongException)
            { return null; }
        }
    }

    private sealed class Fixture : IDisposable
    {
        private const string Products = "tests/private/app/workflow-delivery/fixtures/products/";
        private readonly string ecosystem;
        private readonly bool official;
        internal string Root { get; } = Directory.CreateTempSubdirectory("bash-caller-tests-")
            .FullName;
        internal PreparedRelease Prepared { get; }
        internal string Workflow => ecosystem switch
        {
            "npm" => "workflow-delivery-v3-npm-release.yml",
            "github-release" => "workflow-delivery-v3-github-release.yml",
            "ruby" or "python" => "workflow-delivery-v3-" + ecosystem + "-release.yml",
            _ => "workflow-delivery-v3-" + ecosystem + "-smoke.yml",
        };
        internal string Job => ecosystem == "npm" ? "verify" : "verify-result";
        internal string VerifyStep => ecosystem switch
        {
            "npm" => "Verify registry coordinate and original bytes",
            "python" => "Verify both index files and original bytes in final public state",
            "github-release" => "Read back every published asset against its original bytes",
            _ => "Verify native source original and complete final native state",
        };

        private Fixture(Fixture received)
        {
            ecosystem = received.ecosystem;
            official = received.official;
            Prepared = received.Prepared;
            foreach (string file in Directory.EnumerateFiles(received.Root, "*",
                SearchOption.AllDirectories))
            {
                string target = Path.Combine(Root, Path.GetRelativePath(received.Root, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
            foreach (string directory in Directory.EnumerateDirectories(received.Root, "*",
                SearchOption.AllDirectories))
                Directory.CreateDirectory(Path.Combine(Root,
                    Path.GetRelativePath(received.Root, directory)));
        }

        // A row-owned copy of one caller's received tree.
        internal static Fixture CopyOf(Fixture received) => new(received);

        internal Fixture(string ecosystem, bool official = false)
        {
            this.ecosystem = ecosystem;
            this.official = official;
            bool github = ecosystem == "github-release";
            string unit = github ? "image-occlusion-editor"
                : official ? ecosystem switch
                {
                    "npm" => "hexo-renderer-asciidoc",
                    "python" => "nbgv-python",
                    _ => "asciidoctor-latexmath",
                }
                : "hcoona-release-smoke-" + ecosystem;
            string declaration = github ? ReleaseUnitCatalog.GithubRelease(unit).Declaration
                : official ? "src/public/lib/" + unit + "/workflow-delivery.release-unit.yml"
                : Products + unit + "/workflow-delivery.release-unit.yml";
            var subject = new ReleaseSubject(new string('a', 40),
                official ? "refs/heads/main" : "refs/heads/development", unit, declaration,
                official ? "official" : "buddy",
                ecosystem switch
                {
                    "npm" => official ? "npmjs" : "github-packages-npm",
                    "python" => official ? "pypi" : "testpypi",
                    "github-release" => GithubReleaseUnit.BuddyDestination,
                    _ => official ? "rubygems" : "github-packages-ruby",
                }, false);
            ReleaseFile[] files = ecosystem switch
            {
                "npm" => [Original("build", "archive", "npm-tarball",
                    official ? unit : AdoptedNpmPackage.PackageName, "original/package.tgz")],
                "python" => [Original("distributions", "wheel", "python-wheel", unit,
                    "archives/package.whl"), Original("distributions", "sdist", "python-sdist",
                    unit, "archives/package.tar.gz")],
                "github-release" => [Original("windows-app", "installer", "inno-installer",
                    ReleaseUnitCatalog.GithubRelease(unit).Asset("inno-installer", "1.0.0"),
                    "originals/setup.exe"),
                    Original("windows-app", "portable", "portable-zip",
                    ReleaseUnitCatalog.GithubRelease(unit).Asset("portable-zip", "1.0.0"),
                    "originals/portable.zip")],
                _ => [Original("gem-build", "gem-output", "ruby-gem", unit, "original.gem")],
            };
            string[] required = ecosystem switch
            {
                "npm" => ["consumer"],
                "python" => PythonPreparation.RequiredFor(ReleaseUnitCatalog.Python(unit)),
                "github-release" => GithubReleasePreparation.Required,
                _ => RubyPreparation.RequiredFor(ReleaseUnitCatalog.Ruby(unit)),
            };
            ReleaseFile[] anchorAssets = [.. files.Select(file => file with
            {
                Name = Path.GetFileName(file.Path),
            })];
            Prepared = new(subject, files, required, required.Select(id =>
                new ReleaseCheck(id, ReleaseCheckStatus.Passed)).ToArray(),
                Anchor: official ? new(anchorAssets, "### Added\n\n- Official notes.") : null,
                ReleaseNotes: official ? "### Added\n\n- Official notes." : null);
            var native = new Dictionary<string, string>
            {
                ["name"] = unit,
                ["version"] = "1.0.0",
                ["commit"] = subject.Revision,
            };
            if (official && (ecosystem == "ruby" ||
                ecosystem == "python" && !ReleaseUnitCatalog.Python(unit).StaticVersion))
                native["public-release"] = "true";
            if (ecosystem == "ruby") native["platform"] = "ruby";
            if (github) native["tag"] = GithubReleaseUnit.Tag(subject, "1.0.0");
            ReleaseCommand[] commands = [new("build", new("native", Root, ["build"], 60),
                new(NativeTermination.Exited, 0, "controlled native build", "", 0.1, null))];
            foreach (string phase in new[] { "prepared", "observation" })
                Directory.CreateDirectory(Path.Combine(Root, phase));
            string preparation = Path.Combine(Root, "prepared/preparation.json");
            Write(preparation, new ReleasePreparationResult(Prepared,
                ecosystem == "npm" ? new(StringComparer.Ordinal) : native, commands, []),
                TransferJson.Default.ReleasePreparationResult);
            // The initial observation is exact, so completion requires no publication result.
            WriteObservation("observation/observation.json", ReleaseFileState.Exact);
            if (official)
                WriteAnchorObservation("observation/anchor-observation.json",
                    ReleaseFileState.Exact);
            if (ecosystem == "python")
                Write(Path.Combine(Root, "index-tool-result.json"), new PythonIndexToolResult(
                    "controlled-python", [], []), TransferJson.Default.PythonIndexToolResult);
        }

        internal Dictionary<string, string?> Environment() => new()
        {
            ["RUNNER_TEMP"] = Root,
            ["DESTINATION"] = ecosystem switch
            {
                "python" => official ? "pypi" : "testpypi",
                "npm" => official ? "npmjs" : "github-packages",
                "ruby" => official ? "rubygems" : "github-packages",
                "github-release" => GithubReleaseUnit.BuddyDestination,
                _ => "github-packages",
            },
            ["DRY_RUN"] = "false",
            ["UNIT"] = Prepared.Subject.Unit,
            ["WRITE_NEEDED"] = "false",
            ["GITHUB_SHA"] = Prepared.Subject.Revision,
            ["GITHUB_REF"] = Prepared.Subject.SelectedRef,
            ["GITHUB_RUN_ID"] = "123",
            ["GITHUB_RUN_ATTEMPT"] = "1",
            ["DOTNET_EXECUTABLE"] = "workflow_dotnet",
            // The selected runtime setup exports the committed SDK selection.
            ["DOTNET_VERSION"] = RepositoryToolSelection.DotNetSdkVersion,
            ["RUBY_EXECUTABLE"] = "controlled-ruby",
            ["CONTROLLED_OPERATION"] = "observe-" + (ecosystem == "npm"
                ? (official ? "npmjs" : "github-npm") : ecosystem),
            ["WORKFLOW_APP"] = typeof(Program).Assembly.Location,
            ["WORKFLOW_DOTNET"] = System.Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")
                ?? "dotnet",
        };

        internal void WriteFinal(ReleaseFileState state) =>
            WriteObservation("controlled-final.json", state);

        internal void WriteAnchorFinal(ReleaseFileState state) =>
            WriteAnchorObservation("controlled-anchor-final.json", state);

        internal ReleaseOutcome Outcome() => ReleaseCli.Read(
            Path.Combine(Root, "verification/outcome.json"),
            TransferJson.Default.ReleaseCompletionResult).Outcome;

        private void WriteAnchorObservation(string name, ReleaseFileState state)
        {
            var obs = new ReleaseObservation(Prepared.Subject,
                Prepared.Anchor!.Assets.Select(file => new ReleaseFileObservation(file.Output,
                    file.Name, file.Version, state, state == ReleaseFileState.Exact ? file.Size
                        : null, state == ReleaseFileState.Exact ? file.Sha256 : null)).ToArray(),
                [GithubReleaseDestination.Check],
                [new(GithubReleaseDestination.Check, ReleaseCheckStatus.Passed)]);
            Write(Path.Combine(Root, name), new ReleaseObservationResult(obs, [], ReleaseId: 9),
                TransferJson.Default.ReleaseObservationResult);
        }

        private void WriteObservation(string name, ReleaseFileState state)
        {
            string path = Path.Combine(Root, name);
            ReleaseObservation Observation(string check) => new(Prepared.Subject,
                Prepared.Files.Select(file => new ReleaseFileObservation(file.Output, file.Name,
                    file.Version, state, state == ReleaseFileState.Exact ? file.Size : null,
                    state == ReleaseFileState.Exact ? file.Sha256 : null)).ToArray(), [check],
                [new(check, ReleaseCheckStatus.Passed)]);
            ReleaseObservationResult observed = ecosystem switch
            {
                "npm" => new(Observation("native-package-control"), []),
                "python" => new(Observation("public-index-files"), []),
                "github-release" => new(Observation(GithubReleaseDestination.Check), [],
                    ReleaseId: 7),
                _ => new(Observation("native-source-original"), [], RubySource: new(state switch
                {
                    ReleaseFileState.Absent => RubySourceState.Absent,
                    ReleaseFileState.Exact => RubySourceState.Present,
                    _ => RubySourceState.Unknown,
                }, null, null, [])),
            };
            Write(path, observed, TransferJson.Default.ReleaseObservationResult);
        }

        private static ReleaseFile Original(string build, string output, string kind, string name,
            string path)
        {
            byte[] bytes = Encoding.UTF8.GetBytes("controlled original " + kind);
            return new(build, output, kind, name, "1.0.0", path, bytes.LongLength,
                Convert.ToHexStringLower(SHA256.HashData(bytes)));
        }

        private static void Write<T>(string path, T value, JsonTypeInfo<T> type) =>
            File.WriteAllText(path, JsonSerializer.Serialize(value, type));

        public void Dispose() => Directory.Delete(Root, true);
    }
}
