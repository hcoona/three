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

        PythonPublicationResult publication = await fixture.PublishAsync(initial,
            context.CancellationToken);
        PythonCompletionResult result = fixture.Complete(initial, fixture.Exact(), publication);

        AssertSequence(expected, fixture.Writes);
        AssertSequence(expected, result.Outcome.Uploads.Select(value => value.Output));
        Assert.HasCount(expected.Length, publication.Invocations);
        Assert.AreEqual(1, fixture.Reads);
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

        PythonPublicationResult publication = await fixture.PublishAsync(initial,
            context.CancellationToken);
        PythonCompletionResult result = fixture.Complete(initial, fixture.Exact(), publication);

        AssertSequence(expected, fixture.Writes);
        AssertSequence(expected, result.Outcome.Uploads.Select(value => value.Output));
        Assert.AreSame(fresh, publication.Before);
        Assert.AreEqual(1, fixture.Reads);
        Assert.AreEqual(expected.Length != 0, result.MutationAttempted);
        Assert.AreEqual(ReleaseBusinessState.Satisfied, result.Outcome.State);
    }

    [TestMethod]
    public async Task NbgvBuddyPublishesBothOriginalsOfItsUnit()
    {
        using var fixture = new Fixture(unit: PythonReleaseUnits.Nbgv);
        PythonIndexObservationResult initial = fixture.Observe();
        fixture.Observations.Enqueue(initial);
        PythonPublicationResult publication = await fixture.PublishAsync(initial,
            context.CancellationToken);
        PythonCompletionResult result = fixture.Complete(initial, fixture.Exact(), publication);
        AssertSequence(["wheel", "sdist"], fixture.Writes);
        Assert.AreEqual(PythonReleaseUnits.Nbgv, publication.Subject.Unit);
        Assert.AreEqual(ReleaseBusinessState.Satisfied, result.Outcome.State);
    }

    [TestMethod]
    [DataRow("smoke-required")]
    [DataRow("missing-result")]
    [DataRow("failed-result")]
    [DataRow("official")]
    public void ValidateNbgvRequiresAdoptedSuiteResultAndBuddy(string defect)
    {
        using var fixture = new Fixture(unit: PythonReleaseUnits.Nbgv);
        PythonRelease.Validate(fixture.Preparation);
        PreparedRelease prepared = fixture.Prepared;
        ReleaseCheck[] checks = defect switch
        {
            "failed-result" => [.. prepared.Checks.Select(check => check.Id == "tests"
                ? check with { Status = ReleaseCheckStatus.Failed } : check)],
            "missing-result" => [.. prepared.Checks.Where(check => check.Id != "tests")],
            _ => prepared.Checks,
        };
        string[] required = defect == "smoke-required"
            ? [.. prepared.RequiredChecks.Where(id => id != "tests")] : prepared.RequiredChecks;
        if (defect == "smoke-required") checks = [.. checks.Where(check => check.Id != "tests")];
        ReleaseSubject subject = defect == "official" ? prepared.Subject with
        {
            Channel = "official", Destination = "pypi", SelectedRef = "refs/heads/main",
        } : prepared.Subject;
        var preparation = fixture.Preparation with
        {
            Prepared = prepared with
            {
                Subject = subject, RequiredChecks = required, Checks = checks,
            },
        };
        Assert.ThrowsExactly<InvalidDataException>(() => PythonRelease.Validate(preparation));
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
        Assert.IsNotEmpty(publication.Errors);
        Assert.AreEqual(expected, result.Outcome.State);
        Assert.AreEqual(false, result.MutationAttempted);
        Assert.IsEmpty(result.Outcome.Uploads);
    }

    [TestMethod]
    [DataRow("Absent", "Absent", "Passed", "Unknown")]
    [DataRow("Exact", "Absent", "Passed", "Unknown")]
    [DataRow("Conflict", "Absent", "Passed", "Unsatisfied")]
    [DataRow("Absent", "Absent", "Missing", "Unsatisfied")]
    [DataRow("Exact", "Exact", "Passed", "Satisfied")]
    public async Task AcknowledgedUploadContinuesWhileIndexReadLags(string wheelName,
        string sdistName, string qualificationName, string expectedName)
    {
        ReleaseBusinessState expected = Enum.Parse<ReleaseBusinessState>(expectedName);
        using var fixture = new Fixture(unit: PythonReleaseUnits.Nbgv);
        PythonIndexObservationResult initial = fixture.Observe();
        fixture.Observations.Enqueue(initial);

        // The index still reports both files absent after the wheel upload is acknowledged.
        PythonPublicationResult publication = await fixture.PublishAsync(initial,
            context.CancellationToken);
        PythonCompletionResult result = fixture.Complete(initial, fixture.Observe(
            Enum.Parse<ReleaseFileState>(wheelName), Enum.Parse<ReleaseFileState>(sdistName),
            Enum.Parse<ReleaseCheckStatus>(qualificationName)), publication);

        AssertSequence(["wheel", "sdist"], fixture.Writes);
        Assert.AreEqual(1, fixture.Reads);
        Assert.IsEmpty(publication.Errors);
        Assert.AreEqual(expected, result.Outcome.State);
        Assert.AreEqual(true, result.MutationAttempted);
        Assert.HasCount(2, result.Outcome.Uploads);
        string reasons = string.Join(";", result.Outcome.Reasons);
        if (expected == ReleaseBusinessState.Unknown)
            Assert.Contains("not yet visible: sdist", reasons);
        else if (expected == ReleaseBusinessState.Unsatisfied)
            Assert.DoesNotContain("not yet visible", reasons);
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
        Assert.AreEqual(1, fixture.Reads);
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
    public async Task PublishAsyncInterruptedAfterAcknowledgedUploadRetainsItAndStops()
    {
        using var fixture = new Fixture();
        using var cancel =
            CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        PythonIndexObservationResult initial = fixture.Observe();
        fixture.Observations.Enqueue(initial);
        fixture.AfterAcknowledged = cancel.Cancel;

        PythonPublicationResult publication = await fixture.PublishAsync(initial,
            cancel.Token);
        PythonCompletionResult result = fixture.Complete(initial,
            fixture.Observe(ReleaseFileState.Exact), publication);

        AssertSequence(["wheel"], fixture.Writes);
        Assert.AreEqual(1, fixture.Reads);
        Assert.HasCount(1, publication.Invocations);
        Assert.Contains("interrupted", Assert.ContainsSingle(publication.Errors));
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
    [DataRow("Exact", "Exact", "Satisfied")]
    [DataRow("Absent", "Exact", "Unsatisfied")]
    [DataRow("Exact", "Absent", "Unsatisfied")]
    [DataRow("Conflict", "Exact", "Unsatisfied")]
    [DataRow("Exact", "Unknown", "Unknown")]
    public void CompleteRequiresEveryFinalIndexFileWithOriginalBytes(string wheel, string sdist,
        string expected)
    {
        using var fixture = new Fixture();

        PythonCompletionResult result = fixture.Complete(fixture.Exact(), fixture.Observe(
            Enum.Parse<ReleaseFileState>(wheel), Enum.Parse<ReleaseFileState>(sdist)), null);

        Assert.AreEqual(Enum.Parse<ReleaseBusinessState>(expected), result.Outcome.State);
        Assert.AreEqual(false, result.MutationAttempted);
        Assert.IsEmpty(result.Outcome.Uploads);
        if (expected == "Satisfied") Assert.IsEmpty(result.Outcome.Reasons);
        else Assert.IsNotEmpty(result.Outcome.Reasons);
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
    [DataRow("final-subject")]
    [DataRow("final-bytes")]
    [DataRow("publication-subject")]
    public void CompleteSubstitutedBindingsRejects(string defect)
    {
        using var fixture = new Fixture();
        PythonIndexObservationResult initial = fixture.Exact();
        PythonIndexObservationResult final = fixture.Exact();
        PythonPublicationResult? publication = null;
        ReleaseSubject other = fixture.Prepared.Subject with { Revision = new string('d', 40) };
        switch (defect)
        {
            case "final-subject":
                final = final with
                { Observation = final.Observation with { Subject = other } }; break;
            case "final-bytes":
                final.Observation.Files[0] = final.Observation.Files[0] with
                { Sha256 = new string('e', 64) }; break;
            case "publication-subject":
                initial = fixture.Observe();
                publication = new(other, initial, [], []);
                break;
        }

        Assert.ThrowsExactly<InvalidDataException>(() => fixture.Complete(initial, final,
            publication));
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
                new(new("sdist", Fixture.Success), false, "Controlled result.")], []);

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
        internal Action? AfterAcknowledged { get; set; }
        internal Action? AfterPublish { get; set; }
        internal PythonPublishResult? Invocation { get; set; }

        internal Fixture(bool dryRun = false, string unit = PythonReleaseUnits.Smoke)
        {
            Directory.CreateDirectory(Path.Combine(Root, "archives"));
            ReleaseFile[] files = [Original("wheel", "python-wheel", "product.whl",
                "wheel-original", unit),
                Original("sdist", "python-sdist", "product.tar.gz", "sdist-original", unit)];
            var subject = new ReleaseSubject(new string('a', 40), "refs/heads/development",
                unit, "release.yml", "buddy", "testpypi", dryRun);
            string[] required = unit == PythonReleaseUnits.Nbgv
                ? ["native-input", "build", "contents", "wheel-consumer", "sdist-consumer",
                    "tests", "original-bytes"]
                : ["native-input", "build", "contents", "wheel-consumer", "sdist-consumer",
                    "original-bytes"];
            var prepared = new PreparedRelease(subject, files, required,
                required.Select(id => new ReleaseCheck(id, ReleaseCheckStatus.Passed)).ToArray());
            Preparation = new(prepared, new Dictionary<string, string>
            {
                ["name"] = subject.Unit,
                ["version"] = "1.0.0",
                ["commit"] = subject.Revision,
            }, [new("build", Command(), Success)], []);
        }

        private ReleaseFile Original(string output, string kind, string name, string contents,
            string unit)
        {
            string relative = "archives/" + name;
            string path = Path.Combine(Root, relative);
            File.WriteAllText(path, contents);
            byte[] bytes = File.ReadAllBytes(path);
            return new("distributions", output, kind, unit, "1.0.0",
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
            AfterAcknowledged?.Invoke();
            return Task.FromResult(Invocation ?? new(new(output, Success), false,
                "Controlled result; no native publication."));
        }

        internal PythonCompletionResult Complete(PythonIndexObservationResult initial,
            PythonIndexObservationResult final, PythonPublicationResult? publication) =>
            PythonRelease.Complete(Prepared, initial, final, publication);

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
