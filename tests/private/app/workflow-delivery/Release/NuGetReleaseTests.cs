using System.Net;
using WorkflowDelivery.Destinations;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Release;

namespace WorkflowDelivery.Tests.Release;

[TestClass]
public sealed class NuGetReleaseTests(TestContext context)
{
    [TestMethod]
    [DataRow("Exact", false)]
    [DataRow("Absent", true)]
    public async Task PublishAsyncExactOrAbsentUsesSupportedJourney(string stateName,
        bool expectedMutation)
    {
        using var fixture = new NuGetReleaseTestFixture();
        ReleaseFileState state = Enum.Parse<ReleaseFileState>(stateName);
        GithubNuGetObservationResult initial = fixture.Observe(state);
        GithubNuGetObservationResult exact = fixture.Observe(ReleaseFileState.Exact);
        using var transport = new NuGetReleaseTestFixture.UploadTransport(HttpStatusCode.Created);
        int reads = 0;

        GithubNuGetPublicationResult publication = await NuGetRelease.PublishAsync(
            fixture.Preparation, fixture.Root, "controlled-capability",
            (_, _, _) => Task.FromResult(reads++ == 0 ? initial : exact),
            (prepared, originals, endpoint, credential, token) => GithubNuGetPublisher.PushAsync(
                prepared, originals, endpoint, credential, token, transport),
            context.CancellationToken);
        GithubNuGetCompletionResult result = NuGetRelease.Complete(fixture.Prepared, initial,
            exact, fixture.Consumer(), expectedMutation ? publication : null);

        Assert.AreEqual(ReleaseBusinessState.Satisfied, result.Outcome.State);
        Assert.AreEqual((bool?)expectedMutation, result.MutationAttempted);
        Assert.AreEqual(expectedMutation ? 2 : 1, reads);
        Assert.AreEqual(expectedMutation ? 1 : 0, transport.Sends);
        Assert.IsEmpty(result.Outcome.Uploads);
        Assert.AreSame(publication.Invocation, result.Invocation);
        if (expectedMutation) Assert.IsTrue(result.Invocation!.ReturnedSuccessfully);
        else Assert.IsNull(result.Invocation);
    }

    [TestMethod]
    public async Task PublishAsyncFreshExactAvoidsMutation()
    {
        using var fixture = new NuGetReleaseTestFixture();
        GithubNuGetObservationResult initial = fixture.Observe(ReleaseFileState.Absent);
        GithubNuGetObservationResult fresh = fixture.Observe(ReleaseFileState.Exact);
        int pushes = 0;

        GithubNuGetPublicationResult publication = await NuGetRelease.PublishAsync(
            fixture.Preparation, fixture.Root, "controlled-capability",
            (_, _, _) => Task.FromResult(fresh), (_, _, _, _, _) =>
            { pushes++; throw new AssertFailedException("Exact state must not invoke upload."); },
            context.CancellationToken);
        GithubNuGetCompletionResult result = NuGetRelease.Complete(fixture.Prepared, initial,
            fresh, fixture.Consumer(), publication);

        Assert.AreEqual(0, pushes);
        Assert.IsNull(publication.Invocation);
        Assert.AreSame(fresh, publication.Before);
        Assert.AreSame(fresh, publication.After);
        Assert.AreEqual(false, result.MutationAttempted);
        Assert.AreEqual(ReleaseBusinessState.Satisfied, result.Outcome.State);
    }

    [TestMethod]
    [DataRow("Unknown", "Passed")]
    [DataRow("Conflict", "Passed")]
    [DataRow("Absent", "Missing")]
    public async Task PublishAsyncUnknownOrConflictBlocksMutation(string stateName,
        string qualificationName)
    {
        using var fixture = new NuGetReleaseTestFixture();
        GithubNuGetObservationResult blocked = fixture.Observe(
            Enum.Parse<ReleaseFileState>(stateName),
            Enum.Parse<ReleaseCheckStatus>(qualificationName));
        int pushes = 0;

        GithubNuGetPublicationResult result = await NuGetRelease.PublishAsync(
            fixture.Preparation, fixture.Root, "controlled-capability",
            (_, _, _) => Task.FromResult(blocked), (_, _, _, _, _) =>
            { pushes++; throw new AssertFailedException("Blocked state must not invoke upload."); },
            context.CancellationToken);

        Assert.AreEqual(0, pushes);
        Assert.IsNull(result.Invocation);
        Assert.IsNotEmpty(result.Errors);
        Assert.AreSame(blocked, result.After);
    }

