using System.Net;
using System.Text.Json.Nodes;
using System.Text.Json;
using WorkflowDelivery.Destinations;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Release;

namespace WorkflowDelivery.Tests.Release;

[TestClass]
public sealed class NuGetReleaseTests(TestContext context)
{
    [TestMethod]
    public async Task PublishAsyncTransfersTheLibraryOutcomeWithoutInventingAnUploadCommand()
    {
        using var fixture = new NuGetReleaseTestFixture();
        NuGetObservationResult initial = fixture.Observe(ReleaseFileState.Absent);
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
            exact, publication);

        Assert.AreEqual(ReleaseBusinessState.Satisfied, result.Outcome.State);
        Assert.AreEqual(true, result.MutationAttempted);
        Assert.AreEqual(2, reads);
        Assert.AreEqual(1, transport.Sends);
        Assert.IsEmpty(result.Outcome.Uploads);
        Assert.AreSame(publication.Invocation, result.Invocation);
        Assert.IsTrue(result.Invocation!.ReturnedSuccessfully);
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
    // A package GitHub does not have yet is absent; the first upload creates it.
    public async Task PublishAsyncAbsentGithubPackagePermitsCreatingUpload()
    {
        using var fixture = new NuGetReleaseTestFixture();
        using var registry = new MissingPackageRegistry();
        using var client = new HttpClient(registry);
        int pushes = 0;

        NuGetPublicationResult result = await NuGetRelease.PublishAsync(
            fixture.Preparation, fixture.Root, "controlled-capability",
            new GithubNuGetObservation(client).ObserveAsync, (_, _, _, _, _) =>
            { pushes++; return Task.FromResult(new NuGetPushResult(true, false, null, 1)); },
            context.CancellationToken);

        Assert.AreEqual(1, pushes);
        Assert.IsNotNull(result.Invocation);
        Assert.IsEmpty(result.Errors);
        Assert.AreEqual(ReleaseFileState.Absent, result.Before.Observation.Files[0].State);
        Assert.AreEqual(ReleaseCheckStatus.Passed, result.Before.Observation.Checks.Single(
            check => check.Id == "native-package-control").Status);
    }

