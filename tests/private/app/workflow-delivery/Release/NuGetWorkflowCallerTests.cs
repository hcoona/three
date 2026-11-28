using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Release;
using YamlDotNet.RepresentationModel;

namespace WorkflowDelivery.Tests.Release;

[TestClass]
public sealed class NuGetWorkflowCallerTests(TestContext context)
{
    [TestMethod]
    [DataRow("Absent", "true")]
    [DataRow("Exact", "false")]
    public async Task ActualStateStepConsumesSerializedObservation(string state, string needed)
    {
        var fixture = new NuGetReleaseTestFixture();
        await FixtureCleanup.RunAsync(async () =>
        {
            Directory.CreateDirectory(Path.Combine(fixture.Root, "prepared"));
            fixture.Write("prepared/preparation.json", fixture.Preparation,
                TransferJson.Default.ReleasePreparationResult);
            Directory.CreateDirectory(Path.Combine(fixture.Root, "observation"));
            fixture.Write("observation/observation.json", fixture.Observe(
                Enum.Parse<ReleaseFileState>(state)),
                TransferJson.Default.NuGetObservationResult);
            var environment = Environment(fixture);
            environment["GITHUB_OUTPUT"] = Path.Combine(fixture.Root, "output");

            NativeCommandResult result = await Step(fixture, "observe",
                "Describe native state and dry-run writes", environment);

            Assert.IsTrue(result.Succeeded, result.Stderr);
            Assert.AreEqual("write-needed=" + needed + "\n", File.ReadAllText(
                environment["GITHUB_OUTPUT"]!));
        }, fixture.Dispose);
    }

    [TestMethod]
    [DataRow("Exact", 0, "Satisfied")]
    [DataRow("Conflict", 1, "Unsatisfied")]
    public async Task ActualVerificationStepRetainsTypedRegistryOutcome(string state,
        int expectedExit, string expectedState)
    {
        string[] phases = ["prepared", "observation"];
        var fixture = new NuGetReleaseTestFixture();
        await FixtureCleanup.RunAsync(async () =>
        {
            foreach (string phase in phases)
                Directory.CreateDirectory(Path.Combine(fixture.Root, phase));
            fixture.Write("prepared/preparation.json", fixture.Preparation,
                TransferJson.Default.NuGetPreparationResult);
            fixture.Write("observation/observation.json", fixture.Observe(ReleaseFileState.Exact),
                TransferJson.Default.NuGetObservationResult);
            fixture.Write("controlled-final.json", fixture.Observe(
                Enum.Parse<ReleaseFileState>(state)),
                TransferJson.Default.NuGetObservationResult);
            var environment = Environment(fixture);
            environment["CONTROLLED_OBSERVATION"] = Path.Combine(fixture.Root,
                "controlled-final.json");
            // The actual observe command exits 1 for a handled conflict or unknown state; both
            // take the same caller branch, and ReleaseStateTests maps each typed state.
            environment["CONTROLLED_OBSERVATION_EXIT"] = state == "Exact" ? "0" : "1";
            Assert.IsTrue((await Step(fixture, "verify-result", "Receive transferred preparation",
                environment)).Succeeded);

            NativeCommandResult result = await Step(fixture, "verify-result",
                "Verify registry coordinate and original bytes", environment);

            Assert.AreEqual(expectedExit, result.ExitCode, result.Stderr);
            NuGetCompletionResult outcome = JsonSerializer.Deserialize(File.ReadAllText(
                Path.Combine(fixture.Root, "verification/outcome.json")),
                TransferJson.Default.NuGetCompletionResult)!;
            Assert.AreEqual(Enum.Parse<ReleaseBusinessState>(expectedState), outcome.Outcome.State);
            Assert.AreEqual(fixture.Prepared.Subject, outcome.Outcome.Subject);
            Assert.IsFalse(outcome.MutationAttempted);
        }, fixture.Dispose);
    }

