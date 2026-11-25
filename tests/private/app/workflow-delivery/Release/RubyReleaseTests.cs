using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using System.Text.Json;
using WorkflowDelivery.Destinations;
using WorkflowDelivery.Ecosystems;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Release;

namespace WorkflowDelivery.Tests.Release;

[TestClass]
public sealed class RubyReleaseTests(TestContext context)
{
    [TestMethod]
    public void ValidateCompletePreparationBindsPortableOriginal()
    {
        using var fixture = new RubyCallerFixture();
        RubyRelease.Validate(fixture.Preparation);
        RubyRelease.Originals(fixture.Preparation, fixture.Root);
        var transfer = JsonSerializer.Deserialize(JsonSerializer.Serialize(fixture.Preparation,
            TransferJson.Default.RubyPreparationResult),
                TransferJson.Default.RubyPreparationResult)!;
        Assert.AreEqual(fixture.Prepared.Subject, transfer.Prepared.Subject);
        Assert.AreEqual(fixture.Original, Assert.ContainsSingle(transfer.Prepared.Files));
        CollectionAssert.AreEqual(fixture.Prepared.RequiredChecks,
            transfer.Prepared.RequiredChecks);
        CollectionAssert.AreEqual(fixture.Prepared.Checks, transfer.Prepared.Checks);
        Assert.AreEqual(fixture.Prepared.Subject.Revision, transfer.Native["commit"]);
        Assert.AreEqual(fixture.Original.Sha256,
            Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(fixture.Archive))));
        Assert.AreEqual(fixture.Preparation.Commands[0].Result, transfer.Commands[0].Result);
        Assert.IsEmpty(transfer.Errors);
    }

    [TestMethod]
    [DataRow("name")]
    [DataRow("version")]
    [DataRow("platform")]
    [DataRow("commit")]
    public void ValidateRejectsChangedNativeFact(string fact) =>
        RejectPreparation(result => result with
        { Native = new(result.Native) { [fact] = "another-native-value" } });

    [TestMethod]
    public void ValidateRejectsMissingCheck() => RejectPreparation(result => result with
    {
        Prepared = result.Prepared with { Checks = result.Prepared.Checks[..^1] },
    });

    [TestMethod]
    public void ValidateRejectsMissingCommand() =>
        RejectPreparation(result => result with { Commands = [] });

    [TestMethod]
    public void ValidateRejectsCommandWithError() => RejectPreparation(result => result with
    {
        Commands = [result.Commands[0] with
        { Result = RubyCallerFixture.Command() with { Error = "Controlled error" } }],
    });

    [TestMethod]
    public void ValidateRejectsExtraOriginal() => RejectPreparation(result =>
    {
        ReleaseFile original = result.Prepared.Files[0];
        return result with
        {
            Prepared = result.Prepared with
            {
                Files = [original, original with
                { Output = "extra", Path = "extra.gem", Version = "2.0.0" }]
            }
        };
    });

    private static void RejectPreparation(Func<RubyPreparationResult, RubyPreparationResult> change)
    {
        using var fixture = new RubyCallerFixture();
        RubyPreparationResult result = change(fixture.Preparation);
        Assert.ThrowsExactly<InvalidDataException>(() => RubyRelease.Validate(result));
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void ValidateLatexmathRequiresAdoptedSuiteResult(bool requiresSuite, bool failed)
    {
        using var fixture = new RubyCallerFixture(unit: RubyReleaseUnits.Latexmath);
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
        Assert.ThrowsExactly<InvalidDataException>(() => RubyRelease.Validate(preparation));
    }

    [TestMethod]
    [DataRow("official", "rubygems", "refs/tags/preview", true, false, true)]
    [DataRow("buddy", "rubygems", "refs/heads/main", true, true, false)]
    [DataRow("buddy", "github-packages-ruby", "refs/heads/topic", false, false, true,
        RubyReleaseUnits.Latexmath)]
    [DataRow("official", "rubygems", "refs/heads/main", false, false, false,
        RubyReleaseUnits.Latexmath)]
    public void ValidateChannelRefAndDryEligibility(string channel, string destination,
        string selectedRef, bool dry, bool publicRelease, bool accepted,
        string unit = RubyReleaseUnits.Smoke)
    {
        using var fixture = new RubyCallerFixture(channel, dry, unit);
        var preparation = fixture.Preparation with
        {
            Prepared = fixture.Prepared with
            {
                Subject = fixture.Prepared.Subject with
                {
                    Destination
                = destination,
                    SelectedRef = selectedRef
                }
            },
            Native = new(fixture.Preparation.Native)
            {
                ["public-release"] = publicRelease ? "true" :
                "false"
            },
        };
        if (accepted) RubyRelease.Validate(preparation);
        else Assert.ThrowsExactly<InvalidDataException>(() => RubyRelease.Validate(preparation));
        Assert.AreEqual(selectedRef, preparation.Prepared.Subject.SelectedRef);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PublishSubstitutedOriginalStopsBeforeWrite(bool duringRefresh)
    {
        using var fixture = new RubyCallerFixture();
        if (!duringRefresh) File.AppendAllText(fixture.Archive, "substitution");
        int reads = 0, writes = 0;
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => RubyRelease.PublishAsync(
            fixture.Preparation, fixture.Root, fixture.Observe(ReleaseFileState.Absent),
            (_, _) =>
            {
                reads++; File.AppendAllText(fixture.Archive, "substitution"); return
                Task.FromResult(fixture.Observe(ReleaseFileState.Absent));
            },
            (_, _, _) =>
            {
                writes++; throw new
                AssertFailedException("Substituted bytes cannot be written.");
            },
                context.CancellationToken));
        Assert.AreEqual(duringRefresh ? 1 : 0, reads);
        Assert.AreEqual(0, writes);
    }

    [TestMethod]
    [DataRow("Exact", true)]
    [DataRow("Conflict", false)]
    public async Task PublishRefreshedStateSkipsMutation(string stateName, bool satisfied)
    {
        using var fixture = new RubyCallerFixture();
        RubyObservationResult initial = fixture.Observe(ReleaseFileState.Absent);
        RubyObservationResult fresh = fixture.Observe(Enum.Parse<ReleaseFileState>(stateName));
        int writes = 0;
        RubyPublicationResult publication = await RubyRelease.PublishAsync(fixture.Preparation,
            fixture.Root, initial,
            (_, _) => Task.FromResult(fresh), (_, _, _) =>
            {
                writes++; throw new
                AssertFailedException("Refresh forbids mutation.");
            }, context.CancellationToken);
        Assert.AreEqual(0, writes);
        Assert.AreSame(fresh, publication.Before);
        Assert.IsNull(publication.Invocation);
        Assert.IsNull(publication.Readback);
        Assert.AreEqual(!satisfied, publication.Errors.Length != 0);
        if (satisfied)
        {
            RubyCompletionResult result = RubyRelease.Complete(fixture.Prepared, initial, fresh,
                publication);
            Assert.AreEqual(ReleaseBusinessState.Satisfied, result.Outcome.State);
            Assert.AreEqual(false, result.MutationAttempted);
            Assert.IsEmpty(result.Outcome.Uploads);
        }
    }

    [TestMethod]
    public async Task PublishReadbackCancellationRetainsInvocationForFreshVerification()
    {
        using var fixture = new RubyCallerFixture();
        using var cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        RubyObservationResult absent = fixture.Observe(ReleaseFileState.Absent);
        RubyPublishResult attempt =
            fixture.Publish(RubyCallerFixture.Command(NativeTermination.Cancelled, 17));
        int reads = 0, writes = 0;
        RubyPublicationResult publication = await RubyRelease.PublishAsync(fixture.Preparation,
            fixture.Root, absent,
            (_, token) =>
            {
                if (reads++ != 0) token.ThrowIfCancellationRequested(); return
                Task.FromResult(absent);
            },
            (_, _, _) => { writes++; cancellation.Cancel(); return Task.FromResult(attempt); },
                cancellation.Token);
        Assert.AreEqual(1, writes);
        Assert.AreEqual(2, reads);
        Assert.AreSame(attempt, publication.Invocation);
        Assert.IsNull(publication.Readback);
        Assert.Contains("attempted effects retained", Assert.ContainsSingle(publication.Errors));
        RubyCompletionResult complete = RubyRelease.Complete(fixture.Prepared, absent,
            fixture.Observe(ReleaseFileState.Exact), publication);
        Assert.AreEqual(ReleaseBusinessState.Satisfied, complete.Outcome.State);
        Assert.AreEqual(true, complete.MutationAttempted);
        Assert.AreSame(attempt.Upload, Assert.ContainsSingle(complete.Outcome.Uploads));
        Assert.AreEqual(NativeTermination.Cancelled,
            complete.Outcome.Uploads[0].Command.Termination);
    }

    [TestMethod]
    public void CompleteRejectsPublicationForAnotherRevision() =>
        RejectPublication(publication => publication with
        { Subject = publication.Subject with { Revision = new('b', 40) } });

    [TestMethod]
    public void CompleteRejectsPublicationWithDifferentOutput() =>
        RejectPublication(publication => publication with
        {
            Invocation = publication.Invocation! with
            { Upload = publication.Invocation.Upload with { Output = "another-output" } }
        });

    [TestMethod]
    public void CompleteRejectsWriteAfterExactObservation() =>
        RejectPublication(publication => publication with { Before = publication.Readback! });

    [TestMethod]
    public void CompleteRejectsReadbackWithoutInvocation() =>
        RejectPublication(publication => publication with { Invocation = null });

    [TestMethod]
    public void CompleteRejectsInvalidInvocationDuration() =>
        RejectPublication(publication =>
        {
            RubyPublishResult invocation = publication.Invocation!;
            return publication with
            {
                Invocation = invocation with
                {
                    Upload = invocation.Upload with
                    { Command = invocation.Upload.Command with { DurationSeconds = double.NaN } }
                }
            };
        });

    private static void RejectPublication(Func<RubyPublicationResult, RubyPublicationResult> change)
    {
        using var fixture = new RubyCallerFixture();
        RubyObservationResult absent = fixture.Observe(ReleaseFileState.Absent), exact =
            fixture.Observe(ReleaseFileState.Exact);
        RubyPublicationResult publication = change(new(fixture.Prepared.Subject, absent,
            fixture.Publish(RubyCallerFixture.Command()), exact, []));
        Assert.ThrowsExactly<InvalidDataException>(() => RubyRelease.Complete(fixture.Prepared,
            absent, exact, publication));
    }

    [TestMethod]
    public async Task PublishCancellationBeforeInvocationPreventsWrite()
    {
        using var fixture = new RubyCallerFixture();
        using var cancelled =
            CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        int writes = 0;
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            RubyRelease.PublishAsync(fixture.Preparation, fixture.Root,
            fixture.Observe(ReleaseFileState.Absent), (_, _) =>
            {
                cancelled.Cancel();
                return Task.FromResult(fixture.Observe(ReleaseFileState.Absent));
            },
            (_, _, _) =>
            {
                writes++; throw new
                AssertFailedException("Cancellation must stop mutation.");
            }, cancelled.Token));
        Assert.AreEqual(0, writes);
    }

    [TestMethod]
    public async Task DryRunExcludesPublisherAndLiveCompletion()
    {
        using var fixture = new RubyCallerFixture(dry: true);
        int calls = 0;
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            RubyRelease.PublishAsync(fixture.Preparation, fixture.Root,
            fixture.Observe(ReleaseFileState.Absent),
            (_, _) =>
            {
                calls++; throw new
                AssertFailedException("Dry run cannot refresh for push.");
            },
            (_, _, _) => { calls++; throw new AssertFailedException("Dry run cannot push."); },
                context.CancellationToken));
        RubyObservationResult exact = fixture.Observe(ReleaseFileState.Exact);
        Assert.ThrowsExactly<InvalidDataException>(() => RubyRelease.Complete(fixture.Prepared,
            exact, exact, null));
        Assert.AreEqual(0, calls);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void PrepareTypedJsonRetainsQualifiedOrFailedCommand(bool failed)
    {
        using var fixture = new RubyCallerFixture();
        var request = new RubyPreparationRequest(fixture.Prepared.Subject, fixture.Root,
            fixture.Directory("preparation"),
            new(Path.Combine(fixture.Root, "ruby"), Path.Combine(fixture.Root, "gem"),
                Path.Combine(fixture.Root, "dotnet"), Path.Combine(fixture.Root, "mise-bin")),
            Path.Combine(fixture.Root, "mise"));
        string path = fixture.Write("request.json", request,
            TransferJson.Default.RubyPreparationRequest);
        RubyPreparationResult native = fixture.Preparation;
        if (failed) native = native with
        {
            Errors = ["Controlled build failure"],
            Commands =
            [native.Commands[0] with { Result = RubyCallerFixture.Command(NativeTermination.Exited,
            17) }]
        };
        int calls = 0;
        using var output = new StringWriter();
        int? exit = RubyReleaseCli.Run(["release", "prepare-ruby", path], output, prepare: (actual,
            _) =>
        {
            calls++;
            Assert.AreEqual(request, actual);
            return Task.FromResult(native);
        });
        Assert.AreEqual(failed ? 1 : 0, exit);
        Assert.AreEqual(1, calls);
        RubyPreparationResult result = JsonSerializer.Deserialize(output.ToString(),
            TransferJson.Default.RubyPreparationResult)!;
        Assert.AreEqual(request.Subject, result.Prepared.Subject);
        Assert.AreEqual(fixture.Original, Assert.ContainsSingle(result.Prepared.Files));
        Assert.AreEqual(native.Commands[0].Result, result.Commands[0].Result);
        Assert.AreEqual(failed ? 17 : 0, result.Commands[0].Result.ExitCode);
        CollectionAssert.AreEqual(native.Errors, result.Errors);
        CollectionAssert.AreEqual(native.Prepared.Checks, result.Prepared.Checks);
    }

    [TestMethod]
    [DataRow("Exact", 0)]
    [DataRow("Unknown", 1)]
    public void ObserveTypedJsonPreservesNativeState(string stateName, int expectedExit)
    {
        using var fixture = new RubyCallerFixture("official");
        var tools = new RubySourceTools(Path.Combine(fixture.Root, "ruby"),
            Path.Combine(fixture.Root, "source.rb"), fixture.Directory("read"));
        RubyObservationResult observed = fixture.Observe(Enum.Parse<ReleaseFileState>(stateName));
        using var output = new StringWriter();
        int calls = 0;
        int? exit = RubyReleaseCli.Run(["release", "observe-ruby", Preparation(fixture),
            fixture.Root,
            fixture.Write("tools.json", tools, TransferJson.Default.RubySourceTools)], output,
            observe: (prepared, captured, credential, _) =>
            {
                calls++;
                Assert.AreEqual(fixture.Prepared.Subject, prepared.Subject);
                Assert.AreEqual(fixture.Original, Assert.ContainsSingle(prepared.Files));
                Assert.AreEqual(tools, captured);
                Assert.IsNull(credential);
                return Task.FromResult(observed);
            });
        Assert.AreEqual(expectedExit, exit);
        Assert.AreEqual(1, calls);
        RubyObservationResult transfer = JsonSerializer.Deserialize(output.ToString(),
            TransferJson.Default.RubyObservationResult)!;
        Assert.AreEqual(observed.Observation.Subject, transfer.Observation.Subject);
        Assert.AreEqual(observed.Observation.Files[0],
            Assert.ContainsSingle(transfer.Observation.Files));
        Assert.AreEqual(observed.Read.State, transfer.Read.State);
        Assert.AreEqual(observed.Read.Original, transfer.Read.Original);
        Assert.AreEqual(observed.Read.Command, transfer.Read.Command);
        CollectionAssert.AreEqual(observed.Read.Errors, transfer.Read.Errors);
        CollectionAssert.AreEqual(observed.Observation.Checks, transfer.Observation.Checks);
    }

    [TestMethod]
    public void DescribeTypedJsonReportsOnlyHypotheticalWrites()
    {
        using var fixture = new RubyCallerFixture("official", dry: true);
        using var output = new StringWriter();
        using var error = new StringWriter();
        int exit = Program.Run(["release", "describe-ruby", Preparation(fixture),
            Observation(fixture, "observed.json", ReleaseFileState.Absent)], output,
            error);
        Assert.AreEqual("", error.ToString());
        Assert.AreEqual(0, exit);
        ReleaseDryRun result = JsonSerializer.Deserialize(output.ToString(),
            TransferJson.Default.ReleaseDryRun)!;
        Assert.IsTrue(result.Ready);
        Assert.IsTrue(result.Subject.DryRun);
        Assert.AreEqual(fixture.Prepared.Subject, result.Subject);
        Assert.AreEqual(fixture.Original.Output,
            Assert.ContainsSingle(result.HypotheticalWrites));
        Assert.IsEmpty(result.Reasons);
    }

    [TestMethod]
    [DataRow(false, nameof(ReleaseFileState.Exact), nameof(ReleaseBusinessState.Satisfied), 0)]
    [DataRow(true, nameof(ReleaseFileState.Exact), nameof(ReleaseBusinessState.Satisfied), 0)]
    [DataRow(true, nameof(ReleaseFileState.Absent), nameof(ReleaseBusinessState.Unsatisfied), 1)]
    public void PublishAndCompleteRetainSingleWriteAndNativeResult(bool timedOut,
        string final, string expectedState, int expectedExit)
    {
        using var fixture = new RubyCallerFixture("official");
        ReleaseFileState finalState = Enum.Parse<ReleaseFileState>(final);
        string readParent = fixture.Directory("read-parent"), writeParent =
            fixture.Directory("write-parent");
        var readTools = new RubySourceTools(Path.Combine(fixture.Root, "ruby"),
            Path.Combine(fixture.Root, "source.rb"), readParent);
        var writeTools = new RubyPublisherTools(readTools.Ruby, Path.Combine(fixture.Root, "gem"),
            writeParent);
        NativeCommandResult expectedNative = timedOut
            ? new(NativeTermination.TimedOut, null, "", "", 180, "controlled timeout")
            : RubyCallerFixture.Command();
        int reads = 0, writes = 0;
        var scratches = new List<string>();
        using var output = new StringWriter();
        int? exit = RubyReleaseCli.Run(["release", "publish-ruby", Preparation(fixture),
            fixture.Root,
            Observation(fixture, "initial.json", ReleaseFileState.Absent),
                fixture.Write("read-tools.json", readTools, TransferJson.Default.RubySourceTools),
            fixture.Write("publisher-tools.json", writeTools,
                TransferJson.Default.RubyPublisherTools)], output,
            observe: (prepared, tools, credential, _) =>
            {
                Assert.AreEqual(fixture.Prepared.Subject, prepared.Subject);
                Assert.IsNull(credential);
                Assert.StartsWith(readParent + Path.DirectorySeparatorChar, tools.Scratch);
                Assert.IsTrue(Directory.Exists(tools.Scratch));
                Assert.IsEmpty(Directory.EnumerateFileSystemEntries(tools.Scratch));
                Assert.DoesNotContain(tools.Scratch, scratches);
                scratches.Add(tools.Scratch);
                return Task.FromResult(fixture.Observe(reads++ == 0 ? ReleaseFileState.Absent :
                    finalState));
            },
            publish: (preparation, originals, tools, _, _) =>
            {
                writes++;
                Assert.AreEqual(1, reads);
                Assert.AreEqual(fixture.Prepared.Subject, preparation.Prepared.Subject);
                Assert.AreEqual(fixture.Root, originals);
                Assert.AreEqual(writeTools.Gem, tools.Gem);
                Assert.StartsWith(writeParent + Path.DirectorySeparatorChar, tools.Scratch);
                Assert.IsEmpty(Directory.EnumerateFileSystemEntries(tools.Scratch));
                return Task.FromResult(fixture.Publish(expectedNative));
            });
        // CLI acceptance of a result does not rewrite the native command outcome.
        Assert.AreEqual(0, exit);
        Assert.AreEqual(1, writes);
        Assert.AreEqual(2, reads);
        string publication = Path.Combine(fixture.Root, "publication.json");
        string receipt = output.ToString();
        File.WriteAllText(publication, receipt);
        RubyPublicationResult result = JsonSerializer.Deserialize(receipt,
            TransferJson.Default.RubyPublicationResult)!;
        Assert.AreEqual(fixture.Prepared.Subject, result.Subject);
        Assert.AreEqual(expectedNative, result.Invocation!.Upload.Command);
        Assert.AreEqual(fixture.Original.Output, result.Invocation.Upload.Output);
        Assert.AreEqual(finalState, Assert.ContainsSingle(result.Readback!.Observation.Files)
            .State);
        Assert.AreEqual(!timedOut, result.Invocation.Upload.Command.Succeeded);
        Assert.IsEmpty(result.Errors);

        string finalPath = Observation(fixture, "final.json", finalState);
        using var completion = new StringWriter();
        using var error = new StringWriter();
        Assert.AreEqual(expectedExit,
            Program.Run(CompleteArgs(fixture, finalPath, publication), completion, error));
        Assert.IsEmpty(error.ToString());
        RubyCompletionResult completed = JsonSerializer.Deserialize(completion.ToString(),
            TransferJson.Default.RubyCompletionResult)!;
        Assert.AreEqual(Enum.Parse<ReleaseBusinessState>(expectedState), completed.Outcome.State);
        Assert.AreEqual(true, completed.MutationAttempted);
        Assert.AreEqual(expectedNative, Assert.ContainsSingle(completed.Outcome.Uploads).Command);
        Assert.AreEqual(expectedNative, completed.Publication!.Invocation!.Upload.Command);
    }

    [TestMethod]
    [DataRow(true, "Exact", 0, "Satisfied")]
    [DataRow(true, "Absent", 1, "Unsatisfied")]
    [DataRow(false, "Exact", 1, "Unknown")]
    public void CompleteSeparatesNativeCommandFromVerifiedEffects(bool attempted,
        string finalState, int expectedExit, string business)
    {
        using var fixture = new RubyCallerFixture();
        string initial = Observation(fixture, "initial.json", ReleaseFileState.Absent);
        ReleaseFileState state = Enum.Parse<ReleaseFileState>(finalState);
        string final = Observation(fixture, "final.json", state);
        string publisher = "-";
        NativeCommandResult? native = null;
        if (attempted)
        {
            native = RubyCallerFixture.Command(NativeTermination.Exited, 17);
            publisher = fixture.Write("publication.json", new
                RubyPublicationResult(fixture.Prepared.Subject,
                fixture.Observe(ReleaseFileState.Absent), fixture.Publish(native),
                fixture.Observe(state), []),
                TransferJson.Default.RubyPublicationResult);
        }
        using var output = new StringWriter();
        using var error = new StringWriter();
        int exit = Program.Run(["release", "complete-ruby", Preparation(fixture), initial, final,
            publisher], output, error);
        Assert.AreEqual("", error.ToString());
        Assert.AreEqual(expectedExit, exit);
        RubyCompletionResult result = JsonSerializer.Deserialize(output.ToString(),
            TransferJson.Default.RubyCompletionResult)!;
        Assert.AreEqual(fixture.Prepared.Subject, result.Outcome.Subject);
        Assert.AreEqual(Enum.Parse<ReleaseBusinessState>(business), result.Outcome.State);
        if (!attempted) Assert.IsNull(result.MutationAttempted);
        else Assert.AreEqual(native is not null, result.MutationAttempted);
        if (native is null) Assert.IsEmpty(result.Outcome.Uploads);
        else
        {
            Assert.AreEqual(native, Assert.ContainsSingle(result.Outcome.Uploads).Command);
            Assert.AreEqual(native, result.Publication!.Invocation!.Upload.Command);
        }
        if (expectedExit == 0) Assert.IsEmpty(result.Outcome.Reasons);
        else Assert.IsNotEmpty(result.Outcome.Reasons);
    }

    [TestMethod]
    public void CompleteCannotReportSuccessWithoutFinalObservation()
    {
        using var fixture = new RubyCallerFixture();
        string final = Path.Combine(fixture.Root, "missing-final.json");
        string publication = Publication(fixture);
        using var output = new StringWriter();
        Assert.ThrowsExactly<FileNotFoundException>(() => RubyReleaseCli.Run(
            CompleteArgs(fixture, final, publication), output));
        Assert.AreEqual("", output.ToString());
    }

    [TestMethod]
    public void CompleteRejectsPublicationMissingBeforeObservation()
    {
        using var fixture = new RubyCallerFixture();
        string final = Observation(fixture, "final.json", ReleaseFileState.Exact);
        string publication = Publication(fixture);
        JsonObject root = JsonNode.Parse(File.ReadAllText(publication))!.AsObject();
        root.Remove("before");
        File.WriteAllText(publication, root.ToJsonString());
        using var output = new StringWriter();
        Assert.Throws<JsonException>(() => RubyReleaseCli.Run(
            CompleteArgs(fixture, final, publication), output));
        Assert.AreEqual("", output.ToString());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CompleteRejectsAnotherSelectedRef(bool substitutedPublication)
    {
        using var fixture = new RubyCallerFixture();
        ReleaseSubject other = fixture.Prepared.Subject with { SelectedRef = "refs/heads/another" };
        RubyObservationResult exact = fixture.Observe(ReleaseFileState.Exact);
        RubyObservationResult final = substitutedPublication ? exact : exact with
        { Observation = exact.Observation with { Subject = other } };
        string publication = Publication(fixture, substitutedPublication ? other : null);
        using var output = new StringWriter();
        Assert.ThrowsExactly<InvalidDataException>(() => RubyReleaseCli.Run(CompleteArgs(fixture,
            fixture.Write("final.json", final, TransferJson.Default.RubyObservationResult),
            publication), output));
        Assert.AreEqual("", output.ToString());
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void ObserveOriginalTransferFailsBeforeDependency(bool missing)
    {
        using var fixture = new RubyCallerFixture();
        if (missing) File.Delete(fixture.Archive);
        else File.WriteAllText(fixture.Archive, "replaced");
        int calls = 0;
        using var output = new StringWriter();
        Assert.ThrowsExactly<InvalidDataException>(() => RubyReleaseCli.Run(["release",
            "observe-ruby", Preparation(fixture), fixture.Root, Path.Combine(fixture.Root,
            "unread-tools.json")], output,
            observe: (_, _, _, _) =>
            {
                calls++; throw new
                AssertFailedException("Invalid transfer must stop before read.");
            }));
        Assert.AreEqual(0, calls);
        Assert.AreEqual("", output.ToString());
    }

    [TestMethod]
    public void UnsupportedCommandIsNotHandled()
    {
        using var output = new StringWriter();
        Assert.IsNull(RubyReleaseCli.Run(["release", "unknown-ruby"], output));
        Assert.AreEqual("", output.ToString());
    }

    private static string Preparation(RubyCallerFixture fixture) =>
        fixture.Write("preparation.json", fixture.Preparation,
        TransferJson.Default.RubyPreparationResult);
    private static string Observation(RubyCallerFixture fixture, string name, ReleaseFileState
        state) => fixture.Write(name, fixture.Observe(state),
        TransferJson.Default.RubyObservationResult);

    private static string Publication(RubyCallerFixture fixture, ReleaseSubject? subject = null) =>
        fixture.Write("publication.json", new RubyPublicationResult(
            subject ?? fixture.Prepared.Subject, fixture.Observe(ReleaseFileState.Absent),
            fixture.Publish(RubyCallerFixture.Command()), fixture.Observe(ReleaseFileState.Exact),
            []), TransferJson.Default.RubyPublicationResult);

    private static string[] CompleteArgs(RubyCallerFixture fixture, string final,
        string publication) => ["release", "complete-ruby", Preparation(fixture),
        Observation(fixture, "initial.json", ReleaseFileState.Absent), final, publication];
}

// Controlled portable evidence, never a native gem or deployed-feed qualification claim.
internal sealed class RubyCallerFixture : IDisposable
{
    internal string Root { get; } = Path.Combine(Path.GetTempPath(), "ruby-caller-" +
        Guid.NewGuid().ToString("N"));
    internal string Archive => Path.Combine(Root, "original.gem");
    internal RubyPreparationResult Preparation { get; }
    internal PreparedRelease Prepared => Preparation.Prepared;
    internal ReleaseFile Original => Prepared.Files[0];
    internal RubyCallerFixture(string channel = "buddy", bool dry = false,
        string unit = RubyReleaseUnits.Smoke)
    {
        System.IO.Directory.CreateDirectory(Root);
        File.WriteAllText(Archive, "original");
        var subject = new ReleaseSubject(new('a', 40), channel == "official" ? "refs/heads/main" :
            "refs/heads/topic", unit, "fixture/release-unit.yml", channel,
            channel == "official" ? "rubygems" : "github-packages-ruby", dry);
        var file = new ReleaseFile("gem-build", "gem-output", "ruby-gem",
            unit, "1.2.3", "original.gem", 8,
            Convert.ToHexStringLower(SHA256.HashData("original"u8)));
        string[] required = unit == RubyReleaseUnits.Latexmath
            ? ["native-input", "build", "contents", "consumer", "tests", "original-bytes"]
            : ["native-input", "build", "contents", "consumer", "original-bytes"];
        var prepared = new PreparedRelease(subject, [file], required, required.Select(id => new
            ReleaseCheck(id, ReleaseCheckStatus.Passed)).ToArray());
        Preparation = new(prepared, new()
        {
            ["name"] = file.Name,
            ["version"] = file.Version,
            ["platform"] = "ruby",
            ["commit"] = subject.Revision,
            ["public-release"] = "true",
            ["ruby"] = "4.0.7",
            ["rubygems"] = "4.0.7",
            ["nbgv"] = "3.10.94"
        },
            [new("build", new(Path.Combine(Root, "ruby"), Root, ["controlled-build"], 30),
                Command())], []);
    }
    internal static NativeCommandResult Command(NativeTermination termination =
        NativeTermination.Exited, int code = 0) => new(termination, code, "", "", 0.125, null);
    internal RubyObservationResult Observe(ReleaseFileState state) => new(new(Prepared.Subject,
        [new(Original.Output, Original.Name, Original.Version, state, state ==
            ReleaseFileState.Exact ? Original.Size : null, state == ReleaseFileState.Exact ?
            Original.Sha256 : null)], ["native-source-original"], [new("native-source-original",
            state is ReleaseFileState.Exact or ReleaseFileState.Absent ? ReleaseCheckStatus.Passed :
            ReleaseCheckStatus.Failed)]),
        new(state == ReleaseFileState.Absent ? RubySourceState.Absent : state ==
            ReleaseFileState.Exact ? RubySourceState.Present : RubySourceState.Unknown, state ==
            ReleaseFileState.Exact ? RemoteOriginal() : null, Command(), []));
    internal RubySourceOriginal RemoteOriginal() => new(Path.Combine(Root, "remote.gem"),
        Original.Name, Original.Version, "ruby", Original.Size, Original.Sha256);
    internal RubyPublishResult Publish(NativeCommandResult command) => new(Prepared.Subject,
        new(Original.Output, command));
    internal string Directory(string name) => System.IO.Directory.CreateDirectory(Path.Combine(Root,
        name)).FullName;
    internal string Write<T>(string name, T value, JsonTypeInfo<T> type)
    {
        string path = Path.Combine(Root, name);
        File.WriteAllText(path, JsonSerializer.Serialize(value, type));
        return path;
    }
    public void Dispose() => System.IO.Directory.Delete(Root, recursive: true);
}
