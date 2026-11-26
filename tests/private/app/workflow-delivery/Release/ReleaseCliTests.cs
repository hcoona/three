using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.Destinations;
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
            new NpmPreparationResult(prepared, [], []), TransferJson.Default.NpmPreparationResult));
        string initial = WriteObservation("initial.json", initialState);
        string final = WriteObservation("final.json", finalState);
        using var output = new StringWriter();
        using var error = new StringWriter();

        Assert.AreEqual(expectedExit, Program.Run(["release", "complete-" + registry, preparation,
            initial, final, "-"], output, error), error.ToString());

        ReleaseOutcome result = JsonSerializer.Deserialize(output.ToString(),
            TransferJson.Default.ReleaseOutcome)!;
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
            return files.Write(path, github
                ? JsonSerializer.Serialize(new GithubNpmObservationResult(observed, []),
                    TransferJson.Default.GithubNpmObservationResult)
                : JsonSerializer.Serialize(new NpmjsObservationResult(observed, []),
                    TransferJson.Default.NpmjsObservationResult));
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
            new NpmPreparationResult(prepared, [], []), TransferJson.Default.NpmPreparationResult));
        ReleaseObservation absent = Observe(ReleaseFileState.Absent);
        ReleaseObservation before = Observe(ReleaseFileState.Conflict);
        ReleaseObservation exact = Observe(ReleaseFileState.Exact);
        string initial = WriteObservation("initial.json", absent);
        string finalAbsent = WriteObservation("final-absent.json", absent);
        string finalExact = WriteObservation("final-exact.json", exact);
        const string blocked = "Refreshed registry state does not permit publication.";
        string publication = files.Write("published.json", github
            ? JsonSerializer.Serialize(new GithubNpmPublicationResult(prepared.Subject,
                new(before, []), [], new(before, []), [blocked]),
                TransferJson.Default.GithubNpmPublicationResult)
            : JsonSerializer.Serialize(new NpmjsPublicationResult(prepared.Subject,
                new(before, []), [], new(before, []), [blocked]),
                TransferJson.Default.NpmjsPublicationResult));
        using var output = new StringWriter();
        using var error = new StringWriter();

        Assert.AreEqual(1, Program.Run(["release", "complete-" + registry, preparation,
            initial, finalAbsent, publication], output, error), error.ToString());
        ReleaseOutcome refused = JsonSerializer.Deserialize(output.ToString(),
            TransferJson.Default.ReleaseOutcome)!;
        Assert.AreEqual(ReleaseBusinessState.Unsatisfied, refused.State);
        Assert.IsEmpty(refused.Uploads);
        Assert.Contains(blocked, refused.Reasons);

        output.GetStringBuilder().Clear();
        Assert.AreEqual(0, Program.Run(["release", "complete-" + registry, preparation,
            initial, finalExact, publication], output, error), error.ToString());
        ReleaseOutcome observed = JsonSerializer.Deserialize(output.ToString(),
            TransferJson.Default.ReleaseOutcome)!;
        Assert.AreEqual(ReleaseBusinessState.Satisfied, observed.State);
        Assert.IsEmpty(observed.Uploads);

        var badUpload = new ReleaseUpload(file.Output,
            new(WorkflowDelivery.Platform.NativeTermination.Exited, 0, "", "", 0.1, null));
        string illegalWrite = files.Write("illegal-write.json", github
            ? JsonSerializer.Serialize(new GithubNpmPublicationResult(prepared.Subject,
                new(before, []), [badUpload], new(exact, []), []),
                TransferJson.Default.GithubNpmPublicationResult)
            : JsonSerializer.Serialize(new NpmjsPublicationResult(prepared.Subject,
                new(before, []), [badUpload], new(exact, []), []),
                TransferJson.Default.NpmjsPublicationResult));
        output.GetStringBuilder().Clear();
        Assert.AreEqual(2, Program.Run(["release", "complete-" + registry, preparation,
            initial, finalExact, illegalWrite], output, error));
        Assert.Contains("Unsupported npm write result.", error.ToString());

        output.GetStringBuilder().Clear();
        error.GetStringBuilder().Clear();
        Assert.AreEqual(2, Program.Run(["release", "complete-" + registry, preparation,
            finalExact, finalExact, publication], output, error));
        Assert.AreEqual("", output.ToString());
        Assert.Contains("Mismatched or unnecessary npm publication result.", error.ToString());

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
            files.Write(path, github
                ? JsonSerializer.Serialize(new GithubNpmObservationResult(observation, []),
                    TransferJson.Default.GithubNpmObservationResult)
                : JsonSerializer.Serialize(new NpmjsObservationResult(observation, []),
                    TransferJson.Default.NpmjsObservationResult));
    }

    [TestMethod]
    public void DryRunCommandReportsTheMissingPackageAsHypothetical()
    {
        using var files = new Files();
        PreparedRelease prepared = files.Prepared;
        ReleaseFile file = prepared.Files[0];
        var observation = new NpmjsObservationResult(new(prepared.Subject,
            [new(file.Output, file.Name, file.Version, ReleaseFileState.Absent, null, null)],
            ["visibility"], [new("visibility", ReleaseCheckStatus.Passed)]), []);
        string observed = files.Write("observed.json", JsonSerializer.Serialize(observation,
            TransferJson.Default.NpmjsObservationResult));
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
        using var files = new Files();
        string archive = Path.Combine(files.Root, files.Prepared.Files[0].Path);
        File.WriteAllBytes(archive, [4, 5, 6]);
        using var output = new StringWriter();
        using var error = new StringWriter();

        Assert.AreEqual(2, Program.Run(["release", command, files.Preparation, files.Root,
            "unused-scratch", "unused-toolchain"], output, error));

        Assert.AreEqual("", output.ToString());
        Assert.Contains("Original release transfer differs from preparation.", error.ToString());
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
        var failed = new NpmPreparationResult(files.Prepared with
        { Files = [], Checks = [new("consumer", ReleaseCheckStatus.Failed)] }, [],
            ["native consumer failed"]);
        using var output = new StringWriter();

        Assert.AreEqual(1, ReleaseCli.Run(["release", "prepare-npm", request], output,
            (_, _) => Task.FromResult(failed)));

        NpmPreparationResult result = JsonSerializer.Deserialize(output.ToString(),
            TransferJson.Default.NpmPreparationResult)!;
        Assert.AreEqual("native consumer failed", Assert.ContainsSingle(result.Errors));
        Assert.AreEqual(ReleaseCheckStatus.Failed,
            Assert.ContainsSingle(result.Prepared.Checks).Status);
        Assert.IsEmpty(result.Prepared.Files);
    }

    private sealed class Files : IDisposable
    {
        internal string Root { get; } = Directory.CreateTempSubdirectory("release-cli-").FullName;
        internal PreparedRelease Prepared { get; }
        internal string Preparation { get; }
        internal Files()
        {
            byte[] bytes = [1, 2, 3];
            Directory.CreateDirectory(Path.Combine(Root, "original"));
            File.WriteAllBytes(Path.Combine(Root, "original/package.tgz"), bytes);
            Prepared = new(new(new('a', 40), "refs/heads/feature", "example",
                "product/release.yml", "official", "npmjs", true),
                [new("build", "archive", "npm-tarball", "@example/package", "1.2.3",
                    "original/package.tgz", bytes.Length,
                    Convert.ToHexStringLower(SHA256.HashData(bytes)))],
                ["consumer"], [new("consumer", ReleaseCheckStatus.Passed)]);
            Preparation = Write("preparation.json", JsonSerializer.Serialize(
                new NpmPreparationResult(Prepared, [], []),
                TransferJson.Default.NpmPreparationResult));
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