    [TestMethod]
    public async Task ActualVerificationStepStopsOnUnhandledObservationFailure()
    {
        string[] phases = ["prepared", "observation"];
        var fixture = new NuGetReleaseTestFixture();
        await FixtureCleanup.RunAsync(async () =>
        {
            foreach (string phase in phases)
                Directory.CreateDirectory(Path.Combine(fixture.Root, phase));
            fixture.Write("prepared/preparation.json", fixture.Preparation,
                TransferJson.Default.NuGetPreparationResult);
            fixture.Write("observation/observation.json", fixture.Observe(ReleaseFileState.Exact),
                TransferJson.Default.NuGetObservationResult);
            var environment = Environment(fixture);
            // Exit 2 is an unhandled native failure, not a typed destination state.
            environment["CONTROLLED_OBSERVATION_EXIT"] = "2";
            Assert.IsTrue((await Step(fixture, "verify-result", "Receive transferred preparation",
                environment)).Succeeded);

            NativeCommandResult result = await Step(fixture, "verify-result",
                "Verify registry coordinate and original bytes", environment);

            Assert.AreEqual(2, result.ExitCode, result.Stderr);
            Assert.IsFalse(File.Exists(Path.Combine(fixture.Root, "verification/outcome.json")));
        }, fixture.Dispose);
    }

    [TestMethod]
    [DataRow(false, "Conflict", "Exact", "Unsatisfied")]
    [DataRow(true, "Unknown", "Available", "Unknown")]
    public async Task ActualPowerShellPreflightRoutesTypedRefusalToReadOnlyCompletion(bool official,
        string initialState, string finalState, string expectedState)
    {
        var fixture = new NuGetReleaseTestFixture(official: official);
        await FixtureCleanup.RunAsync(async () =>
        {
            var environment = Environment(fixture);
            environment["CONTROLLED_OBSERVATION"] = fixture.Write("controlled-initial.json",
                fixture.Observe(Enum.Parse<ReleaseFileState>(initialState)),
                TransferJson.Default.ReleaseObservationResult);
            environment["CONTROLLED_OBSERVATION_EXIT"] = "1";
            NativeCommandResult observed = await ObserveInitial(fixture, environment);
            Assert.IsTrue(observed.Succeeded, observed.Stderr);
            Assert.AreEqual(File.ReadAllText(environment["CONTROLLED_OBSERVATION"]!).Trim(),
                File.ReadAllText(Path.Combine(fixture.Root, "observation/observation.json"))
                    .Trim());
            NativeCommandResult state = await Step(fixture, "observe",
                "Describe native state and dry-run writes", environment);
            Assert.IsTrue(state.Succeeded, state.Stderr);
            Assert.AreEqual("write-needed=false\n",
                File.ReadAllText(environment["GITHUB_OUTPUT"]!));

            environment["CONTROLLED_OBSERVATION"] = fixture.Write("controlled-final.json",
                fixture.Observe(Enum.Parse<ReleaseFileState>(finalState)),
                TransferJson.Default.ReleaseObservationResult);
            environment["CONTROLLED_OBSERVATION_EXIT"] = "0";
            NativeCommandResult received = await Step(fixture, "verify-result",
                "Receive transferred preparation", environment);
            Assert.IsTrue(received.Succeeded, received.Stderr);
            NativeCommandResult result = await Step(fixture, "verify-result",
                "Verify registry coordinate and original bytes", environment);

            Assert.AreEqual(1, result.ExitCode, result.Stderr);
            ReleaseCompletionResult report = ReleaseCli.Read(
                Path.Combine(fixture.Root, "verification/outcome.json"),
                TransferJson.Default.ReleaseCompletionResult);
            Assert.AreEqual(Enum.Parse<ReleaseBusinessState>(expectedState), report.Outcome.State);
            Assert.AreEqual(false, report.MutationAttempted);
            Assert.IsNull(report.Publication);
        }, fixture.Dispose);
    }

