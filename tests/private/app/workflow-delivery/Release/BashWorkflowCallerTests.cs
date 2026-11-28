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

// Exercise the actual shell callers with controlled destination evidence, not live readers.
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
    [DataRow("python", "Absent", "Unknown", false, "false")]
    [DataRow("python", "Conflict", "Absent", false, "false")]
    [DataRow("python", "Exact", "Absent", false, "true")]
    [DataRow("github-release", "Exact", "Absent", true, "true")]
    [DataRow("ruby", "Absent", null, true, "true")]
    public void CallerRoutesOnlySupportedMissingWork(string ecosystem, string first,
        string? second, bool dryRun, string needed)
    {
        using var fixture = new Fixture(ecosystem, dryRun: dryRun);
        fixture.WriteObservation(
            "observation/observation.json", Enum.Parse<ReleaseFileState>(first),
            second is null ? null : Enum.Parse<ReleaseFileState>(second));
        Assert.AreEqual($"write-needed={needed}\n", fixture.State());
    }

    [TestMethod]
    [DataRow("python", "Absent", "true")]
    [DataRow("npm", "Exact", "false")]
    [DataRow("ruby", "Absent", "true")]
    public void OfficialCallerRoutesAnchorFromItsOwnObservation(
        string ecosystem,
        string state,
        string needed)
    {
        using var fixture = new Fixture(ecosystem, official: true);
        fixture.WriteAnchorObservation(
            "observation/anchor-observation.json", Enum.Parse<ReleaseFileState>(state));
        Assert.AreEqual($"write-needed=false\nanchor-write-needed={needed}\n", fixture.State());
    }

    [TestMethod]
    public void CallerCannotRouteWithoutItsBoundPreparation()
    {
        using var fixture = new Fixture("npm");
        Assert.ThrowsExactly<InvalidDataException>(
            () => fixture.State(revision: new string('b', 40)));
        File.Delete(Path.Combine(fixture.Root, "prepared/preparation.json"));
        Assert.ThrowsExactly<FileNotFoundException>(() => fixture.State());
        Assert.IsFalse(File.Exists(Path.Combine(fixture.Root, "output")));
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

    [TestMethod]
    [DataRow("npm", false, "Conflict", null, "Unsatisfied")]
    [DataRow("python", false, "Unknown", null, "Unknown")]
    [DataRow("ruby", false, "Conflict", null, "Unsatisfied")]
    [DataRow("github-release", false, "Conflict", null, "Unsatisfied")]
    [DataRow("github-release", true, "Unknown", null, "Unknown")]
    [DataRow("npm", true, "Conflict", "Absent", "Unsatisfied")]
    [DataRow("python", true, "Absent", "Conflict", "Unsatisfied")]
    [DataRow("ruby", true, "Absent", "Unknown", "Unknown")]
    [DataRow("chrome-web-store", true, "Conflict", "Absent", "Unsatisfied")]
    [DataRow("firefox-amo", true, "Unobserved", "Conflict", "Unsatisfied")]
    [DataRow("edge-addons", true, "Unobserved", "Absent", "Unknown")]
    public async Task ActualPreflightRoutesTypedRefusalToReadOnlyCompletion(string ecosystem,
        bool official, string primaryState, string? anchorState, string expectedState)
    {
        using var fixture = new Fixture(ecosystem, official);
        fixture.WriteFinal(Enum.Parse<ReleaseFileState>(primaryState));
        if (anchorState is not null)
            fixture.WriteAnchorFinal(Enum.Parse<ReleaseFileState>(anchorState));
        var environment = fixture.Environment();
        environment["CONTROLLED_OBSERVATION_EXIT"] =
            primaryState is "Conflict" or "Unknown" ? "1" : "0";
        environment["CONTROLLED_ANCHOR_EXIT"] = anchorState is "Conflict" or "Unknown" ? "1" : "0";

        NativeCommandResult observed = await ObserveInitial(fixture, environment);
        Assert.IsTrue(observed.Succeeded, observed.Stderr);
        Assert.AreEqual(
            File.ReadAllText(Path.Combine(fixture.Root, "controlled-final.json")).Trim(),
            File.ReadAllText(Path.Combine(fixture.Root, "observation/observation.json")).Trim());
        string anchorPath = Path.Combine(fixture.Root, "observation/anchor-observation.json");
        if (anchorState is not null)
            Assert.AreEqual(
                File.ReadAllText(Path.Combine(fixture.Root, "controlled-anchor-final.json")),
                File.ReadAllText(anchorPath));
        else
            Assert.IsFalse(File.Exists(anchorPath));
        NativeCommandResult state = await Step(fixture, fixture.StateStep, environment,
            context.CancellationToken, job: "observe");
        Assert.IsTrue(state.Succeeded, state.Stderr);
        string outputs = "write-needed=false\n";
        if (anchorState is not null)
            outputs += fixture.Store == "edge-addons"
                ? "anchor-write-needed=true\n" : "anchor-write-needed=false\n";
        Assert.AreEqual(outputs, File.ReadAllText(environment["GITHUB_OUTPUT"]!));
        // An unresolved Edge clearance can have an anchor plan, but no primary proof.
        if (fixture.Store == "edge-addons") Assert.IsFalse(fixture.AnchorPermitted());

        // A later good read does not retroactively grant a write gate or erase a refusal.
        fixture.WriteFinal(fixture.Store is null
            ? ReleaseFileState.Exact : ReleaseFileState.Accepted);
        if (anchorState is not null) fixture.WriteAnchorFinal(ReleaseFileState.Exact);
        environment["CONTROLLED_OBSERVATION_EXIT"] = "0";
        environment["CONTROLLED_ANCHOR_EXIT"] = "0";
        NativeCommandResult result = await Verify(fixture, environment);

        Assert.AreEqual(1, result.ExitCode, result.Stderr);
        ReleaseCompletionResult report = fixture.Report();
        Assert.AreEqual(Enum.Parse<ReleaseBusinessState>(expectedState), report.Outcome.State);
        Assert.AreEqual(false, report.MutationAttempted);
        Assert.IsNull(report.Publication);
        Assert.IsNull(report.AnchorPublication);
        if (anchorState is "Conflict" or "Unknown")
            Assert.Contains("the initial GitHub Release anchor observation did not permit a write",
                string.Join("; ", report.Outcome.Reasons));
        if (fixture.Store is "firefox-amo" or "edge-addons")
            Assert.IsFalse(File.Exists(Path.Combine(fixture.Root, "verification/final.json")));
    }

    [TestMethod]
    [DataRow("github-release", "primary-error")]
    [DataRow("github-release", "primary-status")]
    [DataRow("ruby", "missing-primary")]
    [DataRow("npm", "anchor-error")]
    [DataRow("python", "missing-anchor")]
    [DataRow("python", "anchor-status")]
    [DataRow("chrome-web-store", "malformed-anchor")]
    public async Task ActualPreflightRejectsInvalidEvidence(string ecosystem, string fault)
    {
        using var fixture = new Fixture(ecosystem, official: true);
        if (fault != "missing-primary") fixture.WriteFinal(ReleaseFileState.Absent);
        if (fault is "anchor-error" or "anchor-status")
            fixture.WriteAnchorFinal(ReleaseFileState.Absent);
        if (fault == "malformed-anchor")
            File.WriteAllText(Path.Combine(fixture.Root, "controlled-anchor-final.json"), "{");
        var environment = fixture.Environment();
        environment["CONTROLLED_OBSERVATION_EXIT"] = fault switch
        {
            "primary-error" => "2",
            "missing-primary" or "primary-status" => "1",
            _ => "0",
        };
        environment["CONTROLLED_ANCHOR_EXIT"] = fault == "anchor-error" ? "2" : "1";

        NativeCommandResult observed = await ObserveInitial(fixture, environment);
        Assert.AreEqual(fault.EndsWith("-error", StringComparison.Ordinal) ? 2 : 0,
            observed.ExitCode, observed.Stderr);
        if (observed.Succeeded)
        {
            NativeCommandResult state = await Step(fixture, fixture.StateStep, environment,
                context.CancellationToken, job: "observe");
            Assert.AreEqual(2, state.ExitCode, state.Stderr);
        }
        Assert.IsFalse(File.Exists(environment["GITHUB_OUTPUT"]!));
        Assert.IsFalse(Directory.Exists(Path.Combine(fixture.Root, "verification")));
    }

    [TestMethod]
    public async Task ActualAdmittedPreflightDoesNotMistakeLostReceiptForNonStart()
    {
        using var fixture = new Fixture("npm", official: true);
        fixture.WriteFinal(ReleaseFileState.Absent);
        fixture.WriteAnchorFinal(ReleaseFileState.Exact);
        var environment = fixture.Environment();

        NativeCommandResult observed = await ObserveInitial(fixture, environment);
        Assert.IsTrue(observed.Succeeded, observed.Stderr);
        NativeCommandResult state = await Step(fixture, fixture.StateStep, environment,
            context.CancellationToken, job: "observe");
        Assert.IsTrue(state.Succeeded, state.Stderr);
        Assert.AreEqual("write-needed=true\nanchor-write-needed=false\n",
            File.ReadAllText(environment["GITHUB_OUTPUT"]!));

        fixture.WriteFinal(ReleaseFileState.Exact);
        NativeCommandResult result = await Verify(fixture, environment);

        Assert.AreEqual(1, result.ExitCode, result.Stderr);
        ReleaseCompletionResult report = fixture.Report();
        Assert.AreEqual(ReleaseBusinessState.Unknown, report.Outcome.State);
        Assert.IsNull(report.MutationAttempted);
        Assert.Contains("publication result is missing; effects remain unknown",
            string.Join("; ", report.Outcome.Reasons));
    }

    [TestMethod]
    [DataRow("npm", true)]
    [DataRow("npm", false)]
    [DataRow("python", true)]
    [DataRow("python", false)]
    [DataRow("ruby", true)]
    [DataRow("ruby", false)]
    [DataRow("chrome-web-store", true)]
    [DataRow("chrome-web-store", false)]
    public async Task ActualVerificationUsesOtherReceiptAfterOneDownloadFails(string ecosystem,
        bool primaryLost)
    {
        using var fixture = new Fixture(ecosystem, official: true);
        fixture.WriteObservation("observation/observation.json", ReleaseFileState.Absent);
        fixture.WriteAnchorObservation(
            "observation/anchor-observation.json", ReleaseFileState.Absent);
        string primaryReceipt = fixture.WriteReceipt(anchor: false);
        string gateReceipt = Path.Combine(fixture.Root, "gate-primary-receipt.json");
        File.Copy(primaryReceipt, gateReceipt);
        fixture.WriteReceipt(anchor: true);
        // The gate's independent copy permits the anchor. Loss at the verifier does not undo
        // that proof, but a missing gate copy would never have permitted the anchor.
        Assert.IsFalse(fixture.AnchorPermitted());
        Assert.IsTrue(fixture.AnchorPermitted(gateReceipt));
        fixture.WriteFinal(fixture.Store is null
            ? ReleaseFileState.Exact : ReleaseFileState.Accepted);
        fixture.WriteAnchorFinal(ReleaseFileState.Exact);
        var environment = fixture.Environment();
        environment["PRIMARY_RECEIPT_OUTCOME"] = primaryLost ? "failure" : "success";
        environment["ANCHOR_RECEIPT_OUTCOME"] = primaryLost ? "success" : "failure";

        NativeCommandResult result = await Verify(fixture, environment);

        Assert.AreEqual(1, result.ExitCode, result.Stderr);
        ReleaseCompletionResult report = fixture.Report();
        Assert.AreEqual(ReleaseBusinessState.Unknown, report.Outcome.State);
        Assert.AreEqual(true, report.MutationAttempted);
        Assert.AreEqual(primaryLost ? ReleaseBusinessState.Satisfied : ReleaseBusinessState.Unknown,
            report.AnchorOutcome!.State);
        // Both receipts look valid, but the failed download's bytes are not evidence.
        Assert.AreEqual(!primaryLost, report.Publication is not null);
        Assert.AreEqual(primaryLost, report.AnchorPublication is not null);
        Assert.IsTrue(File.Exists(Path.Combine(fixture.Root, "verification/final.json")));
        Assert.IsTrue(File.Exists(Path.Combine(fixture.Root, "verification/anchor-final.json")));
    }

    [TestMethod]
    public async Task ActualVerificationRetainsBusinessResultDespiteReceiptTransferFailure()
    {
        using var fixture = new Fixture("npm", official: true);
        fixture.WriteFinal(ReleaseFileState.Exact);
        fixture.WriteAnchorFinal(ReleaseFileState.Exact);
        Directory.CreateDirectory(Path.Combine(fixture.Root, "publication"));
        File.WriteAllText(Path.Combine(fixture.Root, "publication/publication.json"), "{");
        var environment = fixture.Environment();
        environment["PRIMARY_RECEIPT_OUTCOME"] = "failure";

        NativeCommandResult result = await Verify(fixture, environment);

        Assert.AreEqual(2, result.ExitCode, result.Stderr);
        Assert.Contains("Verification excluded failed receipt downloads", result.Stderr);
        ReleaseCompletionResult report = fixture.Report();
        Assert.AreEqual(ReleaseBusinessState.Satisfied, report.Outcome.State);
        Assert.AreEqual(false, report.MutationAttempted);
        Assert.IsNull(report.Publication);
    }

    [TestMethod]
    public async Task ActualBrowserStoreBuddyDryRunDescribesOnlyHypotheticalWrites()
    {
        using var fixture = new Fixture("chrome-web-store", dryRun: true);
        fixture.WriteFinal(ReleaseFileState.Unobserved);
        var environment = fixture.Environment();
        NativeCommandResult observed = await ObserveInitial(fixture, environment);
        Assert.IsTrue(observed.Succeeded, observed.Stderr);

        NativeCommandResult state = await Step(fixture, fixture.StateStep, environment,
            context.CancellationToken, job: "observe");

        Assert.IsTrue(state.Succeeded, state.Stderr);
        var simulation = ReleaseCli.Read(Path.Combine(fixture.Root, "observation/dry-run.json"),
            TransferJson.Default.ReleaseDryRun);
        Assert.IsTrue(simulation.Ready);
        Assert.IsNotEmpty(simulation.HypotheticalWrites);
        Assert.Contains("a dry run acquires no store credential",
            string.Join("; ", simulation.Reasons));
        Assert.IsFalse(Directory.Exists(Path.Combine(fixture.Root, "verification")));
    }

    private async Task<NativeCommandResult> ObserveInitial(Fixture fixture,
        Dictionary<string, string?> environment)
    {
        Directory.Delete(Path.Combine(fixture.Root, "observation"), true);
        NativeCommandResult received = await Step(fixture, "Receive transferred preparation",
            environment, context.CancellationToken, job: "observe");
        Assert.IsTrue(received.Succeeded, received.Stderr);
        NativeCommandResult primary = await Step(fixture, fixture.InitialStep, environment,
            context.CancellationToken, job: "observe");
        return primary.Succeeded && fixture.Ecosystem == "npm"
            ? await Step(fixture, "Observe GitHub Release anchor when Official", environment,
                context.CancellationToken, job: "observe")
            : primary;
    }

    private async Task<NativeCommandResult> Verify(Fixture fixture,
        Dictionary<string, string?> environment)
    {
        NativeCommandResult received = await Step(fixture, "Receive transferred preparation",
            environment, context.CancellationToken);
        Assert.IsTrue(received.Succeeded, received.Stderr);
        return await Step(fixture, fixture.VerifyStep, environment, context.CancellationToken);
    }

    private static Task<NativeCommandResult> Step(Fixture fixture, string name,
        Dictionary<string, string?> environment, CancellationToken token, string? job = null)
    {
        // Only the native destination observation is controlled. The actual workflow
        // shell and typed application phase/completion commands run unchanged.
        const string shim = """
            workflow_dotnet() {
              if [ "$3" = "$CONTROLLED_OPERATION" ]; then
                if [ -f "$RUNNER_TEMP/controlled-final.json" ]; then
                  cat "$RUNNER_TEMP/controlled-final.json"
                fi
                return "${CONTROLLED_OBSERVATION_EXIT:-0}"
              fi
              if [ "$3" = "observe-github-anchor" ]; then
                if [ -f "$RUNNER_TEMP/controlled-anchor-final.json" ]; then
                  cp "$RUNNER_TEMP/controlled-anchor-final.json" "$6"
                fi
                return "${CONTROLLED_ANCHOR_EXIT:-0}"
              fi
              case "$3" in observe-*|publish-*) return 99 ;; esac
              shift
              "$WORKFLOW_DOTNET" "$WORKFLOW_APP" "$@"
            }
            mise() {
              shift 4
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
        var selected = (YamlMappingNode)jobs.Children[job ?? fixture.Job];
        var steps = (YamlSequenceNode)selected.Children["steps"];
        YamlMappingNode step = steps.Children.Cast<YamlMappingNode>().Single(value =>
            value.Children.TryGetValue("name", out YamlNode? label) && label.ToString() == name);
        // Match the Actions bash invocation: the closest explicit "shell: bash" (step, job,
        // then workflow default) adds pipefail; an unset shell runs "bash -e".
        string? shell = Shell(step) ?? Shell(Defaults(selected)) ?? Shell(Defaults(document));
        Assert.IsTrue(shell is null or "bash", "Unsupported caller shell: " + shell);
        string[] flags = shell is null ? ["-e"] : ["--noprofile", "--norc", "-eo", "pipefail"];
        // A CRLF checkout of this source must not leak carriage returns into the shim.
        string script = (shim + "\n" + step.Children["run"]).ReplaceLineEndings("\n");
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
        private readonly BrowserStoreTarget? target;
        internal string Root { get; } = Directory.CreateTempSubdirectory("bash-caller-tests-")
            .FullName;
        internal PreparedRelease Prepared { get; }
        internal string? Store => target?.Store;
        internal string Ecosystem => ecosystem;
        internal string Workflow => ecosystem switch
        {
            "github-release" => "workflow-delivery-v3-github-release.yml",
            _ => "workflow-delivery-v3-" + ecosystem + "-release.yml",
        };
        internal string Job => ecosystem == "npm" ? "verify" : "verify-result";
        internal string InitialStep => ecosystem switch
        {
            "npm" => "Observe native package and original bytes",
            "python" => "Observe public destination and qualified originals",
            "github-release" =>
                "Observe tag, release and served assets against qualified originals",
            "browser-store" => "Observe store state and qualified originals",
            _ => "Observe selected native destination and qualified original",
        };
        internal string StateStep => ecosystem switch
        {
            "npm" => "Describe native state",
            "ruby" => "Expose required write and describe dry run",
            _ => "Expose required writes and describe dry run",
        };
        internal string VerifyStep => ecosystem switch
        {
            "npm" => "Verify registry coordinate and original bytes",
            "python" => "Verify both index files and original bytes in final public state",
            "github-release" => "Read back every published asset against its original bytes",
            "browser-store" => "Verify the store-reported version state",
            _ => "Verify native source original and complete final native state",
        };

        private Fixture(Fixture received)
        {
            ecosystem = received.ecosystem;
            official = received.official;
            Prepared = received.Prepared;
            target = received.target;
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

        internal Fixture(string ecosystem, bool official = false, bool dryRun = false)
        {
            if (ecosystem is "chrome-web-store" or "firefox-amo" or "edge-addons")
            {
                target = new(ecosystem, "controlled-item",
                    ecosystem == "chrome-web-store" ? "controlled-publisher" : null);
                ecosystem = "browser-store";
            }
            this.ecosystem = ecosystem;
            this.official = official;
            bool github = ecosystem == "github-release";
            string unit = target is not null ? BrowserStorePreparation.DefaultUnit.Id
                : github ? "image-occlusion-editor"
                : official ? ecosystem switch
                {
                    "npm" => "hexo-renderer-asciidoc",
                    "python" => "nbgv-python",
                    _ => "asciidoctor-latexmath",
                }
                : "hcoona-release-smoke-" + ecosystem;
            string declaration = target is not null
                ? BrowserStorePreparation.DefaultUnit.Declaration
                : github ? ReleaseUnitCatalog.GithubRelease(unit).Declaration
                : official ? "src/public/lib/" + unit + "/workflow-delivery.release-unit.yml"
                : Products + unit + "/workflow-delivery.release-unit.yml";
            var subject = new ReleaseSubject(new string('a', 40),
                official ? "refs/heads/main" : "refs/heads/development", unit, declaration,
                official ? "official" : "buddy",
                ecosystem switch
                {
                    "npm" => official ? "npmjs" : "github-packages-npm",
                    "python" => official ? "pypi" : "testpypi",
                    "github-release" => official ? GithubReleaseUnit.OfficialDestination
                        : GithubReleaseUnit.BuddyDestination,
                    "browser-store" => target!.Store,
                    _ => official ? "rubygems" : "github-packages-ruby",
                }, dryRun);
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
                "browser-store" => [.. BrowserExtensionPackage.Archives(unit, "1.0.0")
                    .Where(archive => BrowserStorePreparation.StoreOutputs(target!.Store)
                        .Contains(archive.Output.Id))
                    .Select(archive => Original("browser-extensions", archive.Output.Id,
                        archive.Output.Kind, unit, "originals/" + archive.Name))],
                _ => [Original("gem-build", "gem-output", "ruby-gem", unit, "original.gem")],
            };
            string[] required = ecosystem switch
            {
                "npm" => ["consumer"],
                "python" => PythonPreparation.RequiredFor(ReleaseUnitCatalog.Python(unit)),
                "github-release" => GithubReleasePreparation.Required,
                "browser-store" =>
                    ["native-input", "build", "contents", "consumer", "original-bytes"],
                _ => RubyPreparation.RequiredFor(ReleaseUnitCatalog.Ruby(unit)),
            };
            const string notes = "### Added\n\n- Official notes.";
            Prepared = new(subject, files, required, required.Select(id =>
                new ReleaseCheck(id, ReleaseCheckStatus.Passed)).ToArray(),
                Anchor: official && !github
                    ? DestinationBindings.GithubAnchor(subject, files, notes, "1.0.0") : null,
                ReleaseNotes: official ? notes : null);
            var native = new Dictionary<string, string>
            {
                ["name"] = unit,
                ["version"] = "1.0.0",
                ["commit"] = subject.Revision,
            };
            if (official && (github || ecosystem == "ruby" ||
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
                ecosystem == "npm" ? new(StringComparer.Ordinal) : native, commands, [], target),
                TransferJson.Default.ReleasePreparationResult);
            // The initial observation is exact, so completion requires no publication result.
            if (target is null)
                WriteObservation("observation/observation.json", ReleaseFileState.Exact);
            if (Prepared.Anchor is not null)
                WriteAnchorObservation("observation/anchor-observation.json",
                    ReleaseFileState.Exact);
            if (ecosystem == "python")
                Write(Path.Combine(Root, "index-tool-result.json"), new PythonIndexToolResult(
                    "controlled-python", [], []),
                    DestinationToolJson.Default.PythonIndexToolResult);
        }

        internal Dictionary<string, string?> Environment() => new()
        {
            ["RUNNER_TEMP"] = Root,
            ["DESTINATION"] = ecosystem switch
            {
                "python" => official ? "pypi" : "testpypi",
                "npm" => official ? "npmjs" : "github-packages",
                "ruby" => official ? "rubygems" : "github-packages",
                _ => Prepared.Subject.Destination,
            },
            ["CHANNEL"] = Prepared.Subject.Channel,
            ["STORE_ITEM"] = target?.Item,
            ["STORE_PUBLISHER"] = target?.Publisher,
            ["EDGE_CLEARANCE"] = "unresolved",
            ["DRY_RUN"] = Prepared.Subject.DryRun ? "true" : "false",
            ["UNIT"] = Prepared.Subject.Unit,
            ["GITHUB_SHA"] = Prepared.Subject.Revision,
            ["GITHUB_REF"] = Prepared.Subject.SelectedRef,
            ["GITHUB_RUN_ID"] = "123",
            ["GITHUB_RUN_ATTEMPT"] = "1",
            ["GITHUB_OUTPUT"] = Path.Combine(Root, "output"),
            ["GITHUB_STEP_SUMMARY"] = Path.Combine(Root, "summary"),
            ["PRIMARY_RECEIPT_OUTCOME"] = "skipped",
            ["ANCHOR_RECEIPT_OUTCOME"] = "skipped",
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

        internal string State(string? revision = null)
        {
            var environment = Environment();
            string output = Path.Combine(Root, "output");
            environment["GITHUB_OUTPUT"] = output;
            if (revision is not null) environment["GITHUB_SHA"] = revision;
            Assert.AreEqual(0, ReleaseJobInputs.Run(["release", "job-inputs", ecosystem, "state"],
                name => environment.GetValueOrDefault(name)));
            return File.ReadAllText(output);
        }

        internal void WriteAnchorFinal(ReleaseFileState state) =>
            WriteAnchorObservation("controlled-anchor-final.json", state);

        internal ReleaseOutcome Outcome() => Report().Outcome;

        internal ReleaseCompletionResult Report() => ReleaseCli.Read(
            Path.Combine(Root, "verification/outcome.json"),
            TransferJson.Default.ReleaseCompletionResult);

        internal bool AnchorPermitted(string receipt = "-")
        {
            using var output = new StringWriter();
            Assert.AreEqual(0, ReleaseCli.Run(["release", "may-publish-github-anchor",
                Path.Combine(Root, "prepared/preparation.json"),
                Path.Combine(Root, "observation/observation.json"),
                Path.Combine(Root, "observation/anchor-observation.json"), receipt], output));
            return bool.Parse(output.ToString());
        }

        internal string WriteReceipt(bool anchor)
        {
            string path = Path.Combine(Root, anchor ? "anchor-publication" : "publication",
                "publication.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            ReleaseObservationResult before = ReleaseCli.Read(Path.Combine(Root, "observation",
                anchor ? "anchor-observation.json" : "observation.json"),
                TransferJson.Default.ReleaseObservationResult);
            ReleaseWriteStep[] steps = !anchor && Store is not null
                ? [ReleaseWriteStep.FromStore("upload", true, "Upload acknowledged."),
                    ReleaseWriteStep.FromStore("publish", true, "Submission acknowledged.")]
                : [.. (anchor ? Prepared.Anchor!.Assets : Prepared.Files).Select(file =>
                    ReleaseWriteStep.FromUpload(new(file.Output,
                        new(NativeTermination.Exited, 0, "controlled upload", "", 0.1, null))))];
            Write(path, new(Prepared.Subject, before, steps, null, []),
                TransferJson.Default.ReleasePublicationResult);
            return path;
        }

        internal void WriteAnchorObservation(string name, ReleaseFileState state)
        {
            var obs = new ReleaseObservation(Prepared.Subject,
                Prepared.Anchor!.Assets.Select(file => new ReleaseFileObservation(file.Output,
                    file.Name, file.Version, state, state == ReleaseFileState.Exact ? file.Size
                        : null, state == ReleaseFileState.Exact ? file.Sha256 : null)).ToArray(),
                [GithubReleaseDestination.Check],
                [new(GithubReleaseDestination.Check, ReleaseCheckStatus.Passed)]);
            Write(Path.Combine(Root, name), new ReleaseObservationResult(obs, [],
                Container: ContainerPresence.Present),
                TransferJson.Default.ReleaseObservationResult);
        }

        internal void WriteObservation(
            string name,
            ReleaseFileState state,
            ReleaseFileState? second = null)
        {
            string path = Path.Combine(Root, name);
            if (target is not null)
            {
                StoreState storeState = state switch
                {
                    ReleaseFileState.Absent => StoreState.Absent,
                    ReleaseFileState.Accepted => StoreState.Published,
                    ReleaseFileState.Conflict => StoreState.Conflict,
                    ReleaseFileState.Unobserved => StoreState.Unobserved,
                    _ => StoreState.Unknown,
                };
                Write(path,
                    StoreHttp.Observation(Prepared.Subject, target, "1.0.0", storeState, [], []),
                    TransferJson.Default.ReleaseObservationResult);
                return;
            }
            ReleaseObservation Observation(string check) => new(Prepared.Subject,
                Prepared.Files.Select((file, i) =>
                {
                    ReleaseFileState actual = i == 0 ? state : second ?? state;
                    return new ReleaseFileObservation(file.Output, file.Name, file.Version, actual,
                        actual == ReleaseFileState.Exact ? file.Size : null,
                        actual == ReleaseFileState.Exact ? file.Sha256 : null);
                }).ToArray(), [check],
                [new(check, ReleaseCheckStatus.Passed)]);
            ReleaseObservationResult observed = ecosystem switch
            {
                "npm" => new(Observation("native-package-control"), []),
                "python" => new(Observation("public-index-files"), []),
                "github-release" => new(Observation(GithubReleaseDestination.Check), [],
                    Container: ContainerPresence.Present),
                _ => new(Observation("native-source-original"), []),
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