    [TestMethod]
    [DataRow("Exact", "Satisfied")]
    [DataRow("Absent", "Unsatisfied")]
    [DataRow("Unknown", "Unknown")]
    [DataRow("Conflict", "Unsatisfied")]
    public async Task PublishAsyncOfficialUploadAbnormalityReadsBackWithoutResend(
        string finalStateName, string outcomeName)
    {
        using var fixture = new NuGetReleaseTestFixture();
        using var transport = new NuGetReleaseTestFixture.UploadTransport(HttpStatusCode.Conflict);
        GithubNuGetObservationResult absent = fixture.Observe(ReleaseFileState.Absent);
        GithubNuGetObservationResult final = fixture.Observe(
            Enum.Parse<ReleaseFileState>(finalStateName));
        NuGetPushResult? captured = null;
        int reads = 0;
        int invocations = 0;

        GithubNuGetPublicationResult publication = await NuGetRelease.PublishAsync(
            fixture.Preparation, fixture.Root, "controlled-capability",
            (_, _, _) => Task.FromResult(reads++ == 0 ? absent : final),
            async (prepared, originals, endpoint, credential, token) =>
            {
                invocations++;
                captured = await GithubNuGetPublisher.PushAsync(prepared, originals, endpoint,
                    credential, token, transport);
                return captured;
            }, context.CancellationToken);
        GithubNuGetCompletionResult result = NuGetRelease.Complete(fixture.Prepared, absent,
            final, fixture.Consumer(), publication);

        Assert.AreEqual(1, invocations);
        Assert.AreEqual(1, transport.Sends);
        Assert.AreEqual(2, reads);
        Assert.IsNotNull(captured);
        Assert.IsFalse(captured.ReturnedSuccessfully);
        Assert.IsNotNull(captured.FailureType);
        Assert.AreSame(captured, publication.Invocation);
        Assert.AreSame(captured, result.Invocation);
        Assert.IsEmpty(publication.Errors);
        Assert.AreEqual(Enum.Parse<ReleaseBusinessState>(outcomeName), result.Outcome.State);
        Assert.AreEqual(true, result.MutationAttempted);
        // Official library evidence is never represented as an invented subprocess upload.
        Assert.IsEmpty(result.Outcome.Uploads);
    }

    [TestMethod]
    public void CompleteAbsentWithoutPublisherRemainsUnknown()
    {
        using var fixture = new NuGetReleaseTestFixture();
        GithubNuGetCompletionResult result = NuGetRelease.Complete(fixture.Prepared,
            fixture.Observe(ReleaseFileState.Absent), fixture.Observe(ReleaseFileState.Exact),
            fixture.Consumer(), null);

        Assert.AreEqual(ReleaseBusinessState.Unknown, result.Outcome.State);
        Assert.IsNull(result.MutationAttempted);
        Assert.IsNull(result.Invocation);
        Assert.Contains("Required publication result is missing", string.Join(";",
            result.Outcome.Reasons));
    }

    [TestMethod]
    [DataRow("Failed")]
    [DataRow("Missing")]
    public void CompleteRequiredConsumerNotPassedIsUnsatisfied(string statusName)
    {
        using var fixture = new NuGetReleaseTestFixture();
        GithubNuGetObservationResult exact = fixture.Observe(ReleaseFileState.Exact);
        GithubNuGetCompletionResult result = NuGetRelease.Complete(fixture.Prepared, exact,
            exact, fixture.Consumer(Enum.Parse<ReleaseCheckStatus>(statusName)), null);

        Assert.AreEqual(ReleaseBusinessState.Unsatisfied, result.Outcome.State);
        Assert.Contains(NuGetRelease.ConsumerCheck, string.Join(";", result.Outcome.Reasons));
        Assert.AreEqual(false, result.MutationAttempted);
    }

