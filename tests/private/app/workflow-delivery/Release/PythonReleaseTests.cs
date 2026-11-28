using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using WorkflowDelivery.Destinations;
using WorkflowDelivery.Ecosystems;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Release;

namespace WorkflowDelivery.Tests.Release;

[TestClass]
public sealed class PythonReleaseTests(TestContext context)
{
    private const ReleaseFileState Absent = ReleaseFileState.Absent;
    private const ReleaseFileState Exact = ReleaseFileState.Exact;
    private static readonly string[] Outputs = ["wheel", "sdist"];

    [TestMethod]
    [TestCategory("DeliveryTlcConformance")]
    public void PythonCallerAgreesWithPublicationWindow()
    {
        foreach (string[] row in DeliveryModelOracle.Rows("PublicationWindowOracle"))
        {
            if (row is not [var name, var before, var refreshed, var response, var delivery,
                    var verified, var issued, var returned, var verdict, var attempted])
                throw new AssertFailedException("Incomplete PublicationWindow observation.");
            using var f = new Fixture(unit: "nbgv-python", official: true);
            ReleaseObservationResult Read(string cells) =>
                f.Observe(Cell(cells[0]), Cell(cells[1]));
            ReleaseObservationResult initial = Read(before);
            ReleasePublicationResult? publication = null;
            var writes = new List<string>();
            if (ReleaseEngine.MayEnterPublisher(ReleaseAdapters.Python, f.Preparation, initial))
            {
                using var publisherOutput = new StringWriter();
                int? code = ReleaseCli.Run(f.PublishArgs(initial), publisherOutput, new(
                    ObservePython: (_, _, _) => Task.FromResult(Read(refreshed)),
                    PublishPython: (_, _, selected, _, _) =>
                    {
                        writes.Add(selected);
                        return Task.FromResult(response == "timeout" && selected == "wheel"
                            ? new PythonPublishResult(new(selected, new(NativeTermination.TimedOut,
                                null, "", "", 1, "Timeout")), false, "controlled timeout")
                            : SuccessfulUpload(selected));
                    }));
                Assert.IsTrue(code is 0 or 1, name);
                publication = Fixture.Parse(
                    publisherOutput, TransferJson.Default.ReleasePublicationResult);
            }
            if (delivery == "drop") publication = null;
            ReleaseCompletionResult result = f.Complete(initial, Read(verified), publication,
                verdict == "satisfied" ? 0 : 1);
            Assert.AreEqual(issued, string.Join(',', writes), name);
            Assert.AreEqual(
                returned, string.Join(',', result.Outcome.Uploads.Select(u => u.Output)), name);
            Assert.AreEqual(verdict, result.Outcome.State.ToString().ToLowerInvariant(), name);
            Assert.AreEqual(attempted, result.MutationAttempted switch
            {
                true => "yes",
                false => "no",
                null => "unknown",
            }, name);
        }

        static ReleaseFileState Cell(char symbol) => symbol switch
        {
            'm' => Absent,
            'e' => Exact,
            'u' => ReleaseFileState.Unknown,
            'c' => ReleaseFileState.Conflict,
            _ => throw new AssertFailedException("Unknown model cell."),
        };
    }

