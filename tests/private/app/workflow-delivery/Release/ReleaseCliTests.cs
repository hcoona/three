using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.Destinations;
using WorkflowDelivery.Ecosystems;
using WorkflowDelivery.Release;

namespace WorkflowDelivery.Tests.Release;

[TestClass]
public sealed class ReleaseCliTests
{
    [TestMethod]
    [DataRow("github-npm", "Exact", "Conflict", 1, "Unsatisfied")]
    [DataRow("npmjs", "Absent", "Exact", 1, "Unknown")]
    public void ActualCompletionBindsTheRegistryTransfer(string registry, string initialState,
        string finalState, int expectedExit, string expectedState)
    {
        using var files = new Files();
        bool github = registry == "github-npm";
        PreparedRelease prepared = files.Prepared with
        {
            Subject = files.Prepared.Subject with
            {
                Channel = github ? "buddy" : "official",
                Destination = github ? "github-packages-npm" : "npmjs",
                DryRun = false
            }
        };
        ReleaseFile file = prepared.Files[0];
        string preparation = files.Write("prepared.json", JsonSerializer.Serialize(
            new ReleasePreparationResult(prepared, new(StringComparer.Ordinal), [], []),
            TransferJson.Default.ReleasePreparationResult));
        string initial = WriteObservation("initial.json", initialState);
        string final = WriteObservation("final.json", finalState);
        using var output = new StringWriter();
        using var error = new StringWriter();

        Assert.AreEqual(expectedExit, Program.Run(["release", "complete-" + registry, preparation,
            initial, final, "-"], output, error), error.ToString());

        ReleaseOutcome result = JsonSerializer.Deserialize(output.ToString(),
            TransferJson.Default.ReleaseCompletionResult)!.Outcome;
        Assert.AreEqual(prepared.Subject, result.Subject);
        Assert.AreEqual(Enum.Parse<ReleaseBusinessState>(expectedState), result.State);
        Assert.IsEmpty(result.Uploads);
        Assert.AreEqual("", error.ToString());

        string WriteObservation(string path, string stateName)
        {
            ReleaseFileState state = Enum.Parse<ReleaseFileState>(stateName);
            string check = github ? "visibility" : "metadata";
            ReleaseObservation observed = new(prepared.Subject,
                [new(file.Output, file.Name, file.Version, state,
                    state == ReleaseFileState.Exact ? file.Size : null,
                    state == ReleaseFileState.Exact ? file.Sha256 : null)],
                [check], [new(check, ReleaseCheckStatus.Passed)]);
            return files.Write(path, JsonSerializer.Serialize(new ReleaseObservationResult(
                observed, []), TransferJson.Default.ReleaseObservationResult));
        }
    }