    [TestMethod]
    [DataRow("2", false, true)]
    [DataRow("1", false, false)]
    [DataRow("1", false, true)]
    [DataRow("1", true, true)]
    public async Task ActualPowerShellPreflightDoesNotRouteInvalidOrDryObservation(
        string exit, bool dryRun, bool hasObservation)
    {
        var fixture = new NuGetReleaseTestFixture(dryRun: dryRun);
        await FixtureCleanup.RunAsync(async () =>
        {
            var environment = Environment(fixture);
            environment["CONTROLLED_OBSERVATION"] = hasObservation
                ? fixture.Write("controlled-initial.json", fixture.Observe(dryRun
                    ? ReleaseFileState.Conflict : ReleaseFileState.Absent),
                    TransferJson.Default.ReleaseObservationResult)
                : Path.Combine(fixture.Root, "missing-initial.json");
            environment["CONTROLLED_OBSERVATION_EXIT"] = exit;

            NativeCommandResult observed = await ObserveInitial(fixture, environment);

            Assert.AreEqual(exit == "2" ? 2 : dryRun ? 1 : 0, observed.ExitCode, observed.Stderr);
            if (observed.Succeeded)
            {
                NativeCommandResult state = await Step(fixture, "observe",
                    "Describe native state and dry-run writes", environment);
                Assert.AreEqual(2, state.ExitCode, state.Stderr);
            }
            Assert.IsFalse(File.Exists(environment["GITHUB_OUTPUT"]!));
            Assert.IsFalse(Directory.Exists(Path.Combine(fixture.Root, "verification")));
        }, fixture.Dispose);
    }

    private async Task<NativeCommandResult> ObserveInitial(NuGetReleaseTestFixture fixture,
        Dictionary<string, string?> environment)
    {
        Directory.CreateDirectory(Path.Combine(fixture.Root, "prepared"));
        fixture.Write("prepared/preparation.json", fixture.Preparation,
            TransferJson.Default.ReleasePreparationResult);
        NativeCommandResult received = await Step(fixture, "observe",
            "Receive transferred preparation", environment);
        Assert.IsTrue(received.Succeeded, received.Stderr);
        return await Step(fixture, "observe", "Observe native destination and original bytes",
            environment);
    }