    [TestMethod]
    [DataRow(false, true, false, true, "sdist")]
    [DataRow(true, false, true, false, "wheel")]
    [DataRow(true, true, true, false, "wheel")]
    [DataRow(true, true, false, false, null)]
    public void PublishCreatesOnlyReviewedFilesStillAbsent(bool initialWheelMissing,
        bool initialSdistMissing, bool freshWheelMissing, bool freshSdistMissing,
        string? expectedWrite)
    {
        using var f = new Fixture();
        ReleaseObservationResult initial = f.Observe(initialWheelMissing ? Absent : Exact,
            initialSdistMissing ? Absent : Exact);
        ReleaseObservationResult fresh = f.Observe(freshWheelMissing ? Absent : Exact,
            freshSdistMissing ? Absent : Exact);
        ReleasePublicationResult publication = f.Publish(initial, fresh);
        ReleaseCompletionResult result = f.Complete(initial, f.Exact(), publication, 0);

        string[] writes = expectedWrite is null ? [] : [expectedWrite];
        CollectionAssert.AreEqual(writes, result.Outcome.Uploads.Select(x => x.Output).ToArray());
        CollectionAssert.AreEqual(fresh.Observation.Files, publication.Before.Observation.Files);
        Assert.AreEqual(expectedWrite is not null, result.MutationAttempted);
        Assert.AreEqual(ReleaseBusinessState.Satisfied, result.Outcome.State);
        Assert.IsEmpty(publication.Errors);
        Assert.IsEmpty(result.Outcome.Reasons);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void ValidateNbgvRequiresAdoptedSuiteResult(bool requiresSuite, bool failed)
    {
        using var fixture = new Fixture(unit: "nbgv-python");
        PreparedRelease prepared = fixture.Prepared;
        ReleaseCheck[] checks = failed
            ? [.. prepared.Checks.Select(check => check.Id == "tests"
                ? check with { Status = ReleaseCheckStatus.Failed } : check)]
            : [.. prepared.Checks.Where(check => check.Id != "tests")];
        var preparation = fixture.Preparation with
        {
            Prepared = prepared with
            {
                RequiredChecks = requiresSuite ? prepared.RequiredChecks
                    : [.. prepared.RequiredChecks.Where(id => id != "tests")],
                Checks = checks,
            },
        };
        using var output = new StringWriter();
        Assert.ThrowsExactly<InvalidDataException>(() => ReleaseCli.Run(fixture.PrepareArgs(),
            output, new(Prepare: (_, _) => Task.FromResult(preparation))));
    }

    [TestMethod]
    [DataRow(true, "Absent", "Absent", "Dry run cannot invoke")]
    [DataRow(false, "Exact", "Exact", "An exact request needs no publisher.")]
    [DataRow(false, "Conflict", "Absent", "cannot permit creation")]
    public void PublishRejectsBeforeOpeningPhaseScratch(bool dryRun, string wheel, string sdist,
        string diagnostic)
    {
        using var f = new Fixture(dryRun);
        Reject(f.PublishArgs(f.Observe(ParseState(wheel), ParseState(sdist))), diagnostic);
        f.AssertNoPhaseScratch();
    }

    [TestMethod]
    [DataRow("Absent", "Unknown", "Passed", "Unknown", "does not permit")]
    [DataRow("Absent", "Absent", "Missing", "Unsatisfied", "does not permit")]
    [DataRow("Exact", "Absent", "Passed", "Unsatisfied", "enlarge")]
    public void PublishUnsafeOrEnlargedStateStopsWithoutWrites(string before, string freshState,
        string qualification, string expected, string diagnostic)
    {
        using var f = new Fixture();
        ReleaseObservationResult initial = f.Observe(ParseState(before));
        ReleaseObservationResult fresh = f.Observe(ParseState(freshState),
            qualification: Enum.Parse<ReleaseCheckStatus>(qualification));
        ReleasePublicationResult publication = f.Publish(initial, fresh, expectedExit: 1);
        ReleaseCompletionResult result = f.Complete(initial, fresh, publication, 1);
        Assert.IsEmpty(publication.Steps);
        Assert.Contains(diagnostic, Assert.ContainsSingle(publication.Errors));
        Assert.IsEmpty(Directory.GetFileSystemEntries(f.PublisherTools.Scratch));
        Assert.AreEqual(Enum.Parse<ReleaseBusinessState>(expected), result.Outcome.State);
        Assert.AreEqual(false, result.MutationAttempted);
        Assert.IsEmpty(result.Outcome.Uploads);
    }

    [TestMethod]
    [DataRow("Absent", "Passed", "Unknown")]
    [DataRow("Exact", "Passed", "Unknown")]
    [DataRow("Conflict", "Passed", "Unsatisfied")]
    [DataRow("Absent", "Missing", "Unsatisfied")]
    public void CompleteRecognizesAcknowledgedUploadsMissingFromQualifiedIndex(string wheelName,
        string qualification, string expectedName)
    {
        using var f = new Fixture();
        ReleaseObservationResult initial = f.Observe();
        var publication = new ReleasePublicationResult(f.Prepared.Subject, initial,
            [WriteStep(SuccessfulUpload("wheel")), WriteStep(SuccessfulUpload("sdist"))],
            null, []);
        ReleaseFileState wheel = ParseState(wheelName);
        ReleaseBusinessState expected = Enum.Parse<ReleaseBusinessState>(expectedName);
        ReleaseCompletionResult result = f.Complete(initial, f.Observe(wheel, Absent,
            Enum.Parse<ReleaseCheckStatus>(qualification)), publication, 1);
        Assert.AreEqual(expected, result.Outcome.State);
        Assert.AreEqual(true, result.MutationAttempted);
        Assert.HasCount(2, result.Outcome.Uploads);
        if (expected == ReleaseBusinessState.Unknown)
        {
            Assert.HasCount(wheel == Exact ? 1 : 2, result.Outcome.Reasons);
            Assert.Contains("Acknowledged Python upload is not yet visible: sdist.",
                result.Outcome.Reasons);
            if (wheel == Absent)
                Assert.Contains("Acknowledged Python upload is not yet visible: wheel.",
                    result.Outcome.Reasons);
        }
        else
            Assert.DoesNotContain("not yet visible", string.Join(";", result.Outcome.Reasons));
    }

    [TestMethod]
    [DataRow("Absent", "Absent", "Unknown")]
    [DataRow("Exact", "Absent", "Unsatisfied")]
    [DataRow("Absent", "Conflict", "Unsatisfied")]
    public void CompleteAfterAmbiguousWheelDistinguishesIssuedFromUnattemptedMissingFiles(
        string finalWheel, string finalSdist, string expectedState)
    {
        using var f = new Fixture();
        ReleaseObservationResult initial = f.Observe();
        var timeout = new NativeCommandResult(NativeTermination.TimedOut, null,
            "partial", "diagnostic", 1, "controlled timeout");
        var writes = new List<string>();
        ReleasePublicationResult publication = f.Publish(initial, initial, publish: output =>
        {
            writes.Add(output);
            return new(new(output, timeout), false, "Controlled diagnostics only.");
        });

        ReleaseCompletionResult result = f.Complete(initial,
            f.Observe(ParseState(finalWheel), ParseState(finalSdist)), publication, 1);

        Assert.AreEqual("wheel", Assert.ContainsSingle(writes));
        Assert.AreEqual(timeout, Assert.ContainsSingle(result.Outcome.Uploads).Command);
        Assert.IsTrue(result.MutationAttempted);
        Assert.AreEqual(Enum.Parse<ReleaseBusinessState>(expectedState), result.Outcome.State);
    }

    [TestMethod]
    [DataRow("Exited", 17, null, false, "Exact", "Satisfied", 0)]
    [DataRow("TimedOut", null, "timeout", false, "Exact", "Satisfied", 0)]
    [DataRow("TimedOut", null, "timeout", false, "Unknown", "Unknown", 1)]
    [DataRow("Exited", 0, null, true, "Exact", "Satisfied", 0)]
    [DataRow("Exited", 0, "cleanup failed", false, "Exact", "Satisfied", 0)]
    [DataRow("Cancelled", null, "cancelled", false, "Exact", "Satisfied", 0)]
    public void PublishAbnormalInvocationStopsBeforeSdist(string termination,
        int? exitCode, string? error, bool maskFailed, string finalSdist, string expected,
        int completionExit)
    {
        using var f = new Fixture();
        ReleaseObservationResult initial = f.Observe();
        var command = new NativeCommandResult(Enum.Parse<NativeTermination>(termination),
            exitCode, "partial", "diagnostic", 0.125, error);
        var invocation = new PythonPublishResult(new("wheel", command), maskFailed,
            "Controlled diagnostics only.");
        ReleasePublicationResult publication = f.Publish(initial, initial, publish: selected =>
        {
            Assert.AreEqual("wheel", selected);
            return invocation;
        });
        ReleaseCompletionResult result = f.Complete(initial,
            f.Observe(Exact, ParseState(finalSdist)), publication, completionExit);

        Assert.AreEqual(WriteStep(invocation), Assert.ContainsSingle(publication.Steps));
        Assert.AreEqual(WriteStep(invocation), Assert.ContainsSingle(result.Publication!.Steps));
        Assert.AreEqual(command, Assert.ContainsSingle(result.Outcome.Uploads).Command);
        Assert.AreEqual(Enum.Parse<ReleaseBusinessState>(expected), result.Outcome.State);
        Assert.AreEqual(true, result.MutationAttempted);
        Assert.AreEqual(f.Prepared.Subject, result.Outcome.Subject);
        if (completionExit == 0) Assert.IsEmpty(result.Outcome.Reasons);
        else Assert.Contains("sdist", string.Join(";", result.Outcome.Reasons));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ConflictingOriginalDisprovesCompletionEvenWhenEvidenceIsMissing(bool receiptPresent)
    {
        using var f = new Fixture();
        ReleaseObservationResult initial = f.Observe();
        ReleasePublicationResult? publication = receiptPresent
            ? f.Publish(initial, f.Exact()) : null;
        ReleaseCompletionResult result = f.Complete(initial,
            f.Observe(ReleaseFileState.Conflict, ReleaseFileState.Unknown), publication, 1);
        Assert.AreEqual(ReleaseBusinessState.Unsatisfied, result.Outcome.State);
        Assert.AreEqual(receiptPresent ? false : (bool?)null, result.MutationAttempted);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PublishCancellationWithoutResultLeavesEffectsUnknown(bool duringUpload)
    {
        using var f = new Fixture();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(
            context.CancellationToken);
        ReleaseObservationResult initial = f.Observe();
        int reads = 0, writes = 0;
        OperationCanceledException error = await Assert.ThrowsExactlyAsync<
            OperationCanceledException>(() => PublishAsync(f.Preparation, f.Root,
                initial, (_, _) =>
                {
                    reads++;
                    if (!duringUpload) cancel.Cancel();
                    return Task.FromResult(initial);
                }, (_, _, output, token) =>
                {
                    Assert.AreEqual("wheel", output);
                    writes++;
                    cancel.Cancel();
                    token.ThrowIfCancellationRequested();
                    throw new AssertFailedException("Publisher token did not cancel.");
                }, cancel.Token));

        Assert.AreEqual(cancel.Token, error.CancellationToken);
        Assert.AreEqual(1, reads);
        Assert.AreEqual(duringUpload ? 1 : 0, writes);
        ReleaseCompletionResult result = f.Complete(initial, f.Exact(), null, 1);
        Assert.AreEqual(ReleaseBusinessState.Unknown, result.Outcome.State);
        Assert.IsNull(result.MutationAttempted);
        Assert.IsNull(result.Publication);
        Assert.IsEmpty(result.Outcome.Uploads);
        Assert.Contains("effects remain unknown", string.Join(";", result.Outcome.Reasons));
    }

    [TestMethod]
    public async Task PublishInterruptedAfterAcknowledgedUploadRetainsItAndStops()
    {
        using var f = new Fixture();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(
            context.CancellationToken);
        ReleaseObservationResult initial = f.Observe();
        ReleasePublicationResult publication = await PublishAsync(f.Preparation,
            f.Root, initial, (_, _) => Task.FromResult(initial), (_, _, output, token) =>
            {
                Assert.IsFalse(token.IsCancellationRequested);
                Assert.AreEqual("wheel", output);
                cancel.Cancel();
                return Task.FromResult(SuccessfulUpload(output));
            }, cancel.Token);
        ReleaseCompletionResult result = f.Complete(initial, f.Observe(Exact), publication, 1);

        Assert.AreEqual("wheel", Assert.ContainsSingle(publication.Uploads).Output);
        Assert.Contains("interrupted", Assert.ContainsSingle(publication.Errors));
        Assert.AreEqual(ReleaseBusinessState.Unsatisfied, result.Outcome.State);
        Assert.AreEqual(true, result.MutationAttempted);
        Assert.AreEqual(Fixture.Success, Assert.ContainsSingle(result.Outcome.Uploads).Command);
    }

    [TestMethod]
    public void CompleteRequiresBothFilesEvenWhenTheInitialPairWasExact()
    {
        using var fixture = new Fixture();

        ReleaseCompletionResult result = fixture.Complete(fixture.Exact(),
            fixture.Observe(Exact, ReleaseFileState.Absent), null, 1);

        Assert.AreEqual(ReleaseBusinessState.Unsatisfied, result.Outcome.State);
        Assert.AreEqual(false, result.MutationAttempted);
        Assert.IsEmpty(result.Outcome.Uploads);
        Assert.IsNotEmpty(result.Outcome.Reasons);
    }

    [TestMethod]
    public void ObserveAndPublishRejectSubstitutedOriginalBeforeOpeningScratch()
    {
        using var f = new Fixture();
        f.SubstituteOriginal();
        const string Diagnostic = "Original Python distribution bytes changed";
        Reject(f.ObserveArgs(), Diagnostic);
        Reject(f.PublishArgs(f.Observe()), Diagnostic);
        f.AssertNoPhaseScratch();
    }

    [TestMethod]
    public void PublishOriginalChangedDuringRefreshRejectsBeforeUpload() =>
        RejectAfterRefresh((fixture, initial) =>
        {
            fixture.SubstituteOriginal();
            return initial;
        });

    [TestMethod]
    public void PublishInitialObservationForAnotherRevisionRejectsBeforeRefresh()
    {
        using var f = new Fixture();
        Reject(f.PublishArgs(AnotherRevision(f.Observe())), "another request");
        f.AssertNoPhaseScratch();
    }

    [TestMethod]
    public void PublishRefreshForAnotherRevisionRejectsBeforeUpload() =>
        RejectAfterRefresh((_, initial) => AnotherRevision(initial));

    private static void RejectAfterRefresh(
        Func<Fixture, ReleaseObservationResult, ReleaseObservationResult> observe)
    {
        using var f = new Fixture();
        ReleaseObservationResult initial = f.Observe();
        using var output = new StringWriter();
        Assert.ThrowsExactly<InvalidDataException>(() => ReleaseCli.Run(
            f.PublishArgs(initial), output,
            new(ObservePython: (_, _, _) => Task.FromResult(observe(f, initial)),
            PublishPython: (_, _, _, _,
                _) => throw new AssertFailedException("Publisher reached."))));
        Assert.ContainsSingle(Directory.GetFileSystemEntries(f.IndexTools.Scratch));
        Assert.IsEmpty(output.ToString());
    }

    [TestMethod]
    public void PublishRejectsMismatchedPublisherOutput()
    {
        using var f = new Fixture();
        ReleaseObservationResult initial = f.Observe();
        using var output = new StringWriter();
        Assert.ThrowsExactly<InvalidDataException>(() => ReleaseCli.Run(
            f.PublishArgs(initial), output, new(ObservePython: (_, _,
                _) => Task.FromResult(initial),
            PublishPython: (_, _, selected, _, _) =>
            {
                Assert.AreEqual("wheel", selected);
                return Task.FromResult(SuccessfulUpload("sdist"));
            })));
        Assert.ContainsSingle(Directory.GetFileSystemEntries(f.PublisherTools.Scratch));
        Assert.IsEmpty(output.ToString());
    }

    [TestMethod]
    public void CompleteRejectsPublicationForAnotherRevision()
    {
        using var f = new Fixture();
        ReleaseObservationResult initial = f.Observe();
        var publication = new ReleasePublicationResult(AnotherRevision(initial).Observation.Subject,
            initial, [], null, []);
        Reject(f.CompleteArgs(initial, f.Exact(), publication),
            "Mismatched or unnecessary publication result.");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CompletePostAbnormalWriteSequenceRejects(bool maskFailed)
    {
        using var fixture = new Fixture();
        ReleaseObservationResult initial = fixture.Observe();
        NativeCommandResult command = maskFailed ? Fixture.Success : Fixture.Success with
        { ExitCode = 17 };
        var publication = new ReleasePublicationResult(fixture.Prepared.Subject, initial,
            [ReleaseWriteStep.FromUpload(new("wheel", command), maskFailed),
                WriteStep(SuccessfulUpload("sdist"))], null, []);

        Reject(fixture.CompleteArgs(initial, fixture.Exact(), publication),
            "Unsupported write sequence.");
    }

    [TestMethod]
    public void WholeRefreshMustFitReviewEvenIfTheReceiptOnlyClaimsAReviewedWrite()
    {
        using var f = new Fixture();
        ReleaseObservationResult reviewed = f.Observe(Absent, Exact);
        ReleaseObservationResult enlarged = f.Observe(Absent, Absent);
        var claimed = new ReleasePublicationResult(f.Prepared.Subject, enlarged,
            [WriteStep(SuccessfulUpload("wheel"))], null, []);

        Assert.ThrowsExactly<InvalidDataException>(() =>
            ReleaseEngine.Complete(
                ReleaseAdapters.Python, f.Preparation, reviewed, f.Exact(), claimed));
        Assert.ThrowsExactly<InvalidDataException>(() =>
            ReleaseEngine.AnchorAllowed(ReleaseAdapters.Python, f.Preparation, reviewed, claimed));
    }

    [TestMethod]
    public void AcknowledgedPrefixWithoutInterruptionIsNotACompletedWindow()
    {
        using var f = new Fixture();
        ReleaseObservationResult bothMissing = f.Observe(Absent, Absent);
        var prefix = new ReleasePublicationResult(f.Prepared.Subject, bothMissing,
            [WriteStep(SuccessfulUpload("wheel"))], null, []);
        Assert.ThrowsExactly<InvalidDataException>(() =>
            ReleaseEngine.Complete(
                ReleaseAdapters.Python, f.Preparation, bothMissing, f.Exact(), prefix));
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void PrepareTransfersQualifiedOrIncompleteNativeResult(bool timedOut, bool missingFiles)
    {
        using var f = new Fixture();
        var command = timedOut ? Fixture.Success with
        {
            Termination = NativeTermination.TimedOut,
            ExitCode = null,
            Stdout = "partial preparation",
            Stderr = "deadline reached",
            Error = "actual timeout"
        } : Fixture.Success;
        ReleasePreparationResult preparation = f.Preparation with
        {
            Prepared = missingFiles ? f.Prepared with { Files = [] } : f.Prepared,
            Commands = [f.Preparation.Commands[0] with { Result = command }],
            Errors = timedOut && !missingFiles ? ["actual timeout"] : [],
        };
        ReleasePreparationResult result = f.Prepare(preparation, timedOut ? 1 : 0);
        Assert.AreEqual(f.Prepared.Subject, result.Prepared.Subject);
        CollectionAssert.AreEqual(preparation.Prepared.Files, result.Prepared.Files);
        CollectionAssert.AreEqual(f.Prepared.RequiredChecks, result.Prepared.RequiredChecks);
        CollectionAssert.AreEqual(f.Prepared.Checks, result.Prepared.Checks);
        CollectionAssert.AreEquivalent(preparation.Native.ToArray(), result.Native.ToArray());
        ReleaseCommand receipt = Assert.ContainsSingle(result.Commands);
        Assert.AreEqual("build", receipt.Check);
        Assert.AreEqual(command, receipt.Result);
        CollectionAssert.AreEqual(preparation.Errors, result.Errors);
    }

    [TestMethod]
    [DataRow("one-file")]
    [DataRow("name")]
    [DataRow("command-error")]
    public void ReceiveRejectsUnqualifiedPreparation(string defect)
    {
        using var f = new Fixture();
        ReleaseSubject subject = f.Prepared.Subject with
        {
            Declaration = ReleaseUnitCatalog.Python(f.Prepared.Subject.Unit).Declaration,
        };
        PreparedRelease prepared = f.Prepared with { Subject = subject };
        var qualified = f.Preparation with { Prepared = prepared };
        ReleasePreparationResult receipt = defect switch
        {
            "one-file" => qualified with
            { Prepared = prepared with { Files = prepared.Files[..1] } },
            "command-error" => qualified with
            {
                Commands = [qualified.Commands[0] with
                {
                    Result = Fixture.Success with { Error = "cleanup failed" },
                }],
            },
            _ => qualified with { Native = new(qualified.Native) { ["name"] = "substituted" } },
        };

        string transfer = Directory.CreateDirectory(Path.Combine(f.Root, "prepared")).FullName;
        File.WriteAllText(Path.Combine(transfer, "preparation.json"),
            JsonSerializer.Serialize(receipt, TransferJson.Default.ReleasePreparationResult));
        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["RUNNER_TEMP"] = f.Root,
            ["DESTINATION"] = subject.Destination,
            ["DRY_RUN"] = "false",
            ["UNIT"] = subject.Unit,
            ["GITHUB_SHA"] = subject.Revision,
            ["GITHUB_REF"] = subject.SelectedRef,
            ["GITHUB_RUN_ATTEMPT"] = "1",
        };
        InvalidDataException error = Assert.ThrowsExactly<InvalidDataException>(() =>
            ReleaseJobInputs.Run(["release", "job-inputs", "python", "receive", "observation"],
                name => environment.GetValueOrDefault(name)));
        Assert.AreEqual("Incomplete or unsupported Python preparation.", error.Message);
        Assert.IsFalse(Directory.Exists(Path.Combine(f.Root, "observation")));
    }

    [TestMethod]
    public void PrepareRejectsMissingSubjectRevisionBeforeNativeWork()
    {
        using var f = new Fixture();
        RejectEditedTransfer(f.PrepareArgs(), input =>
            input["subject"]!.AsObject().Remove("revision"), "revision");
    }

    [TestMethod]
    public void PrepareIndexToolsRejectsMissingRequirementsBeforeNativeWork()
    {
        using var f = new Fixture();
        var request = new PythonIndexToolRequest(f.PublisherTools.Uv, f.IndexTools.Interpreter,
            f.Root, f.Root);
        RejectEditedTransfer(["release", "prepare-python-index-tools",
            f.Save(request, DestinationToolJson.Default.PythonIndexToolRequest)],
            input => input.Remove("requirements"),
            "requirements");
    }

    [TestMethod]
    public void DescribeRejectsUnknownNestedFileProperty()
    {
        using var f = new Fixture(dryRun: true);
        RejectEditedTransfer(f.DescribeArgs(f.Observe()),
            input => input["observation"]!["files"]![0]!["unexpected"] = true, "unexpected");
    }

    [TestMethod]
    [DataRow("true", true)]
    [DataRow("false", false)]
    [DataRow(null, false)]
    public void LiveOfficialPreparationRequiresNativePublicRelease(string? release,
        bool valid)
    {
        using var f = new Fixture(official: true);
        var native = new Dictionary<string, string>(f.Preparation.Native);
        if (release is not null) native["public-release"] = release;
        ReleasePreparationResult official = f.Preparation with { Native = native };
        if (valid) f.Prepare(official, 0);
        else
        {
            using var output = new StringWriter();
            Assert.ThrowsExactly<InvalidDataException>(() => ReleaseCli.Run(
                f.PrepareArgs(), output, new(Prepare: (_, _) => Task.FromResult(official))));
        }
    }

    [TestMethod]
    public void DryDescribeDoesNotInvokePublisher()
    {
        using var f = new Fixture(dryRun: true);
        ReleaseDryRun result = Run(f.DescribeArgs(f.Observe(Exact)), 0,
            TransferJson.Default.ReleaseDryRun);
        Assert.AreEqual(f.Prepared.Subject, result.Subject);
        Assert.IsTrue(result.Ready);
        Assert.AreEqual("sdist", Assert.ContainsSingle(result.HypotheticalWrites));
        Assert.IsEmpty(result.Reasons);
        f.AssertNoPhaseScratch();
        Reject(f.CompleteArgs(f.Observe(), f.Exact()),
            "Dry run cannot produce a live completion result.");
    }

    [TestMethod]
    public void ObserveBindsPreparedSubjectAndIndexTools()
    {
        using var f = new Fixture();
        using var output = new StringWriter();
        Assert.AreEqual(0, ReleaseCli.Run(f.ObserveArgs(), output, new(ObservePython: (prepared,
            tools,
            token) =>
        {
            Assert.AreEqual(f.Prepared.Subject, prepared.Subject);
            CollectionAssert.AreEqual(f.Prepared.Files, prepared.Files);
            Assert.AreEqual(f.IndexTools, tools);
            Assert.IsFalse(token.IsCancellationRequested);
            return Task.FromResult(f.Observe());
        })));
        var result = Fixture.Parse(output, TransferJson.Default.ReleaseObservationResult);
        Assert.AreEqual(f.Prepared.Subject, result.Observation.Subject);
        CollectionAssert.AreEqual(f.Observe().Observation.Files, result.Observation.Files);
        Assert.AreEqual("controlled read", Assert.ContainsSingle(result.Reads).Detail);
    }

    [TestMethod]
    [DataRow("hcoona-release-smoke-python", false)]
    [DataRow("nbgv-python", true)]
    public void PublishUsesFreshPhaseScratchAndRetainsOriginalWrites(string unit, bool official)
    {
        // The static version needs no public-release fact in either channel (WD-REL-001).
        using var f = new Fixture(unit: unit, official: official);
        f.Prepare(f.Preparation, 0);
        var scratches = new HashSet<string>(StringComparer.Ordinal);
        var writes = new List<string>();
        int reads = 0;
        using var output = new StringWriter();
        Assert.AreEqual(0, ReleaseCli.Run(f.PublishArgs(f.Observe()), output,
            new(ObservePython: (prepared, tools, _) =>
            {
                Assert.AreEqual(f.Prepared.Subject, prepared.Subject);
                Assert.AreEqual(f.IndexTools.Interpreter, tools.Interpreter);
                Assert.AreEqual(f.IndexTools.Helper, tools.Helper);
                Fresh(tools.Scratch, f.IndexTools.Scratch);
                reads++;
                return Task.FromResult(f.Observe());
            }, PublishPython: (prepared, originals, selected, tools, _) =>
            {
                Assert.AreEqual(f.Prepared.Subject, prepared.Subject);
                Assert.AreEqual(f.Root, originals);
                Assert.AreEqual(f.PublisherTools.Uv, tools.Uv);
                Fresh(tools.Scratch, f.PublisherTools.Scratch);
                ReleaseFile file = prepared.Files.Single(v => v.Output == selected);
                byte[] bytes = File.ReadAllBytes(Path.Combine(originals, file.Path));
                Assert.AreEqual(file.Size, bytes.LongLength);
                Assert.AreEqual(file.Sha256, Convert.ToHexStringLower(SHA256.HashData(bytes)));
                writes.Add(selected);
                return Task.FromResult(SuccessfulUpload(selected));
            })));
        var result = Fixture.Parse(output, TransferJson.Default.ReleasePublicationResult);
        CollectionAssert.AreEqual(Outputs, writes);
        CollectionAssert.AreEqual(writes.ToArray(), result.Uploads.Select(upload =>
            upload.Output).ToArray());
        Assert.AreEqual(1, reads);
        Assert.HasCount(3, scratches);
        Assert.IsEmpty(result.Errors);
        Assert.AreEqual(ReleaseFileState.Absent, result.Before.Observation.Files[0].State);
        Assert.AreEqual(f.Prepared.Subject, result.Subject);
        void Fresh(string scratch, string parent)
        {
            Assert.AreEqual(parent, Path.GetDirectoryName(scratch));
            Assert.IsTrue(Directory.Exists(scratch));
            Assert.IsEmpty(Directory.GetFileSystemEntries(scratch));
            Assert.IsTrue(scratches.Add(scratch), "Phase reused scratch.");
            File.WriteAllText(Path.Combine(scratch, "used"), "owned phase state");
        }
    }

    [TestMethod]
    [TestCategory("PythonNativeContract")]
    [DoNotParallelize]
    [OSCondition(OperatingSystems.Linux)]
    public void PublishForwardsNativeMasksOnlyToStderr()
    {
        using var f = new Fixture();
        string originals = Directory.CreateDirectory(Path.Combine(f.Root, "originals")).FullName;
        Directory.Move(Path.Combine(f.Root, "archives"), Path.Combine(originals, "archives"));
        const string StdoutMask = "::add-mask::controlled-cli-stdout";
        const string StderrMask = "::add-mask::controlled-cli-stderr";
        File.WriteAllText(f.PublisherTools.Uv, $"""
            #!/bin/sh
            printf '%s\n' '{StdoutMask}'
            printf '%s\n' '{StderrMask}' >&2
            """);
        if (OperatingSystem.IsLinux())
            File.SetUnixFileMode(f.PublisherTools.Uv,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        ReleaseObservationResult initial = f.Observe(ReleaseFileState.Exact);
        string[] args = f.PublishArgs(initial, originals);
        var platform = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["GITHUB_ACTIONS"] = "true",
            ["ACTIONS_ID_TOKEN_REQUEST_URL"] = "https://oidc.invalid/token",
            ["ACTIONS_ID_TOKEN_REQUEST_TOKEN"] = "controlled-cli-oidc",
        };
        var previous = platform.Keys.ToDictionary(name => name,
            Environment.GetEnvironmentVariable, StringComparer.Ordinal);
        using var output = new StringWriter();
        using var error = new StringWriter();
        TextWriter previousOutput = Console.Out;
        TextWriter previousError = Console.Error;
        int? exit;
        try
        {
            foreach ((string name, string value) in platform)
                Environment.SetEnvironmentVariable(name, value);
            Console.SetOut(output);
            Console.SetError(error);
            exit = ReleaseCli.Run(args, Console.Out,
                new(ObservePython: (_, _, _) => Task.FromResult(initial)));
        }
        finally
        {
            Console.SetOut(previousOutput);
            Console.SetError(previousError);
            foreach ((string name, string? value) in previous)
                Environment.SetEnvironmentVariable(name, value);
        }

        Assert.AreEqual(0, exit);
        var result = Fixture.Parse(output, TransferJson.Default.ReleasePublicationResult);
        ReleaseWriteStep invocation = Assert.ContainsSingle(result.Steps);
        Assert.AreEqual("sdist", invocation.Target);
        Assert.IsTrue(invocation.Succeeded);
        Assert.IsFalse(invocation.MaskForwardingFailed);
        Assert.DoesNotContain("::add-mask::", output.ToString());
        Assert.DoesNotContain("controlled-cli-", output.ToString());
        Assert.AreEqual(StdoutMask + Environment.NewLine + StderrMask + Environment.NewLine,
            error.ToString());
    }

    [TestMethod]
    public void ObserveRejectsResultForAnotherRevision()
    {
        using var f = new Fixture();
        ReleaseObservationResult observed = AnotherRevision(f.Exact());
        using var output = new StringWriter();
        Assert.ThrowsExactly<InvalidDataException>(() => ReleaseCli.Run(f.ObserveArgs(),
            output, new(ObservePython: (_, _, _) => Task.FromResult(observed))));
        Assert.IsEmpty(output.ToString());
    }

    private static ReleaseObservationResult AnotherRevision(ReleaseObservationResult result)
        => result with
        {
            Observation = result.Observation with
            { Subject = result.Observation.Subject with { Revision = new('b', 40) } }
        };

    private static void Reject(string[] args, string diagnostic)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.AreEqual(2, Program.Run(args, output, error));
        Assert.IsEmpty(output.ToString());
        Assert.Contains(diagnostic, error.ToString());
    }

    private static void RejectEditedTransfer(string[] args, Action<JsonObject> edit,
        string diagnostic)
    {
        JsonObject input = JsonNode.Parse(File.ReadAllText(args[^1]))!.AsObject();
        edit(input);
        File.WriteAllText(args[^1], input.ToJsonString());
        Reject(args, diagnostic);
    }

    private static T Run<T>(string[] args, int expectedExit, JsonTypeInfo<T> type)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.AreEqual(expectedExit, Program.Run(args, output, error), error.ToString());
        Assert.IsEmpty(error.ToString());
        return Fixture.Parse(output, type);
    }

    private static ReleaseFileState ParseState(string state) => Enum.Parse<ReleaseFileState>(state);

    private static PythonPublishResult SuccessfulUpload(string output) =>
        new(new(output, Fixture.Success), false, "controlled publisher");

    private static ReleaseWriteStep WriteStep(PythonPublishResult invocation) =>
        ReleaseWriteStep.FromUpload(invocation.Upload, invocation.MaskForwardingFailed);

    private static Task<ReleasePublicationResult> PublishAsync(
        ReleasePreparationResult preparation, string originals, ReleaseObservationResult initial,
        Func<PreparedRelease, CancellationToken, Task<ReleaseObservationResult>> observe,
        Func<PreparedRelease, string, string, CancellationToken, Task<PythonPublishResult>> publish,
        CancellationToken token) =>
        ReleaseEngine.PublishAsync(ReleaseAdapters.Python, preparation, originals, initial,
            DestinationPublishPlan.Files(preparation.Prepared, observe, async (file, cancel) =>
                WriteStep(await publish(preparation.Prepared, originals, file.Output, cancel))),
            token);

    private sealed class Fixture : IDisposable
    {
        internal static readonly NativeCommandResult Success = new(NativeTermination.Exited,
            0, "controlled output", "", 0.125, null);
        internal string Root { get; } = Directory.CreateTempSubdirectory("python-release-")
            .FullName;
        internal ReleasePreparationResult Preparation { get; }
        internal PreparedRelease Prepared => Preparation.Prepared;
        internal PythonIndexTools IndexTools { get; }
        internal PythonPublisherTools PublisherTools { get; }

        internal Fixture(bool dryRun = false, string unit = "hcoona-release-smoke-python",
            bool official = false)
        {
            Directory.CreateDirectory(Path.Combine(Root, "archives"));
            ReleaseFile[] files = [Original("wheel", "python-wheel", "product.whl",
                "wheel-original", unit),
                Original("sdist", "python-sdist", "product.tar.gz", "sdist-original", unit)];
            var subject = new ReleaseSubject(new('a', 40),
                official ? "refs/heads/main" : "refs/heads/development", unit, "release.yml",
                official ? "official" : "buddy", official ? "pypi" : "testpypi", dryRun);
            string[] required = unit == "nbgv-python"
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
            IndexTools = new(Path.Combine(Root, "python"), Path.Combine(Root, "packages.py"),
                Directory.CreateDirectory(Path.Combine(Root, "index")).FullName);
            PublisherTools = new(Path.Combine(Root, "uv"),
                Directory.CreateDirectory(Path.Combine(Root, "publisher")).FullName);
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

        internal ReleaseObservationResult Observe(ReleaseFileState wheel =
            ReleaseFileState.Absent,
            ReleaseFileState sdist = ReleaseFileState.Absent,
            ReleaseCheckStatus qualification = ReleaseCheckStatus.Passed) => new(
            new(Prepared.Subject, Prepared.Files.Select(file => new ReleaseFileObservation(
                file.Output, file.Name, file.Version, file.Output == "wheel" ? wheel : sdist,
                file.Size, file.Sha256)).ToArray(), ["public-index-files"],
                [new("public-index-files", qualification)]),
            [new("index", 200, "controlled read")]);

        internal ReleaseObservationResult Exact() => Observe(PythonReleaseTests.Exact,
            PythonReleaseTests.Exact);

        internal ReleaseCompletionResult Complete(ReleaseObservationResult initial,
            ReleaseObservationResult final, ReleasePublicationResult? publication, int exit) =>
            Run(CompleteArgs(initial, final, publication), exit,
                TransferJson.Default.ReleaseCompletionResult);

        internal ReleasePublicationResult Publish(ReleaseObservationResult initial,
            ReleaseObservationResult fresh, int expectedExit = 0,
            Func<string, PythonPublishResult>? publish = null)
        {
            using var output = new StringWriter();
            Assert.AreEqual(expectedExit, ReleaseCli.Run(PublishArgs(initial), output,
                new(ObservePython: (_, _, _) => Task.FromResult(fresh),
                PublishPython: (_, _, selected, _, _) =>
                    Task.FromResult(publish?.Invoke(selected) ?? SuccessfulUpload(selected)))));
            Assert.ContainsSingle(Directory.GetFileSystemEntries(IndexTools.Scratch));
            return Parse(output, TransferJson.Default.ReleasePublicationResult);
        }

        internal ReleasePreparationResult Prepare(ReleasePreparationResult preparation, int exit)
        {
            using var output = new StringWriter();
            Assert.AreEqual(exit, ReleaseCli.Run(PrepareArgs(), output,
                new(Prepare: (request, _) =>
                {
                    Assert.AreEqual(PreparationRequest, request);
                    return Task.FromResult(preparation);
                })));
            return Parse(output, TransferJson.Default.ReleasePreparationResult);
        }

        internal void AssertNoPhaseScratch()
        {
            Assert.IsEmpty(Directory.GetFileSystemEntries(IndexTools.Scratch));
            Assert.IsEmpty(Directory.GetFileSystemEntries(PublisherTools.Scratch));
        }

        internal void SubstituteOriginal()
        {
            string path = Path.Combine(Root, Prepared.Files[0].Path);
            byte[] bytes = File.ReadAllBytes(path);
            bytes[0] ^= 1;
            File.WriteAllBytes(path, bytes);
        }

        private NativeCommand Command() => new("never-executed", Root, [], 30,
            new Dictionary<string, string?>(), InheritEnvironment: false);

        internal string Save<T>(T value, JsonTypeInfo<T> type)
        {
            string path = Path.Combine(Root, Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(path, JsonSerializer.Serialize(value, type));
            return path;
        }
        internal static T Parse<T>(StringWriter output, JsonTypeInfo<T> type) =>
            JsonSerializer.Deserialize(output.ToString(), type) ?? throw new
                AssertFailedException("Missing typed CLI output.");

        private ReleasePreparationRequest PreparationRequest =>
            new(Prepared.Subject, Root, Root, MiseDataDirectory: Root);
        internal string[] PrepareArgs() => ["release", "prepare-python", Save(PreparationRequest,
            TransferJson.Default.ReleasePreparationRequest)];
        internal string[] DescribeArgs(ReleaseObservationResult observation) =>
            ["release", "describe-python", PreparationPath(), ObservationPath(observation)];
        internal string[] ObserveArgs() => ["release", "observe-python", PreparationPath(),
            Root, Save(IndexTools,
            DestinationToolJson.Default.PythonIndexTools)];
        internal string[] PublishArgs(ReleaseObservationResult initial,
            string? originals = null) => ["release", "publish-python", PreparationPath(),
            originals ?? Root, ObservationPath(initial), Save(IndexTools,
            DestinationToolJson.Default.PythonIndexTools), Save(PublisherTools,
            DestinationToolJson.Default.PythonPublisherTools)];
        internal string[] CompleteArgs(ReleaseObservationResult initial,
            ReleaseObservationResult final, ReleasePublicationResult? publication = null) =>
            ["release", "complete-python", PreparationPath(), ObservationPath(initial),
                ObservationPath(final), publication is null ? "-" :
                    Save(publication, TransferJson.Default.ReleasePublicationResult)];
        private string PreparationPath() => Save(Preparation,
            TransferJson.Default.ReleasePreparationResult);
        private string ObservationPath(ReleaseObservationResult observation) =>
            Save(observation, TransferJson.Default.ReleaseObservationResult);

        public void Dispose() => Directory.Delete(Root, true);
    }
}