    [TestMethod]
    [DataRow("npmjs")]
    [DataRow("github-npm")]
    public void CompleteNpmValidatesPublicationBeforeAndRetainsRefusalDiagnostics(string registry)
    {
        using var files = new Files();
        bool github = registry == "github-npm";
        PreparedRelease prepared = files.Prepared with
        {
            Subject = files.Prepared.Subject with
            {
                SelectedRef = "refs/heads/main",
                Channel = github ? "buddy" : "official",
                Destination = github ? "github-packages-npm" : "npmjs",
                DryRun = false
            }
        };
        ReleaseFile file = prepared.Files[0];
        string preparation = files.Write("prepared.json", JsonSerializer.Serialize(
            new ReleasePreparationResult(prepared, new(StringComparer.Ordinal), [], []),
            TransferJson.Default.ReleasePreparationResult));
        ReleaseObservation absent = Observe(ReleaseFileState.Absent);
        ReleaseObservation before = Observe(ReleaseFileState.Conflict);
        ReleaseObservation exact = Observe(ReleaseFileState.Exact);
        string initial = WriteObservation("initial.json", absent);
        string finalAbsent = WriteObservation("final-absent.json", absent);
        string finalExact = WriteObservation("final-exact.json", exact);
        const string blocked = "Refreshed registry state does not permit publication.";
        string publication = files.Write("published.json", JsonSerializer.Serialize(
            new ReleasePublicationResult(prepared.Subject, new(before, []), [],
                new(before, []), [blocked]), TransferJson.Default.ReleasePublicationResult));
        using var output = new StringWriter();
        using var error = new StringWriter();

        Assert.AreEqual(1, Program.Run(["release", "complete-" + registry, preparation,
            initial, finalAbsent, publication], output, error), error.ToString());
        ReleaseOutcome refused = JsonSerializer.Deserialize(output.ToString(),
            TransferJson.Default.ReleaseCompletionResult)!.Outcome;
        Assert.AreEqual(ReleaseBusinessState.Unsatisfied, refused.State);
        Assert.IsEmpty(refused.Uploads);
        Assert.Contains(blocked, refused.Reasons);

        output.GetStringBuilder().Clear();
        Assert.AreEqual(0, Program.Run(["release", "complete-" + registry, preparation,
            initial, finalExact, publication], output, error), error.ToString());
        ReleaseOutcome observed = JsonSerializer.Deserialize(output.ToString(),
            TransferJson.Default.ReleaseCompletionResult)!.Outcome;
        Assert.AreEqual(ReleaseBusinessState.Satisfied, observed.State);
        Assert.IsEmpty(observed.Uploads);

        var badUpload = new ReleaseUpload(file.Output,
            new(WorkflowDelivery.Platform.NativeTermination.Exited, 0, "", "", 0.1, null));
        string illegalWrite = files.Write("illegal-write.json", JsonSerializer.Serialize(
            new ReleasePublicationResult(prepared.Subject, new(before, []),
                [ReleaseWriteStep.FromUpload(badUpload)], new(exact, []), []),
            TransferJson.Default.ReleasePublicationResult));
        output.GetStringBuilder().Clear();
        Assert.AreEqual(2, Program.Run(["release", "complete-" + registry, preparation,
            initial, finalExact, illegalWrite], output, error));
        Assert.Contains("Unsupported write from non-proceeding state.", error.ToString());

        output.GetStringBuilder().Clear();
        error.GetStringBuilder().Clear();
        Assert.AreEqual(2, Program.Run(["release", "complete-" + registry, preparation,
            finalExact, finalExact, publication], output, error));
        Assert.AreEqual("", output.ToString());
        Assert.Contains("Mismatched or unnecessary publication result.", error.ToString());

        ReleaseObservation Observe(ReleaseFileState state)
        {
            string check = github ? "visibility" : "metadata";
            return new(prepared.Subject,
                [new(file.Output, file.Name, file.Version, state,
                    state == ReleaseFileState.Exact ? file.Size : null,
                    state == ReleaseFileState.Exact ? file.Sha256 : null)],
                [check], [new(check, ReleaseCheckStatus.Passed)]);
        }

        string WriteObservation(string path, ReleaseObservation observation) =>
            files.Write(path, JsonSerializer.Serialize(new ReleaseObservationResult(
                observation, []), TransferJson.Default.ReleaseObservationResult));
    }