    private sealed class MissingPackageRegistry : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellation) => Task.FromResult(
            request.RequestUri!.AbsoluteUri == GithubNuGetObservation.ServiceIndex
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                    "{\"version\":\"3.0.0\",\"resources\":["
                    + "{\"@type\":\"PackageBaseAddress/3.0.0\","
                    + "\"@id\":\"https://nuget.pkg.github.com/hcoona/download/\"},"
                    + "{\"@type\":\"PackagePublish/2.0.0\","
                    + "\"@id\":\"https://nuget.pkg.github.com/hcoona\"}]}")
                }
                : new HttpResponseMessage(HttpStatusCode.NotFound)
                { Content = new ByteArrayContent([]) });
    }

    [TestMethod]
    [DataRow("Exact", "Satisfied")]
    [DataRow("Absent", "Unknown")]
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
    public void CompleteRequiresNativeMetadataVisibilityEvenWithExactArchive()
    {
        using var fixture = new NuGetReleaseTestFixture();
        NuGetObservationResult exact = fixture.Observe(ReleaseFileState.Exact);
        NuGetObservationResult final = exact with
        {
            Observation = exact.Observation with
            {
                Checks = [exact.Observation.Checks[0], new("native-metadata-visibility",
                    ReleaseCheckStatus.Missing)],
            },
        };
        NuGetCompletionResult result = NuGetRelease.Complete(fixture.Prepared, exact,
            final, null);

        Assert.AreEqual(ReleaseBusinessState.Unsatisfied, result.Outcome.State);
        Assert.Contains("native-metadata-visibility", string.Join(";", result.Outcome.Reasons));
        Assert.AreEqual(false, result.MutationAttempted);
    }

    // WD-REL-009: nuget.org completes on the exact version reported available, without
    // byte proof; a version already available is satisfied without claiming creation.
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void CompleteNuGetOrgAvailableVersionIsSatisfiedWithoutByteProof(bool preExisting)
    {
        PreparedRelease prepared = Official();
        NuGetObservationResult available = OfficialObservation(prepared,
            ReleaseFileState.Available);
        NuGetObservationResult initial = preExisting ? available
            : OfficialObservation(prepared, ReleaseFileState.Absent);
        var invocation = new NuGetPushResult(true, false, null, 1);
        NuGetPublicationResult? published = preExisting ? null
            : new(prepared.Subject, initial, invocation, available, []);

        using var fixture = new NuGetReleaseTestFixture();
        NuGetCompletionResult result = CompleteOfficial(fixture, prepared, initial, available,
            published, expectedExit: 0);

        Assert.AreEqual(ReleaseBusinessState.Satisfied, result.Outcome.State);
        Assert.AreEqual((bool?)!preExisting, result.MutationAttempted);
        Assert.AreEqual(preExisting ? null : invocation, result.Invocation);
    }

    [TestMethod]
    [DataRow("Absent", "Absent", "Available", 3, 0)]
    [DataRow("Unknown", "Available", "Available", 1, 1)]
    [DataRow("Conflict", "Available", "Available", 1, 1)]
    public void ObserveOfficialSettlesOnlyAbsentAfterSuccessfulUpload(string first, string second,
        string third, int expectedReads, int expectedExit)
    {
        using var fixture = new NuGetReleaseTestFixture();
        NuGetPreparationResult preparation = WriteOfficialOriginal(fixture, Official());
        PreparedRelease prepared = preparation.Prepared;
        NuGetObservationResult absent = OfficialObservation(prepared, ReleaseFileState.Absent);
        string original = fixture.Write("official.json", preparation,
            TransferJson.Default.NuGetPreparationResult);
        string publisher = fixture.Write("publication.json",
            new NuGetPublicationResult(prepared.Subject, absent,
                new(true, false, null, 1), absent, []),
            TransferJson.Default.NuGetPublicationResult);
        string[] states = [first, second, third];
        using var output = new StringWriter();
        int reads = 0;

        int? exit = NuGetReleaseCli.Run(["release", "observe-nuget", original, fixture.Root,
            publisher], output, observe: (subject, credential, _) =>
            {
                Assert.AreEqual(prepared.Subject, subject.Subject);
                Assert.IsEmpty(credential);
                if (reads == expectedReads)
                    throw new AssertFailedException("Unexpected additional settling read.");
                return Task.FromResult(OfficialObservation(subject,
                    Enum.Parse<ReleaseFileState>(states[reads++])));
            }, settleInterval: TimeSpan.Zero);

        Assert.AreEqual(expectedExit, exit);
        Assert.AreEqual(expectedReads, reads);
        NuGetObservationResult evidence = JsonSerializer.Deserialize(output.ToString(),
            TransferJson.Default.NuGetObservationResult)!;
        Assert.AreEqual(Enum.Parse<ReleaseFileState>(states[expectedReads - 1]),
            Assert.ContainsSingle(evidence.Observation.Files).State);
    }

    [TestMethod]
    [DoNotParallelize]
    public void PublishRejectsSubstitutedOfficialOriginal()
    {
        using var fixture = new NuGetReleaseTestFixture();
        NuGetPreparationResult preparation = WriteOfficialOriginal(fixture, Official());
        string transfer = fixture.Write("official.json", preparation,
            TransferJson.Default.NuGetPreparationResult);
        File.AppendAllText(Path.Combine(fixture.Root, preparation.Prepared.Files[0].Path),
            "substituted bytes");
        using var output = new StringWriter();
        using var error = new StringWriter();
        string? previous = Environment.GetEnvironmentVariable("NUGET_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("NUGET_API_KEY", "controlled-key");
            Assert.AreEqual(2, Program.Run(["release", "publish-nuget", transfer, fixture.Root],
                output, error));
        }
        finally
        {
            Environment.SetEnvironmentVariable("NUGET_API_KEY", previous);
        }
        Assert.IsEmpty(output.ToString());
        Assert.Contains("original native package bytes", error.ToString());
    }

    [TestMethod]
    public void CompleteRejectsObservationForAnotherRevision()
    {
        using var fixture = new NuGetReleaseTestFixture();
        NuGetObservationResult exact = fixture.Observe(ReleaseFileState.Exact);
        var foreign = exact with
        {
            Observation = exact.Observation with
            { Subject = exact.Observation.Subject with { Revision = new string('b', 40) } }
        };
        string initial = fixture.Write("initial.json", exact,
            TransferJson.Default.NuGetObservationResult);
        string final = fixture.Write("final.json", foreign,
            TransferJson.Default.NuGetObservationResult);
        using var output = new StringWriter();
        using var error = new StringWriter();

        Assert.AreEqual(2, Program.Run(["release", "complete-nuget", Preparation(fixture),
            initial, final, "-"], output, error));
        Assert.IsEmpty(output.ToString());
        Assert.IsNotEmpty(error.ToString());
    }

    // An accepted nuget.org upload not listed within about 15 minutes is uncertain and
    // returns to the owner; re-dispatching the same revision observes it without an upload.
    // The deadline cuts an in-flight read or a delay.
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task NuGetOrgUnlistedAfterBoundIsUnknownAndRedispatchIsSatisfied(
        bool hangingRead)
    {
        PreparedRelease prepared = Official();
        NuGetObservationResult absent = OfficialObservation(prepared, ReleaseFileState.Absent);
        var invocation = new NuGetPushResult(true, false, null, 1);
        int reads = 0;
        NuGetObservationResult final = await NuGetRelease.SettleAsync(prepared,
            async (_, _, token) =>
            {
                if (reads++ == 0 || !hangingRead) return absent;
                await Assert.ThrowsAsync<OperationCanceledException>(() =>
                    Task.Delay(Timeout.Infinite, token));
                return OfficialObservation(prepared, ReleaseFileState.Unknown);
            }, "", TimeSpan.FromMilliseconds(200),
            hangingRead ? TimeSpan.Zero : Timeout.InfiniteTimeSpan, context.CancellationToken);

        NuGetCompletionResult result = NuGetRelease.Complete(prepared, absent, final,
            new(prepared.Subject, absent, invocation, absent, []));

        Assert.AreEqual(TimeSpan.FromMinutes(15), NuGetRelease.SettleDeadline);
        Assert.AreEqual(hangingRead ? 2 : 1, reads);
        Assert.AreEqual(ReleaseFileState.Unknown, final.Observation.Files[0].State);
        Assert.AreEqual(ReleaseBusinessState.Unknown, result.Outcome.State);
        Assert.AreEqual(true, result.MutationAttempted);
        Assert.IsTrue(result.Outcome.Reasons.Any(reason => reason.Contains("re-dispatch",
            StringComparison.Ordinal)));

        NuGetObservationResult available = OfficialObservation(prepared,
            ReleaseFileState.Available);
        NuGetCompletionResult redispatch = NuGetRelease.Complete(prepared, available,
            available, null);
        Assert.AreEqual(ReleaseBusinessState.Satisfied, redispatch.Outcome.State);
        Assert.AreEqual(false, redispatch.MutationAttempted);
    }

    // GitHub Packages Buddy keeps exact byte readback; the core rejects a version-only state.
    [TestMethod]
    public async Task PublishAsyncBuddyRejectsVersionOnlyStateBeforeUpload()
    {
        using var fixture = new NuGetReleaseTestFixture();
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => NuGetRelease.PublishAsync(
            fixture.Preparation, fixture.Root, "controlled-capability",
            (_, _, _) => Task.FromResult(fixture.Observe(ReleaseFileState.Available)),
            (_, _, _, _, _) => throw new AssertFailedException("Available must not upload."),
            context.CancellationToken));
    }

    [TestMethod]
    public void CompleteBuddyRejectsVersionOnlyState()
    {
        using var fixture = new NuGetReleaseTestFixture();
        Assert.ThrowsExactly<InvalidDataException>(() => NuGetRelease.Complete(
            fixture.Prepared, fixture.Observe(ReleaseFileState.Exact),
            fixture.Observe(ReleaseFileState.Available), null));
    }

    [TestMethod]
    public void CompleteNuGetOrgRejectsByteEvidenceOnVersionOnlyState()
    {
        PreparedRelease prepared = Official();
        NuGetObservationResult available = OfficialObservation(prepared,
            ReleaseFileState.Available);
        NuGetObservationResult sized = available with
        {
            Observation = available.Observation with
            { Files = [available.Observation.Files[0] with { Size = 1 }] }
        };
        Assert.ThrowsExactly<InvalidDataException>(() => NuGetRelease.Complete(prepared,
            sized, sized, null));
    }

    private static PreparedRelease Official() => new(
        new(new string('a', 40), "refs/heads/main", "phi-failure-detector",
            "src/public/lib/PhiFailureDetector/workflow-delivery.release-unit.yml", "official",
            "nugetorg", false),
        [new("package", "original", "nuget-package", "PhiFailureDetector", "1.0.0",
            "PhiFailureDetector.1.0.0.nupkg", 1, new string('b', 64))],
        [.. NuGetPreparation.Required], NuGetPreparation.Required.Select(id =>
            new ReleaseCheck(id, ReleaseCheckStatus.Passed)).ToArray());

    private static NuGetObservationResult OfficialObservation(PreparedRelease prepared,
        ReleaseFileState state)
    {
        ReleaseFile file = prepared.Files[0];
        return new(new(prepared.Subject,
            [new(file.Output, file.Name, file.Version, state, null, null)],
            NuGetOrgObservation.RequiredChecks,
            [new("native-metadata-visibility", ReleaseCheckStatus.Passed)]),
            new(NuGetOrgObservation.PackageBaseAddress, NuGetPublisher.NuGetOrgEndpoint), []);
    }

    private static NuGetCompletionResult CompleteOfficial(NuGetReleaseTestFixture fixture,
        PreparedRelease prepared, NuGetObservationResult initial, NuGetObservationResult final,
        NuGetPublicationResult? publication, int expectedExit)
    {
        NuGetPreparationResult preparation = WriteOfficialOriginal(fixture, prepared);
        string input = fixture.Write("official.json", preparation,
            TransferJson.Default.NuGetPreparationResult);
        string before = fixture.Write("initial.json", initial,
            TransferJson.Default.NuGetObservationResult);
        string after = fixture.Write("final.json", final,
            TransferJson.Default.NuGetObservationResult);
        string publisher = publication is null ? "-" : fixture.Write("publication.json",
            publication, TransferJson.Default.NuGetPublicationResult);
        using var output = new StringWriter();
        using var error = new StringWriter();

        Assert.AreEqual(expectedExit, Program.Run(["release", "complete-nuget", input, before,
            after, publisher], output, error), error.ToString());
        Assert.IsEmpty(error.ToString());
        return JsonSerializer.Deserialize(output.ToString(),
            TransferJson.Default.NuGetCompletionResult)!;
    }

    private static NuGetPreparationResult WriteOfficialOriginal(NuGetReleaseTestFixture fixture,
        PreparedRelease prepared)
    {
        ReleaseFile file = Assert.ContainsSingle(prepared.Files);
        string archive = Path.Combine(fixture.Root, file.Path);
        using (var zip = System.IO.Compression.ZipFile.Open(archive,
            System.IO.Compression.ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry("package.nuspec").Open()))
            writer.Write("<package><metadata><id>PhiFailureDetector</id>"
                + "<version>1.0.0</version><authors>hcoona</authors>"
                + "<description>Controlled archive</description></metadata></package>");
        var native = new Dictionary<string, string>(fixture.Preparation.Native,
            StringComparer.Ordinal)
        {
            ["PackageId"] = file.Name,
            ["AssemblyName"] = file.Name,
            ["TargetFramework"] = "",
            ["DocumentationFile"] = "",
        };
        return fixture.Preparation with
        {
            Prepared = prepared with
            {
                Files = [file with { Size = new FileInfo(archive).Length,
                    Sha256 = WorkflowDelivery.Ecosystems.DotNetPackageArchive.Digest(archive) }]
            },
            Native = native,
        };
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
    [DataRow("\"not-a-state\"")]
    [DataRow("17")]
    public void DescribeRejectsInvalidObservationState(string stateJson)
    {
        using var fixture = new NuGetReleaseTestFixture(dryRun: true);
        string preparation = Preparation(fixture);
        string observation = Observation(fixture, "observed.json", ReleaseFileState.Exact);
        JsonObject root = JsonNode.Parse(File.ReadAllText(observation))!.AsObject();
        root["observation"]!["files"]![0]!["state"] = JsonNode.Parse(stateJson);
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