    [TestMethod]
    [DataRow("reads")]
    [DataRow("commands")]
    [DataRow("errors")]
    public void CompletePassedConsumerWithoutRequiredEvidenceIsRejected(string missing)
    {
        using var fixture = new NuGetReleaseTestFixture();
        GithubNuGetObservationResult exact = fixture.Observe(ReleaseFileState.Exact);
        GithubNuGetConsumerResult consumer = fixture.Consumer();
        consumer = missing switch
        {
            "reads" => consumer with { Reads = null },
            "commands" => consumer with { Commands = [] },
            _ => consumer with { Errors = ["Incomplete verification."] },
        };

        Assert.ThrowsExactly<InvalidDataException>(() => NuGetRelease.Complete(
            fixture.Prepared, exact, exact, consumer, null));
    }

    [TestMethod]
    [DataRow("Cancelled", "Cancelled")]
    [DataRow("TimedOut", "TimedOut")]
    [DataRow("CleanupFailed", "Failed")]
    public async Task ConsumeAsyncNativeTerminationRetainsActualCommandResult(
        string terminationName, string statusName)
    {
        using var fixture = new NuGetReleaseTestFixture();
        string scratch = Directory.CreateDirectory(Path.Combine(fixture.Root, "consumer-scratch"))
            .FullName;
        var captured = new NativeCommandResult(Enum.Parse<NativeTermination>(terminationName),
            17, "partial output", "captured diagnostic", 0.125, "Controlled interruption.");
        int executions = 0;

        GithubNuGetConsumerResult result = await NuGetRelease.ConsumeAsync(fixture.Preparation,
            fixture.Observe(ReleaseFileState.Exact), fixture.Root, scratch,
            "controlled-read-capability", (command, _) =>
            {
                executions++;
                Assert.AreEqual("--version", Assert.ContainsSingle(command.Arguments));
                Assert.IsFalse(command.InheritEnvironment);
                Assert.IsFalse(command.Environment!.ContainsKey("GITHUB_TOKEN"));
                return Task.FromResult(captured);
            }, context.CancellationToken);

        Assert.AreEqual(1, executions);
        Assert.AreEqual(Enum.Parse<ReleaseCheckStatus>(statusName), result.Check.Status);
        Assert.AreEqual(NuGetRelease.ConsumerCheck, result.Check.Id);
        Assert.AreSame(captured, Assert.ContainsSingle(result.Commands));
        Assert.IsNull(result.Reads);
        Assert.IsNotEmpty(result.Errors);
        Assert.IsFalse(File.Exists(Path.Combine(scratch, "consumer", "graph.json")));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task OriginalSubstitutedArchiveRejectsBeforeDependencyCalls(bool duringRead)
    {
        using var fixture = new NuGetReleaseTestFixture();
        if (!duringRead) File.AppendAllText(fixture.Archive, "substituted");
        int reads = 0;
        int pushes = 0;

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => NuGetRelease.PublishAsync(
            fixture.Preparation, fixture.Root, "controlled-capability", (_, _, _) =>
            {
                reads++;
                File.AppendAllText(fixture.Archive, "substituted");
                return Task.FromResult(fixture.Observe(ReleaseFileState.Absent));
            }, (_, _, _, _, _) =>
            { pushes++; throw new AssertFailedException("Substituted archive must not upload."); },
            context.CancellationToken));

        Assert.AreEqual(duringRead ? 1 : 0, reads);
        Assert.AreEqual(0, pushes);
    }

    [TestMethod]
    public async Task DryRunPublicationAndLiveCompletionAreExcluded()
    {
        using var fixture = new NuGetReleaseTestFixture(dryRun: true);
        int calls = 0;
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => NuGetRelease.PublishAsync(
            fixture.Preparation, fixture.Root, "controlled-capability", (_, _, _) =>
            { calls++; throw new AssertFailedException("Dry-run publication cannot observe."); },
            (_, _, _, _, _) =>
            { calls++; throw new AssertFailedException("Dry run cannot upload."); },
            context.CancellationToken));
        GithubNuGetObservationResult exact = fixture.Observe(ReleaseFileState.Exact);
        Assert.ThrowsExactly<InvalidDataException>(() => NuGetRelease.Complete(fixture.Prepared,
            exact, exact, fixture.Consumer(), null));
        Assert.AreEqual(0, calls);
    }
}