    [TestMethod]
    public void DryRunCommandReportsTheMissingPackageAsHypothetical()
    {
        using var files = new Files();
        PreparedRelease prepared = files.Prepared;
        ReleaseFile file = prepared.Files[0];
        var observation = new ReleaseObservationResult(new(prepared.Subject,
            [new(file.Output, file.Name, file.Version, ReleaseFileState.Absent, null, null)],
            ["visibility"], [new("visibility", ReleaseCheckStatus.Passed)]), []);
        string observed = files.Write("observed.json", JsonSerializer.Serialize(observation,
            TransferJson.Default.ReleaseObservationResult));
        using var output = new StringWriter();
        using var error = new StringWriter();

        Assert.AreEqual(0, Program.Run(["release", "describe-npmjs", files.Preparation,
            observed], output, error), error.ToString());

        ReleaseDryRun result = JsonSerializer.Deserialize(output.ToString(),
            TransferJson.Default.ReleaseDryRun)!;
        Assert.IsTrue(result.Ready);
        Assert.AreEqual(file.Output, Assert.ContainsSingle(result.HypotheticalWrites));
        Assert.AreEqual(prepared.Subject, result.Subject);
        Assert.IsTrue(result.Subject.DryRun);
        Assert.AreEqual("", error.ToString());
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void ObserveRejectsMissingOrSubstitutedOriginal(bool missing)
    {
        using var files = new Files();
        string archive = Path.Combine(files.Root, files.Prepared.Files[0].Path);
        if (missing) File.Delete(archive);
        else File.WriteAllBytes(archive, [4, 5, 6]);
        using var output = new StringWriter();
        using var error = new StringWriter();

        Assert.AreEqual(2, Program.Run(["release", "observe-npmjs", files.Preparation,
            files.Root], output, error));

        Assert.AreEqual("", output.ToString());
        Assert.IsNotEmpty(error.ToString());
    }

    [TestMethod]
    [DataRow("publish-github-npm")]
    [DataRow("publish-npmjs")]
    public void ActualPublisherRejectsSubstitutedOriginalBeforeOtherInputs(string command)
    {
        using var files = new Files(github: command == "publish-github-npm");
        string archive = Path.Combine(files.Root, files.Prepared.Files[0].Path);
        File.WriteAllBytes(archive, [4, 5, 6]);
        using var output = new StringWriter();
        using var error = new StringWriter();

        Assert.AreEqual(2, Program.Run(["release", command, files.Preparation, files.Root,
            "unused-scratch", "unused-toolchain"], output, error));

        Assert.AreEqual("", output.ToString());
        Assert.Contains("Transferred original differs from preparation.", error.ToString());
    }

    [TestMethod]
    [DataRow("""{"checkout":"product","scratch":"output"}""")]
    [DataRow("""{"subject":null,"checkout":"product","scratch":"output"}""")]
    public void PrepareRejectsMalformedSubjectBeforeNativeExecution(string request)
    {
        using var files = new Files();
        AssertPreparationRejected(files.Write("request.json", request));
    }

    [TestMethod]
    [DataRow("\"scratch\":\"replaced\",", "Duplicate")]
    [DataRow("\"approvalMirror\":true,", "approvalMirror")]
    public void PrepareRejectsAdditionalTransferFields(string property, string diagnostic)
    {
        using var files = new Files();
        string request = JsonSerializer.Serialize(
            new ReleasePreparationRequest(files.Prepared.Subject, files.Root, files.Root),
            TransferJson.Default.ReleasePreparationRequest);
        AssertPreparationRejected(files.Write("request.json", request.Insert(1, property)),
            diagnostic);
    }

    private static void AssertPreparationRejected(string path, string? diagnostic = null)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        Assert.AreEqual(2, Program.Run(["release", "prepare-npm", path], output, error));

        Assert.AreEqual("", output.ToString());
        Assert.IsNotEmpty(error.ToString());
        if (diagnostic is not null) Assert.Contains(diagnostic, error.ToString());
    }

    [TestMethod]
    public void FailedPreparationKeepsNativeDiagnosticsAndNonzeroExit()
    {
        using var files = new Files();
        string request = files.Write("request.json", JsonSerializer.Serialize(
            new ReleasePreparationRequest(files.Prepared.Subject, files.Root, files.Root),
            TransferJson.Default.ReleasePreparationRequest));
        var failed = new ReleasePreparationResult(files.Prepared with
        { Files = [], Checks = [new("consumer", ReleaseCheckStatus.Failed)] },
            new(StringComparer.Ordinal), [],
            ["native consumer failed"]);
        using var output = new StringWriter();

        Assert.AreEqual(1, ReleaseCli.Run(["release", "prepare-npm", request], output,
            new(Prepare: (_, _) => Task.FromResult(failed))));

        ReleasePreparationResult result = JsonSerializer.Deserialize(output.ToString(),
            TransferJson.Default.ReleasePreparationResult)!;
        Assert.AreEqual("native consumer failed", Assert.ContainsSingle(result.Errors));
        Assert.AreEqual(ReleaseCheckStatus.Failed,
            Assert.ContainsSingle(result.Prepared.Checks).Status);
        Assert.IsEmpty(result.Prepared.Files);
    }

