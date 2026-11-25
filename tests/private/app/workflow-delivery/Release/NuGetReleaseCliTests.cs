using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using WorkflowDelivery.Destinations;
using WorkflowDelivery.Release;

namespace WorkflowDelivery.Tests.Release;

[TestClass]
public sealed class NuGetReleaseCliTests(TestContext context)
{
    [TestMethod]
    public void PrepareControlledNativeResultTransfersQualifiedPreparation()
    {
        using var fixture = new NuGetReleaseTestFixture();
        var request = new NuGetPreparationRequest(fixture.Prepared.Subject, fixture.Root,
            Path.Combine(fixture.Root, "preparation-scratch"));
        string requestPath = fixture.Write("request.json", request,
            TransferJson.Default.NuGetPreparationRequest);
        using var output = new StringWriter();
        int prepares = 0;

        int? exit = NuGetReleaseCli.Run(["release", "prepare-nuget", requestPath], output,
            (captured, _) =>
            {
                prepares++;
                Assert.AreEqual(request, captured);
                return Task.FromResult(fixture.Preparation);
            });

        Assert.AreEqual(0, exit);
        Assert.AreEqual(1, prepares);
        NuGetPreparationResult transfer = JsonSerializer.Deserialize(output.ToString(),
            TransferJson.Default.NuGetPreparationResult)!;
        Assert.AreEqual(fixture.Prepared.Subject, transfer.Prepared.Subject);
        Assert.AreEqual(fixture.Prepared.Files[0], Assert.ContainsSingle(transfer.Prepared.Files));
        Assert.AreEqual(fixture.Preparation.Native["AssemblyInformationalVersion"],
            transfer.Native["AssemblyInformationalVersion"]);
        Assert.IsEmpty(transfer.Errors);
        Assert.AreEqual(fixture.Archive, NuGetRelease.Original(transfer, fixture.Root).Path);
    }

    [TestMethod]
    [DataRow("Absent", 0, 1)]
    [DataRow("Unknown", 1, 0)]
    public void DescribeReportsHypotheticalWritesOrUncertainState(string stateName,
        int expectedExit, int writes)
    {
        using var fixture = new NuGetReleaseTestFixture(dryRun: true);
        string preparation = Preparation(fixture);
        string observed = Observation(fixture, "observed.json",
            Enum.Parse<ReleaseFileState>(stateName));
        using var output = new StringWriter();

        int? exit = NuGetReleaseCli.Run(["release", "describe-nuget", preparation,
            observed], output);

        Assert.AreEqual(expectedExit, exit);
        ReleaseDryRun result = JsonSerializer.Deserialize(output.ToString(),
            TransferJson.Default.ReleaseDryRun)!;
        Assert.AreEqual(expectedExit == 0, result.Ready);
        Assert.AreEqual(fixture.Prepared.Subject, result.Subject);
        Assert.IsTrue(result.Subject.DryRun);
        Assert.HasCount(writes, result.HypotheticalWrites);
        if (writes != 0)
            Assert.AreEqual(fixture.Prepared.Files[0].Output,
                Assert.ContainsSingle(result.HypotheticalWrites));
        if (expectedExit != 0) Assert.IsNotEmpty(result.Reasons);
    }