    [TestMethod]
    public async Task BodyAndCleanupFailuresRetainOriginalExceptionsAndStacks()
    {
        var bodyFailure = new AssertFailedException("Original body assertion.");
        var cleanupFailure = new IOException("Controlled cleanup failure.");
        int disposals = 0;
        async Task FailBody()
        {
            await Task.Yield();
            throw bodyFailure;
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        void FailCleanup()
        {
            disposals++;
            throw cleanupFailure;
        }

        AggregateException failure = await Assert.ThrowsExactlyAsync<AggregateException>(
            () => FixtureCleanup.RunAsync(FailBody, FailCleanup));

        Assert.HasCount(2, failure.InnerExceptions);
        Assert.AreSame(bodyFailure, failure.InnerExceptions[0]);
        Assert.AreSame(cleanupFailure, failure.InnerExceptions[1]);
        Assert.Contains(nameof(FailBody), bodyFailure.StackTrace ?? "");
        Assert.Contains(nameof(FailCleanup), cleanupFailure.StackTrace ?? "");
        Assert.AreEqual(1, disposals);
    }

    [TestMethod]
    public async Task CleanupFailureAfterSuccessfulBodyRemainsFailure()
    {
        var cleanupFailure = new IOException("Controlled cleanup failure.");
        int disposals = 0;
        [MethodImpl(MethodImplOptions.NoInlining)]
        void FailCleanup()
        {
            disposals++;
            throw cleanupFailure;
        }

        IOException failure = await Assert.ThrowsExactlyAsync<IOException>(
            () => FixtureCleanup.RunAsync(() => Task.CompletedTask, FailCleanup));

        Assert.AreSame(cleanupFailure, failure);
        Assert.Contains(nameof(FailCleanup), failure.StackTrace ?? "");
        Assert.AreEqual(1, disposals);
    }

    private static Dictionary<string, string?> Environment(NuGetReleaseTestFixture fixture) => new()
    {
        ["RUNNER_TEMP"] = fixture.Root,
        ["DESTINATION"] = fixture.Prepared.Subject.Destination == "nugetorg"
            ? "nugetorg" : "github-packages",
        ["UNIT"] = fixture.Prepared.Subject.Unit,
        ["DRY_RUN"] = fixture.Prepared.Subject.DryRun ? "true" : "false",
        ["GITHUB_SHA"] = fixture.Prepared.Subject.Revision,
        ["GITHUB_REF"] = fixture.Prepared.Subject.SelectedRef,
        ["GITHUB_RUN_ID"] = "123",
        ["GITHUB_RUN_ATTEMPT"] = "1",
        ["GITHUB_OUTPUT"] = Path.Combine(fixture.Root, "output"),
        ["WORKFLOW_APP"] = typeof(Program).Assembly.Location,
        ["WORKFLOW_DOTNET"] = System.Environment.GetEnvironmentVariable("DOTNET_HOST_PATH")
            ?? "dotnet",
    };

    private async Task<NativeCommandResult> Step(NuGetReleaseTestFixture fixture, string job,
        string name, Dictionary<string, string?> environment)
    {
        // Only the native destination observation is controlled. The actual workflow
        // shell and typed application state/phase/completion commands run unchanged.
        const string shim = """
            $ErrorActionPreference = 'Stop'
            [Console]::Error.WriteLine('NuGet caller: script entered.')
            function mise {
              $operation = @($args | Select-Object -Skip 4)
              switch ($operation[1]) {
                'observe-nuget' {
                  $observed = "$env:RUNNER_TEMP/observation/observation.json"
                  if ($env:CONTROLLED_OBSERVATION) { $observed = $env:CONTROLLED_OBSERVATION }
                  if (Test-Path -LiteralPath $observed -PathType Leaf) {
                    Get-Content -LiteralPath $observed -Raw
                  }
                  $global:LASTEXITCODE = 0
                  if ($env:CONTROLLED_OBSERVATION_EXIT) {
                    $global:LASTEXITCODE = [int]$env:CONTROLLED_OBSERVATION_EXIT
                  }
                }
                default {
                  [Console]::Error.WriteLine('NuGet caller: invocation attempted.')
                  & $env:WORKFLOW_DOTNET $env:WORKFLOW_APP @operation
                  $exitCode = $LASTEXITCODE
                  [Console]::Error.WriteLine("NuGet caller: invocation returned; exit $exitCode.")
                  $global:LASTEXITCODE = $exitCode
                }
              }
            }
            """;
        // The workflow is an embedded resource so that native CI scope selects this test
        // whenever the caller workflow changes.
        var yaml = new YamlStream();
        using Stream resource = typeof(NuGetWorkflowCallerTests).Assembly
            .GetManifestResourceStream("workflow-delivery-v3-nuget-release.yml")
            ?? throw new AssertFailedException("Missing embedded caller workflow.");
        using var reader = new StreamReader(resource);
        yaml.Load(reader);
        var document = (YamlMappingNode)yaml.Documents[0].RootNode;
        var jobs = (YamlMappingNode)document.Children["jobs"];
        var selected = (YamlMappingNode)jobs.Children[job];
        var steps = (YamlSequenceNode)selected.Children["steps"];
        YamlMappingNode step = steps.Children.Cast<YamlMappingNode>().Single(value =>
            value.Children.TryGetValue("name", out YamlNode? label) && label.ToString() == name);
        string script = shim + "\n" + step.Children["run"]
            + "\nif ((Test-Path -LiteralPath variable:\\LASTEXITCODE)) { exit $LASTEXITCODE }";
        NativeCommandResult result = await NativeProcess.ExecuteAsync(new("pwsh", fixture.Root,
            ["-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand",
                Convert.ToBase64String(Encoding.Unicode.GetBytes(script))], 30, environment),
            context.CancellationToken);
        // Fixture disposal can otherwise replace the original command failure.
        context.WriteLine($"Workflow step '{job}/{name}': {result.Termination}, "
            + $"exit {result.ExitCode}, duration {result.DurationSeconds}s; error: {result.Error}");
        context.WriteLine($"stdout (last 4096 characters): "
            + result.Stdout[^Math.Min(result.Stdout.Length, 4096)..]);
        context.WriteLine($"stderr (last 4096 characters): "
            + result.Stderr[^Math.Min(result.Stderr.Length, 4096)..]);
        return result;
    }
}
