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
        ReleaseAdapters.Ruby.ValidatePreparation(fixture.Preparation);
        ReleaseAdapters.Ruby.VerifyOriginals(fixture.Preparation, fixture.Root);
        var transfer = JsonSerializer.Deserialize(JsonSerializer.Serialize(fixture.Preparation,
            TransferJson.Default.ReleasePreparationResult),
                TransferJson.Default.ReleasePreparationResult)!;
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
    public void ValidateRejectsNativeNameForAnotherGem() =>
        RejectPreparation(result => result with
        { Native = new(result.Native) { ["name"] = "another-gem" } });

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

    private static void RejectPreparation(Func<ReleasePreparationResult,
        ReleasePreparationResult> change)
    {
        using var fixture = new RubyCallerFixture();
        ReleasePreparationResult result = change(fixture.Preparation);
        Assert.ThrowsExactly<InvalidDataException>(() =>
            ReleaseAdapters.Ruby.ValidatePreparation(result));
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
        Assert.ThrowsExactly<InvalidDataException>(() =>
            ReleaseAdapters.Ruby.ValidatePreparation(preparation));
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
        if (accepted) ReleaseAdapters.Ruby.ValidatePreparation(preparation);
        else Assert.ThrowsExactly<InvalidDataException>(() =>
            ReleaseAdapters.Ruby.ValidatePreparation(preparation));
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
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => PublishAsync(
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
        ReleaseObservationResult initial = fixture.Observe(ReleaseFileState.Absent);
        ReleaseObservationResult fresh = fixture.Observe(Enum.Parse<ReleaseFileState>(stateName));
        int writes = 0;
        ReleasePublicationResult publication = await PublishAsync(fixture.Preparation,
            fixture.Root, initial,
            (_, _) => Task.FromResult(fresh), (_, _, _) =>
            {
                writes++; throw new
                AssertFailedException("Refresh forbids mutation.");
            }, context.CancellationToken);
        Assert.AreEqual(0, writes);
        Assert.AreSame(fresh, publication.Before);
        Assert.IsEmpty(publication.Steps);
        Assert.IsNull(publication.After);
        Assert.AreEqual(!satisfied, publication.Errors.Length != 0);
        if (satisfied)
        {
            ReleaseCompletionResult result = ReleaseEngine.Complete(ReleaseAdapters.Ruby,
                fixture.Preparation, initial, fresh,
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
        ReleaseObservationResult absent = fixture.Observe(ReleaseFileState.Absent);
        RubyPublishResult attempt =
            fixture.Publish(RubyCallerFixture.Command(NativeTermination.Cancelled, 17));
        int reads = 0, writes = 0;
        ReleasePublicationResult publication = await PublishAsync(fixture.Preparation,
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
        Assert.AreEqual(attempt.Upload, Assert.ContainsSingle(publication.Uploads));
        Assert.IsNull(publication.After);
        Assert.Contains("attempted effects retained", Assert.ContainsSingle(publication.Errors));
        ReleaseCompletionResult complete = ReleaseEngine.Complete(ReleaseAdapters.Ruby,
                fixture.Preparation, absent,
            fixture.Observe(ReleaseFileState.Exact), publication);
        Assert.AreEqual(ReleaseBusinessState.Satisfied, complete.Outcome.State);
        Assert.AreEqual(true, complete.MutationAttempted);
        Assert.AreEqual(attempt.Upload, Assert.ContainsSingle(complete.Outcome.Uploads));
        Assert.AreEqual(NativeTermination.Cancelled,
            complete.Outcome.Uploads[0].Command.Termination);
    }

    [TestMethod]
    public void CompleteRejectsWriteAfterExactObservation() =>
        RejectPublication(publication => publication with { Before = publication.After! });

    [TestMethod]
    public void CompleteRejectsReadbackWithoutInvocation() =>
        RejectPublication(publication => publication with { Steps = [] });

    private static void RejectPublication(Func<ReleasePublicationResult,
        ReleasePublicationResult> change)
    {
        using var fixture = new RubyCallerFixture();
        ReleaseObservationResult absent = fixture.Observe(ReleaseFileState.Absent), exact =
            fixture.Observe(ReleaseFileState.Exact);
        ReleasePublicationResult publication = change(new(fixture.Prepared.Subject, absent,
            [ReleaseWriteStep.FromUpload(new(fixture.Original.Output,
                RubyCallerFixture.Command()))],
            exact, []));
        Assert.ThrowsExactly<InvalidDataException>(() =>
            ReleaseEngine.Complete(ReleaseAdapters.Ruby,
                fixture.Preparation,
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
            PublishAsync(fixture.Preparation, fixture.Root,
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
    public async Task DryRunCannotReachThePublisher()
    {
        using var fixture = new RubyCallerFixture(dry: true);
        int calls = 0;
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            PublishAsync(fixture.Preparation, fixture.Root,
            fixture.Observe(ReleaseFileState.Absent),
            (_, _) =>
            {
                calls++; throw new
                AssertFailedException("Dry run cannot refresh for push.");
            },
            (_, _, _) => { calls++; throw new AssertFailedException("Dry run cannot push."); },
                context.CancellationToken));
        Assert.AreEqual(0, calls);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void PrepareTypedJsonRetainsQualifiedOrFailedCommand(bool failed)
    {
        using var fixture = new RubyCallerFixture();
        var request = new ReleasePreparationRequest(fixture.Prepared.Subject, fixture.Root,
            fixture.Directory("preparation"),
            Tools: new(Path.Combine(fixture.Root, "ruby"), Path.Combine(fixture.Root, "gem"),
                Path.Combine(fixture.Root, "dotnet"), Path.Combine(fixture.Root, "mise-bin")),
            MiseDataDirectory: Path.Combine(fixture.Root, "mise"));
        string path = fixture.Write("request.json", request,
            TransferJson.Default.ReleasePreparationRequest);
        ReleasePreparationResult native = fixture.Preparation;
        if (failed) native = native with
        {
            Errors = ["Controlled build failure"],
            Commands =
            [native.Commands[0] with { Result = RubyCallerFixture.Command(NativeTermination.Exited,
            17) }]
        };
        int calls = 0;
        using var output = new StringWriter();
        int? exit = ReleaseCli.Run(["release", "prepare-ruby", path], output, new(Prepare: (actual,
            _) =>
        {
            calls++;
            Assert.AreEqual(request, actual);
            return Task.FromResult(native);
        }));
        Assert.AreEqual(failed ? 1 : 0, exit);
        Assert.AreEqual(1, calls);
        ReleasePreparationResult result = JsonSerializer.Deserialize(output.ToString(),
            TransferJson.Default.ReleasePreparationResult)!;
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
        ReleaseObservationResult observed =
            fixture.Observe(Enum.Parse<ReleaseFileState>(stateName));
        using var output = new StringWriter();
        int calls = 0;
        int? exit = ReleaseCli.Run(["release", "observe-ruby", Preparation(fixture),
            fixture.Root,
            fixture.Write("tools.json", tools, TransferJson.Default.RubySourceTools)], output,
            new(ObserveRuby: (prepared, captured, credential, _) =>
            {
                calls++;
                Assert.AreEqual(fixture.Prepared.Subject, prepared.Subject);
                Assert.AreEqual(fixture.Original, Assert.ContainsSingle(prepared.Files));
                Assert.AreEqual(tools, captured);
                Assert.IsNull(credential);
                return Task.FromResult(observed);
            }));
        Assert.AreEqual(expectedExit, exit);
        Assert.AreEqual(1, calls);
        ReleaseObservationResult transfer = JsonSerializer.Deserialize(output.ToString(),
            TransferJson.Default.ReleaseObservationResult)!;
        Assert.AreEqual(observed.Observation.Subject, transfer.Observation.Subject);
        Assert.AreEqual(observed.Observation.Files[0],
            Assert.ContainsSingle(transfer.Observation.Files));
        Assert.AreEqual(observed.RubySource!.State, transfer.RubySource!.State);
        Assert.AreEqual(observed.RubySource!.Original, transfer.RubySource!.Original);
        Assert.AreEqual(observed.RubySource!.Command, transfer.RubySource!.Command);
        CollectionAssert.AreEqual(observed.RubySource!.Errors, transfer.RubySource!.Errors);
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
        int? exit = ReleaseCli.Run(["release", "publish-ruby", Preparation(fixture),
            fixture.Root,
            Observation(fixture, "initial.json", ReleaseFileState.Absent),
                fixture.Write("read-tools.json", readTools, TransferJson.Default.RubySourceTools),
            fixture.Write("publisher-tools.json", writeTools,
                TransferJson.Default.RubyPublisherTools)], output,
            new(ObserveRuby: (prepared, tools, credential, _) =>
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
            PublishRuby: (preparation, originals, tools, _, _) =>
            {
                writes++;
                Assert.AreEqual(1, reads);
                Assert.AreEqual(fixture.Prepared.Subject, preparation.Prepared.Subject);
                Assert.AreEqual(fixture.Root, originals);
                Assert.AreEqual(writeTools.Gem, tools.Gem);
                Assert.StartsWith(writeParent + Path.DirectorySeparatorChar, tools.Scratch);
                Assert.IsEmpty(Directory.EnumerateFileSystemEntries(tools.Scratch));
                return Task.FromResult(fixture.Publish(expectedNative));
            }));
        // CLI acceptance of a result does not rewrite the native command outcome.
        Assert.AreEqual(0, exit);
        Assert.AreEqual(1, writes);
        Assert.AreEqual(2, reads);
        string publication = Path.Combine(fixture.Root, "publication.json");
        string receipt = output.ToString();
        File.WriteAllText(publication, receipt);
        ReleasePublicationResult result = JsonSerializer.Deserialize(receipt,
            TransferJson.Default.ReleasePublicationResult)!;
        Assert.AreEqual(fixture.Prepared.Subject, result.Subject);
        ReleaseUpload upload = Assert.ContainsSingle(result.Uploads);
        Assert.AreEqual(expectedNative, upload.Command);
        Assert.AreEqual(fixture.Original.Output, upload.Output);
        Assert.AreEqual(finalState, Assert.ContainsSingle(result.After!.Observation.Files)
            .State);
        Assert.AreEqual(!timedOut, upload.Command.Succeeded);
        Assert.IsEmpty(result.Errors);

        string finalPath = Observation(fixture, "final.json", finalState);
        using var completion = new StringWriter();
        using var error = new StringWriter();
        Assert.AreEqual(expectedExit,
            Program.Run(CompleteArgs(fixture, finalPath, publication), completion, error));
        Assert.IsEmpty(error.ToString());
        ReleaseCompletionResult completed = JsonSerializer.Deserialize(completion.ToString(),
            TransferJson.Default.ReleaseCompletionResult)!;
        Assert.AreEqual(Enum.Parse<ReleaseBusinessState>(expectedState), completed.Outcome.State);
        Assert.AreEqual(true, completed.MutationAttempted);
        Assert.AreEqual(expectedNative, Assert.ContainsSingle(completed.Outcome.Uploads).Command);
        Assert.AreEqual(expectedNative,
            Assert.ContainsSingle(completed.Publication!.Uploads).Command);
    }

    [TestMethod]
    public void CompleteWithMissingPublisherLeavesMutationUnknownEvenIfNowExact()
    {
        using var fixture = new RubyCallerFixture();
        string initial = Observation(fixture, "initial.json", ReleaseFileState.Absent);
        string final = Observation(fixture, "final.json", ReleaseFileState.Exact);
        using var output = new StringWriter();

        Assert.AreEqual(1, ReleaseCli.Run(["release", "complete-ruby",
            Preparation(fixture), initial, final, "-"], output));

        ReleaseCompletionResult result = JsonSerializer.Deserialize(output.ToString(),
            TransferJson.Default.ReleaseCompletionResult)!;
        Assert.AreEqual(ReleaseBusinessState.Unknown, result.Outcome.State);
        Assert.IsNull(result.MutationAttempted);
        Assert.IsEmpty(result.Outcome.Uploads);
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
        Assert.Throws<JsonException>(() => ReleaseCli.Run(
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
        ReleaseObservationResult exact = fixture.Observe(ReleaseFileState.Exact);
        ReleaseObservationResult final = substitutedPublication ? exact : exact with
        { Observation = exact.Observation with { Subject = other } };
        string publication = Publication(fixture, substitutedPublication ? other : null);
        using var output = new StringWriter();
        Assert.ThrowsExactly<InvalidDataException>(() => ReleaseCli.Run(CompleteArgs(fixture,
            fixture.Write("final.json", final, TransferJson.Default.ReleaseObservationResult),
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
        Assert.ThrowsExactly<InvalidDataException>(() => ReleaseCli.Run(["release",
            "observe-ruby", Preparation(fixture), fixture.Root, Path.Combine(fixture.Root,
            "unread-tools.json")], output,
            new(ObserveRuby: (_, _, _, _) =>
            {
                calls++; throw new
                AssertFailedException("Invalid transfer must stop before read.");
            })));
        Assert.AreEqual(0, calls);
        Assert.AreEqual("", output.ToString());
    }

    [TestMethod]
    public void UnsupportedCommandIsNotHandled()
    {
        using var output = new StringWriter();
        Assert.IsNull(ReleaseCli.Run(["release", "unknown-ruby"], output));
        Assert.AreEqual("", output.ToString());
    }

    private static string Preparation(RubyCallerFixture fixture) =>
        fixture.Write("preparation.json", fixture.Preparation,
        TransferJson.Default.ReleasePreparationResult);
    private static string Observation(RubyCallerFixture fixture, string name, ReleaseFileState
        state) => fixture.Write(name, fixture.Observe(state),
        TransferJson.Default.ReleaseObservationResult);

    private static string Publication(RubyCallerFixture fixture, ReleaseSubject? subject = null) =>
        fixture.Write("publication.json", new ReleasePublicationResult(
            subject ?? fixture.Prepared.Subject, fixture.Observe(ReleaseFileState.Absent),
            [ReleaseWriteStep.FromUpload(new(fixture.Original.Output,
                RubyCallerFixture.Command()))],
            fixture.Observe(ReleaseFileState.Exact),
            []), TransferJson.Default.ReleasePublicationResult);

    private static Task<ReleasePublicationResult> PublishAsync(
        ReleasePreparationResult preparation, string originals, ReleaseObservationResult initial,
        Func<PreparedRelease, CancellationToken, Task<ReleaseObservationResult>> observe,
        Func<ReleasePreparationResult, string, CancellationToken, Task<RubyPublishResult>> publish,
        CancellationToken token) =>
        ReleaseEngine.PublishAsync(ReleaseAdapters.Ruby, preparation, originals, initial, new(
            ObserveAsync: observe,
            WriteOutputAsync: async (_, cancel) => ReleaseWriteStep.FromUpload(
                (await publish(preparation, originals, cancel)).Upload),
            Readback: DestinationReadbackMode.AllowCancellation), token);

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
    internal ReleasePreparationResult Preparation { get; }
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
    internal ReleaseObservationResult Observe(ReleaseFileState state) => new(new(Prepared.Subject,
        [new(Original.Output, Original.Name, Original.Version, state, state ==
            ReleaseFileState.Exact ? Original.Size : null, state == ReleaseFileState.Exact ?
            Original.Sha256 : null)], ["native-source-original"], [new("native-source-original",
            state is ReleaseFileState.Exact or ReleaseFileState.Absent ? ReleaseCheckStatus.Passed :
            ReleaseCheckStatus.Failed)]),
        [], RubySource: new(state == ReleaseFileState.Absent ? RubySourceState.Absent : state ==
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