    [TestMethod]
    [DataRow("Exact")]
    [DataRow("Absent")]
    public async Task CompleteExactOrAbsentReportsActualEvidence(string stateName)
    {
        using var fixture = new NuGetReleaseTestFixture();
        ReleaseFileState state = Enum.Parse<ReleaseFileState>(stateName);
        string initial = Observation(fixture, "initial.json", state);
        string final = Observation(fixture, "final.json", ReleaseFileState.Exact);
        string publisher = "-";
        NuGetPushResult? invocation = null;
        if (state == ReleaseFileState.Absent)
        {
            using var transport = new NuGetReleaseTestFixture.UploadTransport(
                HttpStatusCode.Created);
            invocation = await NuGetPublisher.PushAsync(fixture.Prepared, fixture.Root,
                NuGetPublisher.GithubEndpoint, "controlled-capability", context.CancellationToken,
                transport);
            Assert.IsTrue(invocation.ReturnedSuccessfully);
            Assert.AreEqual(1, transport.Sends);
            publisher = fixture.Write("publisher.json", new NuGetPublicationResult(
                fixture.Prepared.Subject, fixture.Observe(ReleaseFileState.Absent), invocation,
                fixture.Observe(ReleaseFileState.Exact), []),
                TransferJson.Default.NuGetPublicationResult);
        }
        using var output = new StringWriter();

        int? exit = NuGetReleaseCli.Run(["release", "complete-nuget", Preparation(fixture),
            initial, final, publisher], output);

        Assert.AreEqual(0, exit);
        NuGetCompletionResult result = JsonSerializer.Deserialize(output.ToString(),
            TransferJson.Default.NuGetCompletionResult)!;
        Assert.AreEqual(fixture.Prepared.Subject, result.Outcome.Subject);
        Assert.AreEqual(ReleaseBusinessState.Satisfied, result.Outcome.State);
        Assert.AreEqual(state == ReleaseFileState.Absent, result.MutationAttempted);
        Assert.AreEqual(invocation, result.Invocation);
        Assert.IsEmpty(result.Outcome.Uploads);
        Assert.IsEmpty(result.Outcome.Reasons);
    }

    [TestMethod]
    public void CompleteMissingPublisherRemainsUnknown()
    {
        using var fixture = new NuGetReleaseTestFixture();
        string initial = Observation(fixture, "initial.json", ReleaseFileState.Absent);
        string final = Observation(fixture, "final.json", ReleaseFileState.Exact);
        using var output = new StringWriter();

        int? exit = NuGetReleaseCli.Run(["release", "complete-nuget", Preparation(fixture),
            initial, final, "-"], output);

        Assert.AreEqual(1, exit);
        NuGetCompletionResult result = JsonSerializer.Deserialize(output.ToString(),
            TransferJson.Default.NuGetCompletionResult)!;
        Assert.AreEqual(ReleaseBusinessState.Unknown, result.Outcome.State);
        Assert.IsNull(result.MutationAttempted);
        Assert.IsNull(result.Invocation);
        Assert.Contains("Required publication result is missing", string.Join(";",
            result.Outcome.Reasons));
    }

    [TestMethod]
    public void CompleteFreshExactPublisherDoesNotClaimMutation()
    {
        using var fixture = new NuGetReleaseTestFixture();
        string initial = Observation(fixture, "initial.json", ReleaseFileState.Absent);
        string final = Observation(fixture, "final.json", ReleaseFileState.Exact);
        string publisher = fixture.Write("publisher.json", new NuGetPublicationResult(
            fixture.Prepared.Subject, fixture.Observe(ReleaseFileState.Exact), null,
            fixture.Observe(ReleaseFileState.Exact), []),
            TransferJson.Default.NuGetPublicationResult);
        using var output = new StringWriter();

        int? exit = NuGetReleaseCli.Run(["release", "complete-nuget", Preparation(fixture),
            initial, final, publisher], output);

        Assert.AreEqual(0, exit);
        NuGetCompletionResult result = JsonSerializer.Deserialize(output.ToString(),
            TransferJson.Default.NuGetCompletionResult)!;
        Assert.AreEqual(ReleaseBusinessState.Satisfied, result.Outcome.State);
        Assert.AreEqual(false, result.MutationAttempted);
        Assert.IsNull(result.Invocation);
        Assert.IsEmpty(result.Outcome.Uploads);
    }

    [TestMethod]
    public void CompleteMissingFinalObservationFileCannotReportCompletion()
    {
        using var fixture = new NuGetReleaseTestFixture();
        string exact = Observation(fixture, "exact.json", ReleaseFileState.Exact);
        using var output = new StringWriter();

        Assert.ThrowsExactly<FileNotFoundException>(() => NuGetReleaseCli.Run(
            ["release", "complete-nuget", Preparation(fixture), exact,
                Path.Combine(fixture.Root, "missing-final.json"), "-"], output));

        Assert.AreEqual("", output.ToString());
    }

