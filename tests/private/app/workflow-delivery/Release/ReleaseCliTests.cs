using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.Destinations;
using WorkflowDelivery.Release;

namespace WorkflowDelivery.Tests.Release;

[TestClass]
public sealed class ReleaseCliTests
{
    [TestMethod]
    [DataRow(true, false, 0)]
    [DataRow(false, false, 1)]
    [DataRow(true, true, 1)]
    [DataRow(false, true, 1)]
    public void ActualGithubCompletionRequiresRemoteConsumerEvenWithExactOriginals(
        bool consumerPassed, bool initiallyAbsent, int expectedExit)
    {
        using var files = new Files();
        PreparedRelease prepared = files.Prepared with
        { Subject = files.Prepared.Subject with
            { Channel = "buddy", Destination = "github-packages-npm", DryRun = false } };
        ReleaseFile file = prepared.Files[0];
        string preparation = files.Write("buddy-prepared.json", JsonSerializer.Serialize(
            new NpmPreparationResult(prepared, [], []), TransferJson.Default.NpmPreparationResult));
        var observed = new GithubNpmObservationResult(new(prepared.Subject,
            [new(file.Output, file.Name, file.Version, ReleaseFileState.Exact,
                file.Size, file.Sha256)],
            ["visibility"], [new("visibility", ReleaseCheckStatus.Passed)]), []);
        string observation = files.Write("buddy-observed.json", JsonSerializer.Serialize(observed,
            TransferJson.Default.GithubNpmObservationResult));
        string initial = files.Write("buddy-initial.json", JsonSerializer.Serialize(observed with
        { Observation = observed.Observation with { Files = [observed.Observation.Files[0] with
            { State = initiallyAbsent ? ReleaseFileState.Absent : ReleaseFileState.Exact,
                Size = initiallyAbsent ? null : file.Size,
                Sha256 = initiallyAbsent ? null : file.Sha256 }] } },
            TransferJson.Default.GithubNpmObservationResult));
        string consumer = files.Write("buddy-consumed.json", JsonSerializer.Serialize(
            new GithubNpmConsumerResult(prepared.Subject, new("native-registry-consumer",
                consumerPassed ? ReleaseCheckStatus.Passed : ReleaseCheckStatus.Failed), [], []),
            TransferJson.Default.GithubNpmConsumerResult));
        using var output = new StringWriter();
        using var error = new StringWriter();

        Assert.AreEqual(expectedExit, Program.Run(["release", "complete-github-npm", preparation,
            initial, observation, consumer, "-"], output, error), error.ToString());

        ReleaseOutcome result = JsonSerializer.Deserialize(output.ToString(),
            TransferJson.Default.ReleaseOutcome)!;
        Assert.AreEqual(prepared.Subject, result.Subject);
        Assert.AreEqual(initiallyAbsent ? ReleaseBusinessState.Unknown : consumerPassed ?
                ReleaseBusinessState.Satisfied : ReleaseBusinessState.Unsatisfied,
            result.State);
        Assert.IsEmpty(result.Uploads);
    }

    [TestMethod]
    [DataRow("Exact", 0, 0)]
    [DataRow("Absent", 0, 1)]
    [DataRow("Conflict", 1, 0)]
    [DataRow("Unknown", 1, 0)]
    public void ActualDryRunCommandReportsHypotheticalWorkAndFailure(string stateName,
        int exit, int writes)
    {
        using var files = new Files();
        PreparedRelease prepared = files.Prepared;
        ReleaseFile file = prepared.Files[0];
        ReleaseFileState state = Enum.Parse<ReleaseFileState>(stateName);
        var observation = new NpmjsObservationResult(new(prepared.Subject,
            [new(file.Output, file.Name, file.Version, state,
                state == ReleaseFileState.Exact ? file.Size : null,
                state == ReleaseFileState.Exact ? file.Sha256 : null)],
            ["visibility"], [new("visibility", ReleaseCheckStatus.Passed)]), []);
        string observed = files.Write("observed.json", JsonSerializer.Serialize(observation,
            TransferJson.Default.NpmjsObservationResult));
        using var output = new StringWriter();
        using var error = new StringWriter();

        Assert.AreEqual(exit, Program.Run(["release", "describe-npmjs", files.Preparation,
            observed], output, error), error.ToString());

        ReleaseDryRun result = JsonSerializer.Deserialize(output.ToString(),
            TransferJson.Default.ReleaseDryRun)!;
        Assert.AreEqual(exit == 0, result.Ready);
        Assert.HasCount(writes, result.HypotheticalWrites);
        Assert.AreEqual(prepared.Subject, result.Subject);
        Assert.IsTrue(result.Subject.DryRun);
        Assert.AreEqual("", error.ToString());
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("substituted")]
    public void ActualObservationCommandRejectsMissingOrSubstitutedOriginal(string defect)
    {
        using var files = new Files();
        string archive = Path.Combine(files.Root, files.Prepared.Files[0].Path);
        if (defect == "missing") File.Delete(archive);
        else File.WriteAllBytes(archive, [4, 5, 6]);
        using var output = new StringWriter();
        using var error = new StringWriter();

        Assert.AreEqual(2, Program.Run(["release", "observe-npmjs", files.Preparation,
            files.Root], output, error));

        Assert.AreEqual("", output.ToString());
        Assert.IsNotEmpty(error.ToString());
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("null")]
    [DataRow("unknown")]
    [DataRow("duplicate")]
    public void ActualPreparationCommandRejectsMalformedSubjectBeforeNativeExecution(
        string defect)
    {
        using var files = new Files();
        JsonObject request = JsonNode.Parse(JsonSerializer.Serialize(
            new NpmPreparationRequest(files.Prepared.Subject, files.Root, files.Root),
            TransferJson.Default.NpmPreparationRequest))!.AsObject();
        if (defect == "missing") request.Remove("subject");
        if (defect == "null") request["subject"] = null;
        if (defect == "unknown") request["approvalMirror"] = true;
        string json = request.ToJsonString();
        if (defect == "duplicate") json = json.Insert(1, "\"scratch\":\"duplicate\",");
        string path = files.Write("request.json", json);
        using var output = new StringWriter();
        using var error = new StringWriter();

        Assert.AreEqual(2, Program.Run(["release", "prepare-npm", path], output, error));

        Assert.AreEqual("", output.ToString());
        Assert.IsNotEmpty(error.ToString());
    }

    [TestMethod]
    public void FailedPreparationKeepsNativeDiagnosticsAndNonzeroExit()
    {
        using var files = new Files();
        string request = files.Write("request.json", JsonSerializer.Serialize(
            new NpmPreparationRequest(files.Prepared.Subject, files.Root, files.Root),
            TransferJson.Default.NpmPreparationRequest));
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
