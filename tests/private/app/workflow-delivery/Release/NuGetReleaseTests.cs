using System.Net;
using WorkflowDelivery.Destinations;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Release;

namespace WorkflowDelivery.Tests.Release;

[TestClass]
public sealed class NuGetReleaseTests(TestContext context)
{
    // A returned publication may not be visible yet; only Absent is re-observed, bounded.
    [TestMethod]
    [DataRow(new[] { "Absent", "Absent", "Exact" }, 3, "Exact")]
    [DataRow(new[] { "Conflict" }, 1, "Conflict")]
    [DataRow(new[] { "Unknown" }, 1, "Unknown")]
    [DataRow(new[] { "Absent", "Absent", "Absent", "Absent" }, 3, "Absent")]
    public async Task SettleReobservesOnlyAbsenceWithinTheBound(string[] states, int reads,
        string expected)
    {
        using var fixture = new NuGetReleaseTestFixture();
        int read = 0;
        NuGetObservationResult result = await NuGetRelease.SettleAsync(fixture.Prepared,
            (prepared, credential, _) =>
            {
                Assert.AreEqual(fixture.Prepared, prepared);
                Assert.AreEqual("credential", credential);
                return Task.FromResult(fixture.Observe(Enum.Parse<ReleaseFileState>(
                    states[read++])));
            }, "credential", 3, TimeSpan.Zero, context.CancellationToken);

        Assert.AreEqual(reads, read);
        Assert.AreEqual(Enum.Parse<ReleaseFileState>(expected),
            result.Observation.Files[0].State);
    }

    [TestMethod]
    [DataRow("Exact", false)]
    [DataRow("Absent", true)]
    public async Task PublishAsyncExactOrAbsentUsesSupportedJourney(string stateName,
        bool expectedMutation)
    {
        using var fixture = new NuGetReleaseTestFixture();
        ReleaseFileState state = Enum.Parse<ReleaseFileState>(stateName);
        NuGetObservationResult initial = fixture.Observe(state);
        NuGetObservationResult exact = fixture.Observe(ReleaseFileState.Exact);
        using var transport = new NuGetReleaseTestFixture.UploadTransport(HttpStatusCode.Created);
        int reads = 0;

        NuGetPublicationResult publication = await NuGetRelease.PublishAsync(
            fixture.Preparation, fixture.Root, "controlled-capability",
            (_, _, _) => Task.FromResult(reads++ == 0 ? initial : exact),
            (prepared, originals, endpoint, credential, token) => NuGetPublisher.PushAsync(
                prepared, originals, endpoint, credential, token, transport),
            context.CancellationToken);
        NuGetCompletionResult result = NuGetRelease.Complete(fixture.Prepared, initial,
            exact, expectedMutation ? publication : null);

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
        NuGetObservationResult initial = fixture.Observe(ReleaseFileState.Absent);
        NuGetObservationResult fresh = fixture.Observe(ReleaseFileState.Exact);
        int pushes = 0;

        NuGetPublicationResult publication = await NuGetRelease.PublishAsync(
            fixture.Preparation, fixture.Root, "controlled-capability",
            (_, _, _) => Task.FromResult(fresh), (_, _, _, _, _) =>
            { pushes++; throw new AssertFailedException("Exact state must not invoke upload."); },
            context.CancellationToken);
        NuGetCompletionResult result = NuGetRelease.Complete(fixture.Prepared, initial,
            fresh, publication);

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
        NuGetObservationResult blocked = fixture.Observe(
            Enum.Parse<ReleaseFileState>(stateName),
            Enum.Parse<ReleaseCheckStatus>(qualificationName));
        int pushes = 0;

        NuGetPublicationResult result = await NuGetRelease.PublishAsync(
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
        NuGetObservationResult absent = fixture.Observe(ReleaseFileState.Absent);
        NuGetObservationResult final = fixture.Observe(
            Enum.Parse<ReleaseFileState>(finalStateName));
        NuGetPushResult? captured = null;
        int reads = 0;
        int invocations = 0;

        NuGetPublicationResult publication = await NuGetRelease.PublishAsync(
            fixture.Preparation, fixture.Root, "controlled-capability",
            (_, _, _) => Task.FromResult(reads++ == 0 ? absent : final),
            async (prepared, originals, endpoint, credential, token) =>
            {
                invocations++;
                captured = await NuGetPublisher.PushAsync(prepared, originals, endpoint,
                    credential, token, transport);
                return captured;
            }, context.CancellationToken);
        NuGetCompletionResult result = NuGetRelease.Complete(fixture.Prepared, absent,
            final, publication);

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
        NuGetCompletionResult result = NuGetRelease.Complete(fixture.Prepared,
            fixture.Observe(ReleaseFileState.Absent), fixture.Observe(ReleaseFileState.Exact),
            null);

        Assert.AreEqual(ReleaseBusinessState.Unknown, result.Outcome.State);
        Assert.IsNull(result.MutationAttempted);
        Assert.IsNull(result.Invocation);
        Assert.Contains("Required publication result is missing", string.Join(";",
            result.Outcome.Reasons));
    }

    [TestMethod]
    [DataRow("Failed")]
    [DataRow("Missing")]
    public void CompleteFinalRegistryCheckNotPassedIsUnsatisfied(string statusName)
    {
        using var fixture = new NuGetReleaseTestFixture();
        NuGetObservationResult exact = fixture.Observe(ReleaseFileState.Exact);
        NuGetObservationResult final = exact with
        {
            Observation = exact.Observation with
            {
                Checks = [exact.Observation.Checks[0], new("native-metadata-visibility",
                    Enum.Parse<ReleaseCheckStatus>(statusName))],
            },
        };
        NuGetCompletionResult result = NuGetRelease.Complete(fixture.Prepared, exact,
            final, null);

        Assert.AreEqual(ReleaseBusinessState.Unsatisfied, result.Outcome.State);
        Assert.Contains("native-metadata-visibility", string.Join(";", result.Outcome.Reasons));
        Assert.AreEqual(false, result.MutationAttempted);
    }

    [TestMethod]
    public void CompleteFinalUnknownRegistryStateRemainsUnknown()
    {
        using var fixture = new NuGetReleaseTestFixture();
        NuGetCompletionResult result = NuGetRelease.Complete(fixture.Prepared,
            fixture.Observe(ReleaseFileState.Exact), fixture.Observe(ReleaseFileState.Unknown),
            null);

        Assert.AreEqual(ReleaseBusinessState.Unknown, result.Outcome.State);
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
        NuGetObservationResult exact = fixture.Observe(ReleaseFileState.Exact);
        Assert.ThrowsExactly<InvalidDataException>(() => NuGetRelease.Complete(fixture.Prepared,
            exact, exact, null));
        Assert.AreEqual(0, calls);
    }
}
