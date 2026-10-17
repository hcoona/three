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

// The bash Release callers (npm Buddy, Python and Ruby) share the NuGet caller's handled
// verification boundary: an observation exit of 1 is a typed Conflict/Unknown result that
// still reaches completion, and any higher exit fails the step before completion.
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
            foreach (string ecosystem in new[] { "npm", "python", "ruby" })
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
        internal string Root { get; } = Directory.CreateTempSubdirectory("bash-caller-tests-")
            .FullName;
        internal PreparedRelease Prepared { get; }
        internal string Workflow => "workflow-delivery-v3-" + (ecosystem ==
            "npm" ? "buddy" : ecosystem) + "-smoke.yml";
        internal string Job => ecosystem == "npm" ? "verify" : "verify-result";
        internal string VerifyStep => ecosystem switch
        {
            "npm" => "Verify registry coordinate and original bytes",
            "python" => "Verify both index files and original bytes in final public state",
            _ => "Verify native source original and complete final native state",
        };

        private Fixture(Fixture received)
        {
            ecosystem = received.ecosystem;
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

        internal Fixture(string ecosystem)
        {
            this.ecosystem = ecosystem;
            string unit = "hcoona-release-smoke-" + ecosystem;
            var subject = new ReleaseSubject(new string('a', 40), "refs/heads/development", unit,
                Products + unit + "/workflow-delivery.release-unit.yml", "buddy",
                ecosystem switch
                {
                    "npm" => "github-packages-npm",
                    "python" => "testpypi",
                    _ => "github-packages-ruby",
                }, false);
            ReleaseFile[] files = ecosystem switch
            {
                "npm" => [Original("build", "archive", "npm-tarball", GithubNpmObservation.Package,
                    "original/package.tgz")],
                "python" => [Original("distributions", "wheel", "python-wheel", unit,
                    "archives/package.whl"), Original("distributions", "sdist", "python-sdist",
                    unit, "archives/package.tar.gz")],
                _ => [Original("gem-build", "gem-output", "ruby-gem", unit, "original.gem")],
            };
            string[] required = ecosystem switch
            {
                "npm" => ["consumer"],
                "python" => PythonPreparation.Required,
                _ => RubyPreparation.Required,
            };
            Prepared = new(subject, files, required, required.Select(id =>
                new ReleaseCheck(id, ReleaseCheckStatus.Passed)).ToArray());
            var native = new Dictionary<string, string>
            {
                ["name"] = unit,
                ["version"] = "1.0.0",
                ["commit"] = subject.Revision,
            };
            if (ecosystem == "ruby") native["platform"] = "ruby";
            ReleaseCommand[] commands = [new("build", new("native", Root, ["build"], 60),
                new(NativeTermination.Exited, 0, "controlled native build", "", 0.1, null))];
            foreach (string phase in new[] { "prepared", "observation" })
                Directory.CreateDirectory(Path.Combine(Root, phase));
            string preparation = Path.Combine(Root, "prepared/preparation.json");
            if (ecosystem == "npm")
                Write(preparation, new NpmPreparationResult(Prepared, commands, []),
                    TransferJson.Default.NpmPreparationResult);
            else if (ecosystem == "python")
                Write(preparation, new PythonPreparationResult(Prepared, native, commands, []),
                    TransferJson.Default.PythonPreparationResult);
            else
                Write(preparation, new RubyPreparationResult(Prepared, native, commands, []),
                    TransferJson.Default.RubyPreparationResult);
            // The initial observation is exact, so completion requires no publication result.
            WriteObservation("observation/observation.json", ReleaseFileState.Exact);
            if (ecosystem == "python")
                Write(Path.Combine(Root, "index-tool-result.json"), new PythonIndexToolResult(
                    "controlled-python", [], []), TransferJson.Default.PythonIndexToolResult);
        }

        internal Dictionary<string, string?> Environment() => new()
        {
            ["RUNNER_TEMP"] = Root,
            ["DESTINATION"] = ecosystem switch
            {
                "npm" => "github-packages-npm",
                "python" => "testpypi",
                _ => "github-packages",
            },
            ["DRY_RUN"] = "false",
            ["WRITE_NEEDED"] = "false",
            ["GITHUB_SHA"] = Prepared.Subject.Revision,
            ["GITHUB_REF"] = Prepared.Subject.SelectedRef,
            ["GITHUB_RUN_ID"] = "123",
            ["GITHUB_RUN_ATTEMPT"] = "1",
            ["DOTNET_EXECUTABLE"] = "workflow_dotnet",
            ["RUBY_EXECUTABLE"] = "controlled-ruby",
            ["CONTROLLED_OPERATION"] = "observe-" + (ecosystem == "npm" ? "github-npm" :
                ecosystem),
            ["WORKFLOW_APP"] = typeof(Program).Assembly.Location,
            ["WORKFLOW_DOTNET"] = System.Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")
                ?? "dotnet",
        };

        internal void WriteFinal(ReleaseFileState state) =>
            WriteObservation("controlled-final.json", state);

        internal ReleaseOutcome Outcome()
        {
            string json = File.ReadAllText(Path.Combine(Root, "verification/outcome.json"));
            return ecosystem switch
            {
                "npm" => JsonSerializer.Deserialize(json, TransferJson.Default.ReleaseOutcome)!,
                "python" => JsonSerializer.Deserialize(json,
                    TransferJson.Default.PythonCompletionResult)!.Outcome,
                _ => JsonSerializer.Deserialize(json,
                    TransferJson.Default.RubyCompletionResult)!.Outcome,
            };
        }

        private void WriteObservation(string name, ReleaseFileState state)
        {
            string path = Path.Combine(Root, name);
            ReleaseObservation Observation(string check) => new(Prepared.Subject,
                Prepared.Files.Select(file => new ReleaseFileObservation(file.Output, file.Name,
                    file.Version, state, state == ReleaseFileState.Exact ? file.Size : null,
                    state == ReleaseFileState.Exact ? file.Sha256 : null)).ToArray(), [check],
                [new(check, ReleaseCheckStatus.Passed)]);
            if (ecosystem == "npm")
                Write(path, new GithubNpmObservationResult(Observation("native-package-control"),
                    []), TransferJson.Default.GithubNpmObservationResult);
            else if (ecosystem == "python")
                Write(path, new PythonIndexObservationResult(Observation("public-index-files"),
                    []), TransferJson.Default.PythonIndexObservationResult);
            else
                Write(path, new RubyObservationResult(Observation("native-source-original"),
                    new(state switch
                    {
                        ReleaseFileState.Absent => RubySourceState.Absent,
                        ReleaseFileState.Exact => RubySourceState.Present,
                        _ => RubySourceState.Unknown,
                    }, null, null, [])), TransferJson.Default.RubyObservationResult);
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
