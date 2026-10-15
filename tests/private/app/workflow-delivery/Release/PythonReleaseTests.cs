using System.Security.Cryptography;
using WorkflowDelivery.Destinations;
using WorkflowDelivery.Ecosystems;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Release;

namespace WorkflowDelivery.Tests.Release;

[TestClass]
public sealed class PythonReleaseTests(TestContext context)
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PublishAsyncInitiallyExactOrDryRunRejectsPublisher(bool dryRun)
    {
        using var fixture = new Fixture(dryRun);
        PythonIndexObservationResult initial = dryRun ? fixture.Observe() : fixture.Exact();

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => fixture.PublishAsync(
            initial, context.CancellationToken));

        Assert.AreEqual(0, fixture.Reads);
        Assert.IsEmpty(fixture.Writes);
        if (dryRun)
            Assert.ThrowsExactly<InvalidDataException>(() => fixture.Complete(initial,
                fixture.Exact(), null));
        else
        {
            PythonCompletionResult result = fixture.Complete(initial, initial, null);
            Assert.AreEqual(ReleaseBusinessState.Satisfied, result.Outcome.State);
            Assert.AreEqual(false, result.MutationAttempted);
            Assert.IsEmpty(result.Outcome.Uploads);
        }
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    public async Task PublishAsyncSupportedMissingSubsetWritesOnlyMissingFilesInOrder(
        bool wheelExact, bool sdistExact)
    {
        using var fixture = new Fixture();
        PythonIndexObservationResult initial = fixture.Observe(
            wheelExact ? ReleaseFileState.Exact : ReleaseFileState.Absent,
            sdistExact ? ReleaseFileState.Exact : ReleaseFileState.Absent);
        string[] expected = wheelExact ? ["sdist"] : sdistExact ? ["wheel"] : ["wheel", "sdist"];
        fixture.Observations.Enqueue(initial);
        if (expected.Length == 2)
            fixture.Observations.Enqueue(fixture.Observe(ReleaseFileState.Exact));
        fixture.Observations.Enqueue(fixture.Exact());

        PythonPublicationResult publication = await fixture.PublishAsync(initial,
            context.CancellationToken);
        PythonCompletionResult result = fixture.Complete(initial, fixture.Exact(), publication);

        AssertSequence(expected, fixture.Writes);
        AssertSequence(expected, result.Outcome.Uploads.Select(value => value.Output));
        Assert.HasCount(expected.Length, publication.Invocations);
        Assert.HasCount(expected.Length, publication.Readbacks);
        Assert.AreEqual(expected.Length + 1, fixture.Reads);
        Assert.AreEqual(ReleaseBusinessState.Satisfied, result.Outcome.State);
        Assert.AreEqual(true, result.MutationAttempted);
        Assert.AreSame(publication, result.Publication);
        Assert.IsEmpty(publication.Errors);
        Assert.IsEmpty(result.Outcome.Reasons);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    public async Task PublishAsyncFreshExactSubsetRemovesUnnecessaryWrites(
        bool wheelMissing, bool sdistMissing)
    {
        using var fixture = new Fixture();
        PythonIndexObservationResult initial = fixture.Observe();
        PythonIndexObservationResult fresh = fixture.Observe(
            wheelMissing ? ReleaseFileState.Absent : ReleaseFileState.Exact,
            sdistMissing ? ReleaseFileState.Absent : ReleaseFileState.Exact);
        fixture.Observations.Enqueue(fresh);
        string[] expected = wheelMissing ? ["wheel"] : sdistMissing ? ["sdist"] : [];
        if (expected.Length != 0) fixture.Observations.Enqueue(fixture.Exact());

        PythonPublicationResult publication = await fixture.PublishAsync(initial,
            context.CancellationToken);
        PythonCompletionResult result = fixture.Complete(initial, fixture.Exact(), publication);

        AssertSequence(expected, fixture.Writes);
        AssertSequence(expected, result.Outcome.Uploads.Select(value => value.Output));
        Assert.AreSame(fresh, publication.Before);
        Assert.AreEqual(expected.Length + 1, fixture.Reads);
        Assert.AreEqual(expected.Length != 0, result.MutationAttempted);
        Assert.AreEqual(ReleaseBusinessState.Satisfied, result.Outcome.State);
    }

    [TestMethod]
    [DataRow("Unknown")]
    [DataRow("Conflict")]
    public async Task PublishAsyncInitialUnsafeStateRejectsBeforeDelegates(
        string stateName)
    {
        ReleaseFileState state = Enum.Parse<ReleaseFileState>(stateName);
        using var fixture = new Fixture();
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => fixture.PublishAsync(
            fixture.Observe(state), context.CancellationToken));
        Assert.AreEqual(0, fixture.Reads);
        Assert.IsEmpty(fixture.Writes);
    }

    [TestMethod]
    [DataRow("Unknown", "Passed", "Unknown")]
    [DataRow("Conflict", "Passed", "Unsatisfied")]
    [DataRow("Absent", "Missing", "Unsatisfied")]
    public async Task PublishAsyncFreshUnsafeStateStopsWithoutWrites(string stateName,
        string qualificationName, string expectedName)
    {
        ReleaseFileState state = Enum.Parse<ReleaseFileState>(stateName);
        ReleaseCheckStatus qualification = Enum.Parse<ReleaseCheckStatus>(qualificationName);
        ReleaseBusinessState expected = Enum.Parse<ReleaseBusinessState>(expectedName);
        using var fixture = new Fixture();
        PythonIndexObservationResult initial = fixture.Observe();
        PythonIndexObservationResult fresh = fixture.Observe(state, qualification: qualification);
        fixture.Observations.Enqueue(fresh);

        PythonPublicationResult publication = await fixture.PublishAsync(initial,
            context.CancellationToken);
        PythonCompletionResult result = fixture.Complete(initial, fresh, publication);

        Assert.AreEqual(1, fixture.Reads);
        Assert.IsEmpty(fixture.Writes);
        Assert.IsEmpty(publication.Invocations);
        Assert.IsEmpty(publication.Readbacks);
        Assert.IsNotEmpty(publication.Errors);
        Assert.AreEqual(expected, result.Outcome.State);
        Assert.AreEqual(false, result.MutationAttempted);
        Assert.IsEmpty(result.Outcome.Uploads);
    }

    [TestMethod]
    [DataRow("Unknown", "Passed", "Unknown")]
    [DataRow("Conflict", "Passed", "Unsatisfied")]
    [DataRow("Absent", "Missing", "Unsatisfied")]
    [DataRow("Absent", "Passed", "Unsatisfied")]
    public async Task PublishAsyncUnsafeIntermediateStateStopsLaterWrites(
        string wheelName, string qualificationName, string expectedName)
    {
        ReleaseFileState wheel = Enum.Parse<ReleaseFileState>(wheelName);
        ReleaseCheckStatus qualification = Enum.Parse<ReleaseCheckStatus>(qualificationName);
        ReleaseBusinessState expected = Enum.Parse<ReleaseBusinessState>(expectedName);
        using var fixture = new Fixture();
        PythonIndexObservationResult initial = fixture.Observe();
        PythonIndexObservationResult readback = fixture.Observe(wheel,
            qualification: qualification);
        fixture.Observations.Enqueue(initial);
        fixture.Observations.Enqueue(readback);

        PythonPublicationResult publication = await fixture.PublishAsync(initial,
            context.CancellationToken);
        PythonCompletionResult result = fixture.Complete(initial, readback, publication);

        AssertSequence(["wheel"], fixture.Writes);
        Assert.AreEqual(2, fixture.Reads);
        Assert.AreSame(readback, Assert.ContainsSingle(publication.Readbacks));
        Assert.IsNotEmpty(publication.Errors);
        Assert.AreEqual(expected, result.Outcome.State);
        Assert.AreEqual(true, result.MutationAttempted);
        Assert.IsTrue(Assert.ContainsSingle(result.Outcome.Uploads).Command.Succeeded);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task PublishAsyncFreshDisappearanceCannotEnlargeInitialMissingSet(
        bool wheelInitiallyExact)
    {
        using var fixture = new Fixture();
        PythonIndexObservationResult initial = fixture.Observe(
            wheelInitiallyExact ? ReleaseFileState.Exact : ReleaseFileState.Absent,
            wheelInitiallyExact ? ReleaseFileState.Absent : ReleaseFileState.Exact);
        PythonIndexObservationResult fresh = fixture.Observe();
        fixture.Observations.Enqueue(fresh);

        PythonPublicationResult publication = await fixture.PublishAsync(initial,
            context.CancellationToken);
        PythonCompletionResult result = fixture.Complete(initial, fresh, publication);

        Assert.AreEqual(1, fixture.Reads);
        Assert.IsEmpty(fixture.Writes);
        Assert.IsEmpty(publication.Invocations);
        Assert.Contains("enlarge", Assert.ContainsSingle(publication.Errors));
        Assert.AreEqual(ReleaseBusinessState.Unsatisfied, result.Outcome.State);
        Assert.AreEqual(false, result.MutationAttempted);
        Assert.IsEmpty(result.Outcome.Uploads);
    }

    [TestMethod]
    public async Task PublishAsyncReadbackDisappearanceCannotEnlargeInitialMissingSet()
    {
        using var fixture = new Fixture();
        PythonIndexObservationResult initial = fixture.Observe(ReleaseFileState.Absent,
            ReleaseFileState.Exact);
        PythonIndexObservationResult readback = fixture.Observe(ReleaseFileState.Exact);
        fixture.Observations.Enqueue(initial);
        fixture.Observations.Enqueue(readback);

        PythonPublicationResult publication = await fixture.PublishAsync(initial,
            context.CancellationToken);
        PythonCompletionResult result = fixture.Complete(initial, readback, publication);

        AssertSequence(["wheel"], fixture.Writes);
        Assert.AreEqual(2, fixture.Reads);
        Assert.Contains("enlarge", Assert.ContainsSingle(publication.Errors));
        Assert.AreSame(readback, Assert.ContainsSingle(publication.Readbacks));
        Assert.AreEqual(ReleaseBusinessState.Unsatisfied, result.Outcome.State);
        Assert.AreEqual(true, result.MutationAttempted);
        Assert.AreEqual("wheel", Assert.ContainsSingle(result.Outcome.Uploads).Output);
    }

    [TestMethod]
    [DataRow("failed", "Exact", "Satisfied")]
    [DataRow("failed", "Absent", "Unsatisfied")]
    [DataRow("ambiguous", "Exact", "Satisfied")]
    [DataRow("ambiguous", "Absent", "Unsatisfied")]
    [DataRow("ambiguous", "Unknown", "Unknown")]
    [DataRow("mask", "Exact", "Satisfied")]
    [DataRow("mask", "Absent", "Unsatisfied")]
    [DataRow("error", "Exact", "Satisfied")]
    [DataRow("cancelled", "Exact", "Satisfied")]
    [DataRow("cancelled", "Absent", "Unsatisfied")]
    public async Task PublishAsyncAbnormalInvocationStopsDespiteExactReadback(string defect,
        string finalSdistName, string expectedName)
    {
        ReleaseFileState finalSdist = Enum.Parse<ReleaseFileState>(finalSdistName);
        ReleaseBusinessState expected = Enum.Parse<ReleaseBusinessState>(expectedName);
        using var fixture = new Fixture();
        PythonIndexObservationResult initial = fixture.Observe();
        fixture.Observations.Enqueue(initial);
        fixture.Observations.Enqueue(fixture.Exact());
        NativeCommandResult command = defect switch
        {
            "failed" => new(NativeTermination.Exited, 17, "partial", "failure", 0.125, null),
            "ambiguous" => new(NativeTermination.TimedOut, null, "partial", "", 120, "timeout"),
            "cancelled" => new(NativeTermination.Cancelled, null, "partial", "", 0.25, "cancelled"),
            "error" => new(NativeTermination.Exited, 0, "partial", "", 0.5, "cleanup failed"),
            _ => Fixture.Success,
        };
        var invocation = new PythonPublishResult(new("wheel", command), defect == "mask",
            "Controlled diagnostics only.");
        fixture.Invocation = invocation;

        PythonPublicationResult publication = await fixture.PublishAsync(initial,
            context.CancellationToken);
        PythonCompletionResult result = fixture.Complete(initial,
            fixture.Observe(ReleaseFileState.Exact, finalSdist), publication);

        AssertSequence(["wheel"], fixture.Writes);
        Assert.AreEqual(2, fixture.Reads);
        Assert.HasCount(1, publication.Readbacks);
        Assert.AreSame(invocation, Assert.ContainsSingle(publication.Invocations));
        NativeCommandResult retained = Assert.ContainsSingle(result.Outcome.Uploads).Command;
        Assert.AreSame(command, retained);
        Assert.AreEqual(command.Termination, retained.Termination);
        Assert.AreEqual(command.ExitCode, retained.ExitCode);
        Assert.AreEqual(command.Error, retained.Error);
        Assert.AreEqual(command.DurationSeconds, retained.DurationSeconds);
        Assert.AreEqual(defect is "mask" or "error", retained.Succeeded);
        Assert.AreEqual(defect == "mask", publication.Invocations[0].MaskForwardingFailed);
        Assert.AreEqual(expected, result.Outcome.State);
        Assert.AreEqual(true, result.MutationAttempted);
        Assert.AreSame(publication, result.Publication);
    }

    [TestMethod]
    public async Task PublishAsyncCancelledBeforeWritePropagatesWithoutUpload()
    {
        using var fixture = new Fixture();
        using var cancel =
            CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        PythonIndexObservationResult initial = fixture.Observe();
        fixture.Observations.Enqueue(initial);
        fixture.AfterObserve = cancel.Cancel;

        OperationCanceledException error = await
            Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => fixture.PublishAsync(initial, cancel.Token));

        Assert.AreEqual(cancel.Token, error.CancellationToken);
        Assert.AreEqual(1, fixture.Reads);
        Assert.IsEmpty(fixture.Writes);
        PythonCompletionResult result = fixture.Complete(initial, fixture.Exact(), null);
        Assert.AreEqual(ReleaseBusinessState.Unknown, result.Outcome.State);
        Assert.IsNull(result.MutationAttempted);
    }

    [TestMethod]
    public async Task PublishAsyncCancelledReadbackRetainsInvocationAndStops()
    {
        using var fixture = new Fixture();
        using var cancel =
            CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        PythonIndexObservationResult initial = fixture.Observe();
        fixture.Observations.Enqueue(initial);
        fixture.BeforeReadback = cancel.Cancel;

        PythonPublicationResult publication = await fixture.PublishAsync(initial,
            cancel.Token);
        PythonCompletionResult result = fixture.Complete(initial,
            fixture.Observe(ReleaseFileState.Exact), publication);

        AssertSequence(["wheel"], fixture.Writes);
        Assert.AreEqual(2, fixture.Reads);
        Assert.HasCount(1, publication.Invocations);
        Assert.IsEmpty(publication.Readbacks);
        Assert.Contains("cancelled", Assert.ContainsSingle(publication.Errors));
        Assert.AreEqual(ReleaseBusinessState.Unsatisfied, result.Outcome.State);
        Assert.AreEqual(true, result.MutationAttempted);
        Assert.AreSame(Fixture.Success, Assert.ContainsSingle(result.Outcome.Uploads).Command);
    }

    [TestMethod]
    public async Task PublishAsyncCancelledPublisherWithoutResultLeavesEffectsUnknown()
    {
        using var fixture = new Fixture();
        using var cancel =
            CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        PythonIndexObservationResult initial = fixture.Observe();
        fixture.Observations.Enqueue(initial);
        fixture.AfterPublish = cancel.Cancel;

        OperationCanceledException error = await
            Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => fixture.PublishAsync(initial, cancel.Token));
        PythonCompletionResult result = fixture.Complete(initial, fixture.Exact(), null);

        Assert.AreEqual(cancel.Token, error.CancellationToken);
        AssertSequence(["wheel"], fixture.Writes);
        Assert.AreEqual(1, fixture.Reads);
        Assert.AreEqual(ReleaseBusinessState.Unknown, result.Outcome.State);
        Assert.IsNull(result.MutationAttempted);
        Assert.IsNull(result.Publication);
        Assert.IsEmpty(result.Outcome.Uploads);
        Assert.Contains("effects remain unknown", string.Join(";", result.Outcome.Reasons));
    }

    [TestMethod]
    [DataRow("Exact")]
    [DataRow("Absent")]
    public void CompleteMissingPublisherResultLeavesEffectsUnknown(string finalStateName)
    {
        ReleaseFileState finalState = Enum.Parse<ReleaseFileState>(finalStateName);
        using var fixture = new Fixture();
        PythonCompletionResult result = fixture.Complete(fixture.Observe(),
            fixture.Observe(finalState, finalState), null);

        Assert.AreEqual(ReleaseBusinessState.Unknown, result.Outcome.State);
        Assert.IsNull(result.MutationAttempted);
        Assert.IsNull(result.Publication);
        Assert.IsEmpty(result.Outcome.Uploads);
        Assert.Contains("Required Python publication result is missing", string.Join(";",
            result.Outcome.Reasons));
    }

    [TestMethod]
    [DataRow("both")]
    [DataRow("wheel-missing")]
    [DataRow("sdist-missing")]
    [DataRow("both-missing")]
    [DataRow("wheel-failed")]
    [DataRow("sdist-failed")]
    [DataRow("wheel-cancelled")]
    [DataRow("sdist-cancelled")]
    public void CompleteExactSetRequiresBothFormatConsumers(string defect)
    {
        using var fixture = new Fixture();
        PythonRemoteConsumerResult[] consumers = fixture.Consumers();
        if (defect == "both-missing") consumers = [];
        else if (defect.EndsWith("missing", StringComparison.Ordinal))
            consumers = consumers.Where(value => !defect.StartsWith(value.Original.Output,
                StringComparison.Ordinal)).ToArray();
        else if (defect != "both")
        {
            int index = defect.StartsWith("wheel", StringComparison.Ordinal) ? 0 : 1;
            consumers[index] = consumers[index] with
            {
                Check = consumers[index].Check with
                {
                    Status = defect.EndsWith("failed",
                    StringComparison.Ordinal) ? ReleaseCheckStatus.Failed :
                        ReleaseCheckStatus.Cancelled
                },
            };
        }

        PythonCompletionResult result = fixture.Complete(fixture.Exact(), fixture.Exact(), null,
            consumers);

        Assert.AreEqual(defect == "both" ? ReleaseBusinessState.Satisfied :
            ReleaseBusinessState.Unsatisfied, result.Outcome.State);
        Assert.AreEqual(false, result.MutationAttempted);
        Assert.IsEmpty(result.Outcome.Uploads);
        if (defect == "both") Assert.IsEmpty(result.Outcome.Reasons);
        else Assert.Contains("consumer", string.Join(";", result.Outcome.Reasons));
    }

    [TestMethod]
    [DataRow("wheel")]
    [DataRow("sdist")]
    public async Task CompleteAfterPublicationRequiresBothFormatConsumers(string omitted)
    {
        using var fixture = new Fixture();
        PythonIndexObservationResult initial = fixture.Observe();
        fixture.Observations.Enqueue(initial);
        fixture.Observations.Enqueue(fixture.Exact());
        PythonPublicationResult publication = await fixture.PublishAsync(initial,
            context.CancellationToken);

        PythonCompletionResult result = fixture.Complete(initial, fixture.Exact(), publication,
            fixture.Consumers().Where(value => value.Original.Output != omitted).ToArray());

        AssertSequence(["wheel"], fixture.Writes);
        Assert.AreEqual(ReleaseBusinessState.Unsatisfied, result.Outcome.State);
        Assert.AreEqual(true, result.MutationAttempted);
        Assert.Contains("remote-" + omitted + "-consumer", string.Join(";",
            result.Outcome.Reasons));
    }

    [TestMethod]
    [DataRow("commands")]
    [DataRow("install")]
    [DataRow("consume")]
    [DataRow("failed-command")]
    [DataRow("command-error")]
    [DataRow("errors")]
    public void CompletePassedConsumerWithoutNativeEvidenceRejects(string defect)
    {
        using var fixture = new Fixture();
        PythonRemoteConsumerResult[] consumers = fixture.Consumers();
        PythonRemoteConsumerResult consumer = consumers[0];
        consumers[0] = defect switch
        {
            "commands" => consumer with { Commands = [] },
            "install" or "consume" => consumer with
            { Commands = consumer.Commands.Where(value => value.Stage != defect).ToArray() },
            "errors" => consumer with { Errors = ["Controlled missing evidence."] },
            _ => consumer with
            {
                Commands = [consumer.Commands[0] with
                { Result = Fixture.Success with { ExitCode = defect == "failed-command" ? 17 : 0,
                    Error = defect == "command-error" ? "cleanup failed" : null } },
                consumer.Commands[1]]
            },
        };

        Assert.ThrowsExactly<InvalidDataException>(() => fixture.Complete(fixture.Exact(),
            fixture.Exact(), null, consumers));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PublishAsyncSubstitutedOriginalRejectsBeforeUpload(bool duringRead)
    {
        using var fixture = new Fixture();
        PythonIndexObservationResult initial = fixture.Observe();
        fixture.Observations.Enqueue(initial);
        if (duringRead) fixture.AfterObserve = fixture.SubstituteOriginal;
        else fixture.SubstituteOriginal();

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => fixture.PublishAsync(initial,
            context.CancellationToken));

        Assert.AreEqual(duringRead ? 1 : 0, fixture.Reads);
        Assert.IsEmpty(fixture.Writes);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PublishAsyncSubstitutedObservationSubjectRejects(bool fresh)
    {
        using var fixture = new Fixture();
        PythonIndexObservationResult initial = fixture.Observe();
        PythonIndexObservationResult substituted = initial with
        {
            Observation =
            initial.Observation with
            {
                Subject = fixture.Prepared.Subject with
                { Revision = new string('d', 40) }
            }
        };
        fixture.Observations.Enqueue(substituted);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => fixture.PublishAsync(
            fresh ? initial : substituted, context.CancellationToken));

        Assert.AreEqual(fresh ? 1 : 0, fixture.Reads);
        Assert.IsEmpty(fixture.Writes);
    }

    [TestMethod]
    [DataRow("consumer-subject")]
    [DataRow("consumer-original")]
    [DataRow("consumer-index")]
    [DataRow("consumer-check")]
    [DataRow("duplicate-consumer")]
    [DataRow("final-subject")]
    [DataRow("final-bytes")]
    [DataRow("publication-subject")]
    public void CompleteSubstitutedBindingsRejects(string defect)
    {
        using var fixture = new Fixture();
        PythonIndexObservationResult initial = fixture.Exact();
        PythonIndexObservationResult final = fixture.Exact();
        PythonRemoteConsumerResult[] consumers = fixture.Consumers();
        PythonPublicationResult? publication = null;
        ReleaseSubject other = fixture.Prepared.Subject with { Revision = new string('d', 40) };
        switch (defect)
        {
            case "consumer-subject": consumers[0] = consumers[0] with { Subject = other }; break;
            case "consumer-original":
                consumers[0] = consumers[0] with
                { Original = consumers[0].Original with { Sha256 = new string('e', 64) } }; break;
            case "consumer-index":
                consumers[0] = consumers[0] with
                { Index = "https://pypi.org/simple/" }; break;
            case "consumer-check":
                consumers[0] = consumers[0] with
                { Check = new("remote-sdist-consumer", ReleaseCheckStatus.Passed) }; break;
            case "duplicate-consumer": consumers[1] = consumers[0]; break;
            case "final-subject":
                final = final with
                { Observation = final.Observation with { Subject = other } }; break;
            case "final-bytes":
                final.Observation.Files[0] = final.Observation.Files[0] with
                { Sha256 = new string('e', 64) }; break;
            case "publication-subject":
                initial = fixture.Observe();
                publication = new(other, initial, [], [], []);
                break;
        }

        Assert.ThrowsExactly<InvalidDataException>(() => fixture.Complete(initial, final,
            publication, consumers));
    }

    [TestMethod]
    public async Task PublishAsyncSubstitutedPublisherOutputRejects()
    {
        using var fixture = new Fixture();
        PythonIndexObservationResult initial = fixture.Observe();
        fixture.Observations.Enqueue(initial);
        fixture.Invocation = new(new("sdist", Fixture.Success), false, "Controlled result.");

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => fixture.PublishAsync(initial,
            context.CancellationToken));

        AssertSequence(["wheel"], fixture.Writes);
        Assert.AreEqual(1, fixture.Reads);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CompletePostAbnormalWriteSequenceRejects(bool maskFailed)
    {
        using var fixture = new Fixture();
        PythonIndexObservationResult initial = fixture.Observe();
        NativeCommandResult command = maskFailed ? Fixture.Success : Fixture.Success with
        { ExitCode = 17 };
        var publication = new PythonPublicationResult(fixture.Prepared.Subject, initial,
            [new(new("wheel", command), maskFailed, "Controlled result."),
                new(new("sdist", Fixture.Success), false, "Controlled result.")],
            [fixture.Observe(ReleaseFileState.Exact), fixture.Exact()], []);

        Assert.ThrowsExactly<InvalidDataException>(() => fixture.Complete(initial,
            fixture.Exact(), publication));
        Assert.AreSame(command, publication.Invocations[0].Upload.Command);
        Assert.AreEqual(maskFailed, publication.Invocations[0].MaskForwardingFailed);
    }

    private static void AssertSequence(IEnumerable<string> expected, IEnumerable<string> actual) =>
        Assert.AreEqual(string.Join(",", expected), string.Join(",", actual));

    private sealed class Fixture : IDisposable
    {
        internal static readonly NativeCommandResult Success = new(NativeTermination.Exited,
            0, "controlled output", "", 0.125, null);
        internal string Root { get; } = Path.Combine(Path.GetTempPath(),
            "python-release-" + Guid.NewGuid().ToString("N"));
        internal PythonPreparationResult Preparation { get; }
        internal PreparedRelease Prepared => Preparation.Prepared;
        internal Queue<PythonIndexObservationResult> Observations { get; } = new();
        internal List<string> Writes { get; } = [];
        internal int Reads { get; private set; }
        internal Action? AfterObserve { get; set; }
        internal Action? BeforeReadback { get; set; }
        internal Action? AfterPublish { get; set; }
        internal PythonPublishResult? Invocation { get; set; }

        internal Fixture(bool dryRun = false)
        {
            Directory.CreateDirectory(Path.Combine(Root, "archives"));
            ReleaseFile[] files = [Original("wheel", "python-wheel", "product.whl",
                "wheel-original"),
                Original("sdist", "python-sdist", "product.tar.gz", "sdist-original")];
            var subject = new ReleaseSubject(new string('a', 40), "refs/heads/development",
                "hcoona-release-smoke-python", "release.yml", "buddy", "testpypi", dryRun);
            string[] required = ["native-input", "build", "contents", "wheel-consumer",
                "sdist-consumer", "original-bytes"];
            var prepared = new PreparedRelease(subject, files, required,
                required.Select(id => new ReleaseCheck(id, ReleaseCheckStatus.Passed)).ToArray());
            Preparation = new(prepared, new Dictionary<string, string>
            {
                ["name"] = subject.Unit,
                ["version"] = "1.0.0",
                ["commit"] = subject.Revision,
            }, [new("build", Command(), Success)], []);
        }

        private ReleaseFile Original(string output, string kind, string name, string contents)
        {
            string relative = "archives/" + name;
            string path = Path.Combine(Root, relative);
            File.WriteAllText(path, contents);
            byte[] bytes = File.ReadAllBytes(path);
            return new("distributions", output, kind, "hcoona-release-smoke-python", "1.0.0",
                relative, bytes.LongLength, Convert.ToHexStringLower(SHA256.HashData(bytes)));
        }

        internal PythonIndexObservationResult Observe(ReleaseFileState wheel =
            ReleaseFileState.Absent,
            ReleaseFileState sdist = ReleaseFileState.Absent,
            ReleaseCheckStatus qualification = ReleaseCheckStatus.Passed) => new(
            new(Prepared.Subject, Prepared.Files.Select(file => new ReleaseFileObservation(
                file.Output, file.Name, file.Version, file.Output == "wheel" ? wheel : sdist,
                file.Size, file.Sha256)).ToArray(), ["public-index-files"],
                [new("public-index-files", qualification)]), []);

        internal PythonIndexObservationResult Exact() => Observe(ReleaseFileState.Exact,
            ReleaseFileState.Exact);

        internal Task<PythonPublicationResult> PublishAsync(PythonIndexObservationResult initial,
            CancellationToken token) => PythonRelease.PublishAsync(Preparation, Root, initial,
            ObserveAsync, PublishControlledAsync, token);

        private Task<PythonIndexObservationResult> ObserveAsync(PreparedRelease prepared,
            CancellationToken token)
        {
            Assert.AreSame(Prepared, prepared);
            Reads++;
            if (Reads > 1) BeforeReadback?.Invoke();
            token.ThrowIfCancellationRequested();
            Assert.IsNotEmpty(Observations, "Unexpected observation beyond the bounded scenario.");
            PythonIndexObservationResult observation = Observations.Dequeue();
            AfterObserve?.Invoke();
            return Task.FromResult(observation);
        }

        private Task<PythonPublishResult> PublishControlledAsync(PreparedRelease prepared,
            string originals, string output, CancellationToken token)
        {
            Assert.AreSame(Prepared, prepared);
            Assert.AreEqual(Root, originals);
            Assert.IsFalse(token.IsCancellationRequested);
            Writes.Add(output);
            AfterPublish?.Invoke();
            token.ThrowIfCancellationRequested();
            return Task.FromResult(Invocation ?? new(new(output, Success), false,
                "Controlled result; no native publication."));
        }

        internal PythonRemoteConsumerResult[] Consumers() => Prepared.Files.Select(file =>
            new PythonRemoteConsumerResult(Prepared.Subject, file, "https://test.pypi.org/simple/",
                new("remote-" + file.Output + "-consumer", ReleaseCheckStatus.Passed),
                [new("install", Command(), Success), new("consume", Command(), Success)],
                    [])).ToArray();

        internal PythonCompletionResult Complete(PythonIndexObservationResult initial,
            PythonIndexObservationResult final, PythonPublicationResult? publication,
            PythonRemoteConsumerResult[]? consumers = null) => PythonRelease.Complete(Prepared,
                initial, final, consumers ?? Consumers(), publication);

        internal void SubstituteOriginal()
        {
            string path = Path.Combine(Root, Prepared.Files[0].Path);
            byte[] bytes = File.ReadAllBytes(path);
            bytes[0] ^= 1;
            File.WriteAllBytes(path, bytes);
        }

        private NativeCommand Command() => new("never-executed", Root, [], 30,
            new Dictionary<string, string?>(), InheritEnvironment: false);

        public void Dispose() => Directory.Delete(Root, true);
    }
}