    [TestMethod]
    public void CompleteRejectsNullPublicationInsteadOfTreatingItAsMissing()
    {
        using var fixture = new NuGetReleaseTestFixture();
        string preparation = Preparation(fixture);
        string exact = Observation(fixture, "exact.json", ReleaseFileState.Exact);
        string publisher = fixture.WriteText("publisher.json", "null");
        string initial = Observation(fixture, "initial.json", ReleaseFileState.Absent);
        using var output = new StringWriter();

        Assert.ThrowsExactly<InvalidDataException>(() => NuGetReleaseCli.Run(
            ["release", "complete-nuget", preparation, initial, exact, publisher], output));

        Assert.AreEqual("", output.ToString());
    }

    [TestMethod]
    [DataRow("state")]
    [DataRow("integer-state")]
    public void DescribeRejectsInvalidObservationState(string defect)
    {
        using var fixture = new NuGetReleaseTestFixture(dryRun: true);
        string preparation = Preparation(fixture);
        string observation = Observation(fixture, "observed.json", ReleaseFileState.Exact);
        JsonObject root = JsonNode.Parse(File.ReadAllText(observation))!.AsObject();
        root["observation"]!["files"]![0]!["state"] = defect == "state"
            ? JsonValue.Create("not-a-state") : JsonValue.Create(17);
        File.WriteAllText(observation, root.ToJsonString());
        using var output = new StringWriter();

        Assert.ThrowsExactly<JsonException>(() => NuGetReleaseCli.Run(
            ["release", "describe-nuget", preparation, observation], output));

        Assert.AreEqual("", output.ToString());
    }

    [TestMethod]
    public void PublishRejectsDuplicatePreparationBeforeCredentialResolution()
    {
        using var fixture = new NuGetReleaseTestFixture();
        string preparation = Preparation(fixture);
        string text = File.ReadAllText(preparation);
        File.WriteAllText(preparation, "{\"prepared\":null," + text.TrimStart()[1..]);
        using var output = new StringWriter();
        using var error = new StringWriter();

        Assert.AreEqual(2, Program.Run(["release", "publish-nuget", preparation,
            fixture.Root], output, error));

        Assert.AreEqual("", output.ToString());
        Assert.Contains("Duplicate", error.ToString());
    }

    [TestMethod]
    public void ObserveSubstitutedOriginalRejectsBeforeObservation()
    {
        using var fixture = new NuGetReleaseTestFixture();
        string preparation = Preparation(fixture);
        File.AppendAllText(fixture.Archive, "substituted original bytes");
        using var output = new StringWriter();
        int observes = 0;

        Assert.ThrowsExactly<InvalidDataException>(() => NuGetReleaseCli.Run(
            ["release", "observe-nuget", preparation, fixture.Root, "-"], output,
            observe: (_, _, _) =>
            {
                observes++;
                throw new AssertFailedException("Substitution must fail before observation.");
            }));

        Assert.AreEqual(0, observes);
        Assert.AreEqual("", output.ToString());
    }

    [TestMethod]
    public void DescribeLiveRequestRejectsHypotheticalCompletion()
    {
        using var fixture = new NuGetReleaseTestFixture();
        using var output = new StringWriter();

        Assert.ThrowsExactly<InvalidDataException>(() => NuGetReleaseCli.Run(
            ["release", "describe-nuget", Preparation(fixture),
                Observation(fixture, "exact.json", ReleaseFileState.Exact)], output));
        Assert.AreEqual("", output.ToString());
    }

    [TestMethod]
    public void CompleteDryRunRejectsLiveCompletion()
    {
        using var fixture = new NuGetReleaseTestFixture(dryRun: true);
        string exact = Observation(fixture, "exact.json", ReleaseFileState.Exact);
        using var output = new StringWriter();

        Assert.ThrowsExactly<InvalidDataException>(() => NuGetReleaseCli.Run(
            ["release", "complete-nuget", Preparation(fixture), exact, exact, "-"],
            output));
        Assert.AreEqual("", output.ToString());
    }

    private static string Preparation(NuGetReleaseTestFixture fixture) => fixture.Write(
        "preparation.json", fixture.Preparation, TransferJson.Default.NuGetPreparationResult);

    private static string Observation(NuGetReleaseTestFixture fixture, string name,
        ReleaseFileState state) => fixture.Write(name, fixture.Observe(state),
            TransferJson.Default.NuGetObservationResult);

}