    [TestMethod]
    [DataRow("unknown-unit", "official", "npmjs", "Unsupported npm Release unit.")]
    [DataRow(NpmReleaseUnits.Hexo, "buddy", "npmjs",
        "Unsupported selected npm destination and mode.")]
    [DataRow(NpmReleaseUnits.Hexo, "official", "github-packages-npm",
        "Unsupported selected npm destination and mode.")]
    public void StandaloneNpmCommandsRejectUnadmittedUnitsAndMismatchedChannelPairs(string unit,
        string channel, string destination, string expectedDiagnostic)
    {
        using var files = new Files();
        string suffix = destination == "npmjs" ? "npmjs" : "github-npm";
        string check = destination == "npmjs" ? "metadata" : "visibility";
        ReleaseFile file = files.Prepared.Files[0];
        using var output = new StringWriter();
        using var error = new StringWriter();

        (string dryPrepared, string dryObserved) = WriteCase(dryRun: true);
        Assert.AreEqual(2, Program.Run(["release", "describe-" + suffix, dryPrepared, dryObserved],
            output, error));
        Assert.AreEqual("", output.ToString());
        Assert.Contains(expectedDiagnostic, error.ToString());

        output.GetStringBuilder().Clear();
        error.GetStringBuilder().Clear();
        (string livePrepared, string liveObserved) = WriteCase(dryRun: false);
        Assert.AreEqual(2, Program.Run(["release", "complete-" + suffix, livePrepared,
            liveObserved, liveObserved, "-"], output, error));
        Assert.AreEqual("", output.ToString());
        Assert.Contains(expectedDiagnostic, error.ToString());

        (string PreparedPath, string ObservedPath) WriteCase(bool dryRun)
        {
            string tag = dryRun ? "dry" : "live";
            PreparedRelease candidate = files.Prepared with
            {
                Subject = files.Prepared.Subject with
                {
                    Unit = unit,
                    Channel = channel,
                    Destination = destination,
                    DryRun = dryRun,
                },
            };
            string prep = files.Write(tag + "-prepared.json", JsonSerializer.Serialize(
                new ReleasePreparationResult(candidate, new(StringComparer.Ordinal), [], []),
                TransferJson.Default.ReleasePreparationResult));
            string obs = files.Write(tag + "-observed.json", JsonSerializer.Serialize(
                new ReleaseObservationResult(new(candidate.Subject,
                    [new(file.Output, file.Name, file.Version, ReleaseFileState.Absent, null,
                        null)], [check], [new(check, ReleaseCheckStatus.Passed)]), []),
                TransferJson.Default.ReleaseObservationResult));
            return (prep, obs);
        }
    }

    private sealed class Files : IDisposable
    {
        internal string Root { get; } = Directory.CreateTempSubdirectory("release-cli-").FullName;
        internal PreparedRelease Prepared { get; }
        internal string Preparation { get; }
        internal Files(bool github = false)
        {
            byte[] bytes = [1, 2, 3];
            Directory.CreateDirectory(Path.Combine(Root, "original"));
            File.WriteAllBytes(Path.Combine(Root, "original/package.tgz"), bytes);
            Prepared = new(new(new('a', 40), "refs/heads/main", NpmReleaseUnits.Hexo,
                "src/public/lib/hexo-renderer-asciidoc/workflow-delivery.release-unit.yml",
                github ? "buddy" : "official",
                github ? "github-packages-npm" : "npmjs", true),
                [new("build", "archive", "npm-tarball", "@example/package", "1.2.3",
                    "original/package.tgz", bytes.Length,
                    Convert.ToHexStringLower(SHA256.HashData(bytes)))],
                ["consumer"], [new("consumer", ReleaseCheckStatus.Passed)]);
            Preparation = Write("preparation.json", JsonSerializer.Serialize(
                new ReleasePreparationResult(Prepared, new(StringComparer.Ordinal), [], []),
                TransferJson.Default.ReleasePreparationResult));
        }
        internal string Write(string name, string content)
        {
            string path = Path.Combine(Root, name);
            File.WriteAllText(path, content);
            return path;
        }
        public void Dispose() => Directory.Delete(Root, true);
    }
}
