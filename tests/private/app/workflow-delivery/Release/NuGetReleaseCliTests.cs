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
    [DataRow("Exact", 0, 0)]
    [DataRow("Absent", 0, 1)]
    [DataRow("Unknown", 1, 0)]
    [DataRow("Conflict", 1, 0)]
    public void DescribeExactOrAbsentReportsHypotheticalWrites(string stateName,
        int expectedExit, int writes)
    {
        using var fixture = new NuGetReleaseTestFixture(dryRun: true);
        string preparation = Preparation(fixture);
        string observed = Observation(fixture, "observed.json",
            Enum.Parse<ReleaseFileState>(stateName));
        using var output = new StringWriter();

        int? exit = NuGetReleaseCli.Run(["release", "describe-github-nuget", preparation,
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
        string consumer = fixture.Write("consumer.json", fixture.Consumer(),
            TransferJson.Default.GithubNuGetConsumerResult);
        string publisher = "-";
        NuGetPushResult? invocation = null;
        if (state == ReleaseFileState.Absent)
        {
            using var transport = new NuGetReleaseTestFixture.UploadTransport(
                HttpStatusCode.Created);
            invocation = await GithubNuGetPublisher.PushAsync(fixture.Prepared, fixture.Root,
                GithubNuGetPublisher.Endpoint, "controlled-capability", context.CancellationToken,
                transport);
            Assert.IsTrue(invocation.ReturnedSuccessfully);
            Assert.AreEqual(1, transport.Sends);
            publisher = fixture.Write("publisher.json", new GithubNuGetPublicationResult(
                fixture.Prepared.Subject, fixture.Observe(ReleaseFileState.Absent), invocation,
                fixture.Observe(ReleaseFileState.Exact), []),
                TransferJson.Default.GithubNuGetPublicationResult);
        }
        using var output = new StringWriter();

        int? exit = NuGetReleaseCli.Run(["release", "complete-github-nuget", Preparation(fixture),
            initial, final, consumer, publisher], output);

        Assert.AreEqual(0, exit);
        GithubNuGetCompletionResult result = JsonSerializer.Deserialize(output.ToString(),
            TransferJson.Default.GithubNuGetCompletionResult)!;
        Assert.AreEqual(fixture.Prepared.Subject, result.Outcome.Subject);
        Assert.AreEqual(ReleaseBusinessState.Satisfied, result.Outcome.State);
        Assert.AreEqual(state == ReleaseFileState.Absent, result.MutationAttempted);
        Assert.AreEqual(invocation, result.Invocation);
        Assert.IsEmpty(result.Outcome.Uploads);
        Assert.IsEmpty(result.Outcome.Reasons);
    }

    [TestMethod]
    [DataRow("Failed")]
    [DataRow("Missing")]
    public void CompleteRequiredConsumerNotPassedFails(string statusName)
    {
        using var fixture = new NuGetReleaseTestFixture();
        string exact = Observation(fixture, "exact.json", ReleaseFileState.Exact);
        string consumer = fixture.Write("consumer.json",
            fixture.Consumer(Enum.Parse<ReleaseCheckStatus>(statusName)),
            TransferJson.Default.GithubNuGetConsumerResult);
        using var output = new StringWriter();

        int? exit = NuGetReleaseCli.Run(["release", "complete-github-nuget", Preparation(fixture),
            exact, exact, consumer, "-"], output);

        Assert.AreEqual(1, exit);
        GithubNuGetCompletionResult result = JsonSerializer.Deserialize(output.ToString(),
            TransferJson.Default.GithubNuGetCompletionResult)!;
        Assert.AreEqual(ReleaseBusinessState.Unsatisfied, result.Outcome.State);
        Assert.AreEqual(false, result.MutationAttempted);
        Assert.Contains(NuGetRelease.ConsumerCheck, string.Join(";", result.Outcome.Reasons));
    }

    [TestMethod]
    public void CompleteMissingPublisherRemainsUnknown()
    {
        using var fixture = new NuGetReleaseTestFixture();
        string initial = Observation(fixture, "initial.json", ReleaseFileState.Absent);
        string final = Observation(fixture, "final.json", ReleaseFileState.Exact);
        string consumer = fixture.Write("consumer.json", fixture.Consumer(),
            TransferJson.Default.GithubNuGetConsumerResult);
        using var output = new StringWriter();

        int? exit = NuGetReleaseCli.Run(["release", "complete-github-nuget", Preparation(fixture),
            initial, final, consumer, "-"], output);

        Assert.AreEqual(1, exit);
        GithubNuGetCompletionResult result = JsonSerializer.Deserialize(output.ToString(),
            TransferJson.Default.GithubNuGetCompletionResult)!;
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
        string consumer = fixture.Write("consumer.json", fixture.Consumer(),
            TransferJson.Default.GithubNuGetConsumerResult);
        string publisher = fixture.Write("publisher.json", new GithubNuGetPublicationResult(
            fixture.Prepared.Subject, fixture.Observe(ReleaseFileState.Exact), null,
            fixture.Observe(ReleaseFileState.Exact), []),
            TransferJson.Default.GithubNuGetPublicationResult);
        using var output = new StringWriter();

        int? exit = NuGetReleaseCli.Run(["release", "complete-github-nuget", Preparation(fixture),
            initial, final, consumer, publisher], output);

        Assert.AreEqual(0, exit);
        GithubNuGetCompletionResult result = JsonSerializer.Deserialize(output.ToString(),
            TransferJson.Default.GithubNuGetCompletionResult)!;
        Assert.AreEqual(ReleaseBusinessState.Satisfied, result.Outcome.State);
        Assert.AreEqual(false, result.MutationAttempted);
        Assert.IsNull(result.Invocation);
        Assert.IsEmpty(result.Outcome.Uploads);
    }

    [TestMethod]
    public void CompleteMissingConsumerFileCannotReportCompletion()
    {
        using var fixture = new NuGetReleaseTestFixture();
        string exact = Observation(fixture, "exact.json", ReleaseFileState.Exact);
        using var output = new StringWriter();

        Assert.ThrowsExactly<FileNotFoundException>(() => NuGetReleaseCli.Run(
            ["release", "complete-github-nuget", Preparation(fixture), exact, exact,
                Path.Combine(fixture.Root, "missing-consumer.json"), "-"], output));

        Assert.AreEqual("", output.ToString());
    }

    [TestMethod]
    [DataRow("preparation", "root-null")]
    [DataRow("preparation", "null")]
    [DataRow("preparation", "missing")]
    [DataRow("preparation", "unknown")]
    [DataRow("preparation", "duplicate")]
    [DataRow("observation", "root-null")]
    [DataRow("observation", "null")]
    [DataRow("observation", "missing")]
    [DataRow("observation", "unknown")]
    [DataRow("observation", "duplicate")]
    [DataRow("consumer", "root-null")]
    [DataRow("consumer", "null")]
    [DataRow("consumer", "missing")]
    [DataRow("consumer", "unknown")]
    [DataRow("consumer", "duplicate")]
    [DataRow("publisher", "root-null")]
    [DataRow("publisher", "null")]
    [DataRow("publisher", "missing")]
    [DataRow("publisher", "unknown")]
    [DataRow("publisher", "duplicate")]
    public void CompleteStrictTransferJsonRejectsMalformedInput(string transfer, string defect)
    {
        using var fixture = new NuGetReleaseTestFixture();
        string preparation = Preparation(fixture);
        string exact = Observation(fixture, "exact.json", ReleaseFileState.Exact);
        string consumer = fixture.Write("consumer.json", fixture.Consumer(),
            TransferJson.Default.GithubNuGetConsumerResult);
        // A fresh-exact publisher result legitimately contains no library invocation.
        string publisher = fixture.Write("publisher.json", new GithubNuGetPublicationResult(
            fixture.Prepared.Subject, fixture.Observe(ReleaseFileState.Exact), null,
            fixture.Observe(ReleaseFileState.Exact), []),
            TransferJson.Default.GithubNuGetPublicationResult);
        string target = transfer switch
        {
            "preparation" => preparation,
            "observation" => exact,
            "consumer" => consumer,
            _ => publisher,
        };
        string property = transfer switch
        {
            "preparation" => "prepared",
            "observation" => "observation",
            "consumer" => "check",
            _ => "before",
        };
        File.WriteAllText(target, Malformed(File.ReadAllText(target), property, defect));
        string initial = Observation(fixture, "initial.json", ReleaseFileState.Absent);
        using var output = new StringWriter();

        void Run() => NuGetReleaseCli.Run(["release", "complete-github-nuget", preparation,
            initial, exact, consumer, publisher], output);

        if (defect is "root-null" or "duplicate")
            Assert.ThrowsExactly<InvalidDataException>(Run);
        else Assert.ThrowsExactly<JsonException>(Run);
        Assert.AreEqual("", output.ToString());
    }

    [TestMethod]
    [DataRow("state")]
    [DataRow("integer-state")]
    [DataRow("native-null")]
    [DataRow("nested-duplicate")]
    public void DescribeStrictNestedJsonRejectsMalformedTransfer(string defect)
    {
        using var fixture = new NuGetReleaseTestFixture(dryRun: true);
        string preparation = Preparation(fixture);
        string observation = Observation(fixture, "observed.json", ReleaseFileState.Exact);
        if (defect == "native-null")
        {
            JsonObject root = JsonNode.Parse(File.ReadAllText(preparation))!.AsObject();
            root["native"]!["PackageId"] = null;
            File.WriteAllText(preparation, root.ToJsonString());
        }
        else if (defect == "nested-duplicate")
        {
            string text = File.ReadAllText(observation);
            string duplicate = text.Replace("\"output\": \"original\"",
                "\"output\": \"original\", \"output\": \"original\"",
                StringComparison.Ordinal);
            Assert.AreNotEqual(text, duplicate);
            File.WriteAllText(observation, duplicate);
        }
        else
        {
            JsonObject root = JsonNode.Parse(File.ReadAllText(observation))!.AsObject();
            root["observation"]!["files"]![0]!["state"] = defect == "state"
                ? JsonValue.Create("not-a-state") : JsonValue.Create(17);
            File.WriteAllText(observation, root.ToJsonString());
        }
        using var output = new StringWriter();

        void Run() => NuGetReleaseCli.Run(["release", "describe-github-nuget", preparation,
            observation], output);

        if (defect is "native-null" or "nested-duplicate")
            Assert.ThrowsExactly<InvalidDataException>(Run);
        else Assert.ThrowsExactly<JsonException>(Run);
        Assert.AreEqual("", output.ToString());
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
            ["release", "observe-github-nuget", preparation, fixture.Root], output,
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
            ["release", "describe-github-nuget", Preparation(fixture),
                Observation(fixture, "exact.json", ReleaseFileState.Exact)], output));
        Assert.AreEqual("", output.ToString());
    }

    [TestMethod]
    public void CompleteDryRunRejectsLiveCompletion()
    {
        using var fixture = new NuGetReleaseTestFixture(dryRun: true);
        string exact = Observation(fixture, "exact.json", ReleaseFileState.Exact);
        string consumer = fixture.Write("consumer.json", fixture.Consumer(),
            TransferJson.Default.GithubNuGetConsumerResult);
        using var output = new StringWriter();

        Assert.ThrowsExactly<InvalidDataException>(() => NuGetReleaseCli.Run(
            ["release", "complete-github-nuget", Preparation(fixture), exact, exact, consumer,
                "-"], output));
        Assert.AreEqual("", output.ToString());
    }

    private static string Preparation(NuGetReleaseTestFixture fixture) => fixture.Write(
        "preparation.json", fixture.Preparation, TransferJson.Default.NuGetPreparationResult);

    private static string Observation(NuGetReleaseTestFixture fixture, string name,
        ReleaseFileState state) => fixture.Write(name, fixture.Observe(state),
            TransferJson.Default.GithubNuGetObservationResult);

    private static string Malformed(string text, string property, string defect)
    {
        if (defect == "root-null") return "null";
        JsonObject root = JsonNode.Parse(text)!.AsObject();
        if (defect == "null") root[property] = null;
        if (defect == "missing") root.Remove(property);
        if (defect == "unknown") root["unexpectedTransferProperty"] = true;
        if (defect == "duplicate")
            return "{\"" + property + "\":" + root[property]!.ToJsonString() + ","
                + text.TrimStart()[1..];
        return root.ToJsonString();
    }
}
