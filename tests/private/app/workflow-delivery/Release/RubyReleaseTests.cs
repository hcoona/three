using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using WorkflowDelivery.Destinations;
using WorkflowDelivery.Ecosystems;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Release;

namespace WorkflowDelivery.Tests.Release;

[TestClass]
public sealed class RubyReleaseTests(TestContext context)
{
    private static readonly string[] ExcludedPublisherEnvironment =
        ["GITHUB_TOKEN", "RUBYOPT", "RUBYLIB", "HTTPS_PROXY", "BUNDLE_GEMS__RUBYGEMS__ORG"];
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
    [DataRow("missing-check")]
    [DataRow("failed-check")]
    [DataRow("missing-command")]
    [DataRow("failed-command")]
    [DataRow("command-error")]
    [DataRow("errors")]
    [DataRow("name")]
    [DataRow("version")]
    [DataRow("platform")]
    [DataRow("commit")]
    [DataRow("extra-file")]
    public void ValidateIncompletePreparationCannotQualify(string defect)
    {
        using var fixture = new RubyCallerFixture();
        RubyPreparationResult result = fixture.Preparation;
        result = defect switch
        {
            "missing-check" => result with { Prepared = result.Prepared with { Checks =
                result.Prepared.Checks[..^1] } },
            "failed-check" => result with { Prepared = result.Prepared with { Checks =
                result.Prepared.Checks.Select(c => c.Id == "contents" ? c with { Status =
                ReleaseCheckStatus.Failed } : c).ToArray() } },
            "missing-command" => result with { Commands = [] },
            "failed-command" => result with { Commands = [result.Commands[0] with { Result =
                RubyCallerFixture.Command(NativeTermination.Exited, 17) }] },
            "command-error" => result with { Commands = [result.Commands[0] with { Result =
                RubyCallerFixture.Command() with { Error = "Controlled error" } }] },
            "errors" => result with { Errors = ["Incomplete qualification"] },
            "extra-file" => result with { Prepared = result.Prepared with { Files =
                [fixture.Original, fixture.Original with { Output = "extra", Path = "extra.gem",
                Version = "2.0.0" }] } },
            "size" => result with { Prepared = result.Prepared with { Files = [fixture.Original with
                { Size = 8 * 1024 * 1024 + 1 }] } },
            _ => result with { Native = new(result.Native) { [defect] = "another-native-value" } },
        };
        Assert.ThrowsExactly<InvalidDataException>(() => RubyRelease.Validate(result));
    }

    [TestMethod]
    [DataRow("buddy", "github-packages-ruby", "refs/heads/topic", false, false, true)]
    [DataRow("official", "rubygems", "refs/heads/main", false, true, true)]
    [DataRow("official", "rubygems", "refs/heads/main", false, false, false)]
    [DataRow("official", "rubygems", "refs/tags/preview", true, false, true)]
    [DataRow("buddy", "github-packages-ruby", "refs/tags/preview", true, false, true)]
    [DataRow("buddy", "rubygems", "refs/heads/main", true, true, false)]
    public void ValidateChannelRefAndDryEligibility(string channel, string destination,
        string selectedRef, bool dry, bool publicRelease, bool accepted)
    {
        using var fixture = new RubyCallerFixture(channel, dry);
        var preparation = fixture.Preparation with
        {
            Prepared = fixture.Prepared with { Subject = fixture.Prepared.Subject with { Destination
                = destination, SelectedRef = selectedRef } },
            Native = new(fixture.Preparation.Native) { ["public-release"] = publicRelease ? "true" :
                "false" },
        };
        if (accepted) RubyRelease.Validate(preparation);
        else Assert.ThrowsExactly<InvalidDataException>(() => RubyRelease.Validate(preparation));
        Assert.AreEqual(dry, preparation.Prepared.Subject.DryRun);
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
            (_, _) => { reads++; File.AppendAllText(fixture.Archive, "substitution"); return
                Task.FromResult(fixture.Observe(ReleaseFileState.Absent)); },
            (_, _, _) => { writes++; throw new
                AssertFailedException("Substituted bytes cannot be written."); },
                context.CancellationToken));
        Assert.AreEqual(duringRefresh ? 1 : 0, reads);
        Assert.AreEqual(0, writes);
    }

    [TestMethod]
    [DataRow("Exact", "Present", "Passed")]
    [DataRow("Conflict", "Present", "Failed")]
    [DataRow("Absent", "Absent", "Passed")]
    [DataRow("Unknown", "Unknown", "Failed")]
    public async Task ObserveNativeAnswerClassifiesOriginalBytes(string stateName, string
        sourceName, string checkName)
    {
        using var fixture = new RubyCallerFixture("official");
        ReleaseFileState state = Enum.Parse<ReleaseFileState>(stateName);
        string scratch = fixture.Directory("native-read");
        int executions = 0;
        RubyObservationResult result = await
            RubyDestinationObservation.ObserveAsync(fixture.Prepared,
            new(Path.Combine(fixture.Root, "ruby"), Path.Combine(fixture.Root, "source.rb"),
                scratch), null,
            (command, _) =>
            {
                executions++;
                Assert.AreEqual(90, command.DeadlineSeconds);
                Assert.IsFalse(command.InheritEnvironment);
                Assert.IsTrue(command.CloseStandardInput);
                Assert.IsFalse(command.Environment!.ContainsKey("GEM_HOST_API_KEY"));
                using JsonDocument request =
                    JsonDocument.Parse(File.ReadAllText(command.Arguments[1]));
                Assert.AreEqual("https://rubygems.org",
                    request.RootElement.GetProperty("host").GetString());
                string answer;
                if (state is ReleaseFileState.Exact or ReleaseFileState.Conflict)
                {
                    string path =
                        Path.Combine(request.RootElement.GetProperty("directory").GetString()!,
                        "remote.gem");
                    File.WriteAllText(path, state == ReleaseFileState.Exact ? "original" :
                        "different");
                    answer = JsonSerializer.Serialize(new { state = "present", path, name =
                        fixture.Original.Name, version = fixture.Original.Version, platform =
                        "ruby", size = new FileInfo(path).Length, sha256 =
                        Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))) });
                }
                else answer = state == ReleaseFileState.Absent ? "{\"state\":\"absent\"}" :
                    "{broken";
                return Task.FromResult(RubyCallerFixture.Command() with { Stdout = answer });
            }, context.CancellationToken);
        context.WriteLine("native observation receipt: " + JsonSerializer.Serialize(result,
            TransferJson.Default.RubyObservationResult));
        Assert.AreEqual(1, executions);
        Assert.AreEqual(state, Assert.ContainsSingle(result.Observation.Files).State);
        Assert.AreEqual(Enum.Parse<RubySourceState>(sourceName), result.Read.State);
        Assert.AreEqual(Enum.Parse<ReleaseCheckStatus>(checkName),
            Assert.ContainsSingle(result.Observation.Checks).Status);
        Assert.AreEqual(fixture.Prepared.Subject, result.Observation.Subject);
        Assert.AreEqual(NativeTermination.Exited, result.Read.Command!.Termination);
        Assert.AreEqual(0, result.Read.Command.ExitCode);
        Assert.AreEqual("", result.Read.Command.Stdout);
        if (state == ReleaseFileState.Exact) Assert.AreEqual(fixture.Original.Sha256,
            result.Read.Original!.Sha256);
        if (state == ReleaseFileState.Conflict) Assert.AreNotEqual(fixture.Original.Sha256,
            result.Read.Original!.Sha256);
    }

    [TestMethod]
    [DataRow("Exact", true)]
    [DataRow("Unknown", false)]
    [DataRow("Conflict", false)]
    public async Task PublishRefreshedStateSkipsMutation(string stateName, bool satisfied)
    {
        using var fixture = new RubyCallerFixture();
        RubyObservationResult initial = fixture.Observe(ReleaseFileState.Absent);
        RubyObservationResult fresh = fixture.Observe(Enum.Parse<ReleaseFileState>(stateName));
        int writes = 0;
        RubyPublicationResult publication = await RubyRelease.PublishAsync(fixture.Preparation,
            fixture.Root, initial,
            (_, _) => Task.FromResult(fresh), (_, _, _) => { writes++; throw new
                AssertFailedException("Refresh forbids mutation."); }, context.CancellationToken);
        context.WriteLine("refresh receipt: " + JsonSerializer.Serialize(publication,
            TransferJson.Default.RubyPublicationResult));
        Assert.AreEqual(0, writes);
        Assert.AreSame(fresh, publication.Before);
        Assert.IsNull(publication.Invocation);
        Assert.IsNull(publication.Readback);
        Assert.AreEqual(!satisfied, publication.Errors.Length != 0);
        if (satisfied)
        {
            RubyCompletionResult result = RubyRelease.Complete(fixture.Prepared, initial, fresh,
                fixture.Consumer(), publication);
            Assert.AreEqual(ReleaseBusinessState.Satisfied, result.Outcome.State);
            Assert.AreEqual(false, result.MutationAttempted);
            Assert.IsEmpty(result.Outcome.Uploads);
        }
    }

    [TestMethod]
    [DataRow("Exited", 0, "Exact", "Satisfied")]
    [DataRow("Exited", 17, "Exact", "Satisfied")]
    [DataRow("Cancelled", 17, "Exact", "Satisfied")]
    [DataRow("TimedOut", 17, "Absent", "Unsatisfied")]
    [DataRow("Exited", 17, "Unknown", "Unknown")]
    [DataRow("Exited", 17, "Conflict", "Unsatisfied")]
    public async Task PublishOnceReadbackSeparatesCommandAndBusiness(string terminationName, int
        code, string finalStateName, string outcomeName)
    {
        using var fixture = new RubyCallerFixture();
        RubyObservationResult absent = fixture.Observe(ReleaseFileState.Absent);
        RubyObservationResult final = fixture.Observe(Enum.Parse<ReleaseFileState>(finalStateName));
        NativeCommandResult command =
            RubyCallerFixture.Command(Enum.Parse<NativeTermination>(terminationName), code);
        int reads = 0, writes = 0;
        RubyPublicationResult publication = await RubyRelease.PublishAsync(fixture.Preparation,
            fixture.Root, absent,
            (prepared, _) => { Assert.AreSame(fixture.Prepared, prepared); return
                Task.FromResult(reads++ == 0 ? absent : final); },
            (prepared, originals, _) => { writes++; Assert.AreSame(fixture.Preparation, prepared);
                Assert.AreEqual(fixture.Root, originals); Assert.AreEqual(1, reads); return
                Task.FromResult(fixture.Publish(command)); }, context.CancellationToken);
        RubyCompletionResult result = RubyRelease.Complete(fixture.Prepared, absent, final,
            fixture.Consumer(), publication);
        context.WriteLine("publication receipt: " + JsonSerializer.Serialize(publication,
            TransferJson.Default.RubyPublicationResult));
        Assert.AreEqual(1, writes);
        Assert.AreEqual(2, reads);
        Assert.AreSame(final, publication.Readback);
        Assert.AreSame(command, Assert.ContainsSingle(result.Outcome.Uploads).Command);
        context.WriteLine("completion receipt: " + JsonSerializer.Serialize(result,
            TransferJson.Default.RubyCompletionResult));
        Assert.AreEqual(Enum.Parse<ReleaseBusinessState>(outcomeName), result.Outcome.State);
        Assert.AreEqual(true, result.MutationAttempted);
        Assert.AreEqual(Enum.Parse<NativeTermination>(terminationName),
            publication.Invocation!.Upload.Command.Termination);
        Assert.AreEqual(code, publication.Invocation.Upload.Command.ExitCode);
        Assert.AreEqual(0.125, publication.Invocation.Upload.Command.DurationSeconds);
        Assert.AreEqual(terminationName == "Exited" && code == 0,
            publication.Invocation.Upload.Command.Succeeded);
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
            (_, token) => { if (reads++ != 0) token.ThrowIfCancellationRequested(); return
                Task.FromResult(absent); },
            (_, _, _) => { writes++; cancellation.Cancel(); return Task.FromResult(attempt); },
                cancellation.Token);
        context.WriteLine("publication receipt: " + JsonSerializer.Serialize(publication,
            TransferJson.Default.RubyPublicationResult));
        Assert.AreEqual(1, writes);
        Assert.AreEqual(2, reads);
        Assert.AreSame(attempt, publication.Invocation);
        Assert.IsNull(publication.Readback);
        Assert.Contains("attempted effects retained", Assert.ContainsSingle(publication.Errors));
        RubyCompletionResult complete = RubyRelease.Complete(fixture.Prepared, absent,
            fixture.Observe(ReleaseFileState.Exact), fixture.Consumer(), publication);
        Assert.AreEqual(ReleaseBusinessState.Satisfied, complete.Outcome.State);
        Assert.AreEqual(true, complete.MutationAttempted);
        Assert.AreSame(attempt.Upload, Assert.ContainsSingle(complete.Outcome.Uploads));
        Assert.AreEqual(NativeTermination.Cancelled,
            complete.Outcome.Uploads[0].Command.Termination);
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("Failed")]
    [DataRow("Cancelled")]
    [DataRow("TimedOut")]
    public void CompleteRequiresSeparateConsumer(string status)
    {
        using var fixture = new RubyCallerFixture();
        RubyObservationResult exact = fixture.Observe(ReleaseFileState.Exact);
        RubyRemoteConsumerResult? consumer = status == "missing" ? null :
            fixture.Consumer(Enum.Parse<ReleaseCheckStatus>(status));
        RubyCompletionResult result = RubyRelease.Complete(fixture.Prepared, exact, exact, consumer,
            null);
        Assert.AreEqual(ReleaseBusinessState.Unsatisfied, result.Outcome.State);
        Assert.AreEqual(false, result.MutationAttempted);
        Assert.Contains("remote-gem-output-consumer", string.Join(';', result.Outcome.Reasons));
        Assert.IsEmpty(result.Outcome.Uploads);
    }

    [TestMethod]
    [DataRow("subject")]
    [DataRow("original")]
    [DataRow("source")]
    [DataRow("check")]
    [DataRow("read")]
    [DataRow("digest")]
    [DataRow("commands")]
    [DataRow("install")]
    [DataRow("consume")]
    [DataRow("abnormal")]
    [DataRow("errors")]
    public void CompleteRejectsUnscopedOrIncompletePassedConsumer(string defect)
    {
        using var fixture = new RubyCallerFixture();
        RubyRemoteConsumerResult consumer = fixture.Consumer();
        consumer = defect switch
        {
            "subject" => consumer with { Subject = consumer.Subject with { Revision = new('b', 40) }
                },
            "original" => consumer with { Original = consumer.Original with { Sha256 = new('b', 64)
                } },
            "source" => consumer with { Source = "https://another.invalid" },
            "check" => consumer with { Check = consumer.Check with { Id =
                "different-output-consumer" } },
            "read" => consumer with { Read = null },
            "digest" => consumer with { Read = consumer.Read! with { Original =
                consumer.Read.Original! with { Sha256 = new('b', 64) } } },
            "commands" => consumer with { Commands = [] },
            "install" => consumer with { Commands = consumer.Commands.Where(c => c.Stage !=
                "remote-install").ToArray() },
            "consume" => consumer with { Commands = consumer.Commands.Where(c => c.Stage !=
                "remote-consume").ToArray() },
            "abnormal" => consumer with { Commands = [consumer.Commands[0] with { Result =
                RubyCallerFixture.Command(NativeTermination.Cancelled, 17) }, ..
                consumer.Commands[1..]] },
            _ => consumer with { Errors = ["Controlled failure"] },
        };
        RubyObservationResult exact = fixture.Observe(ReleaseFileState.Exact);
        Assert.ThrowsExactly<InvalidDataException>(() => RubyRelease.Complete(fixture.Prepared,
            exact, exact, consumer, null));
    }

    [TestMethod]
    public void CompleteMissingPublicationRetainsUnknownEffects()
    {
        using var fixture = new RubyCallerFixture();
        RubyCompletionResult result = RubyRelease.Complete(fixture.Prepared,
            fixture.Observe(ReleaseFileState.Absent), fixture.Observe(ReleaseFileState.Exact),
            fixture.Consumer(), null);
        Assert.AreEqual(ReleaseBusinessState.Unknown, result.Outcome.State);
        Assert.IsNull(result.MutationAttempted);
        Assert.IsNull(result.Publication);
        Assert.IsEmpty(result.Outcome.Uploads);
        Assert.Contains("effects remain unknown", string.Join(';', result.Outcome.Reasons));
    }

    [TestMethod]
    [DataRow("Cancelled", "Cancelled")]
    [DataRow("TimedOut", "TimedOut")]
    [DataRow("StartFailed", "Failed")]
    public async Task ObserveNativeAbnormalityRetainsUnknownAndTermination(string termination,
        string status)
    {
        using var fixture = new RubyCallerFixture("official");
        NativeCommandResult reached =
            RubyCallerFixture.Command(Enum.Parse<NativeTermination>(termination), 17)
            with { Stdout = "partial unsafe output", Stderr = "unsafe diagnostic", Error =
                "unsafe detail" };
        RubyObservationResult result = await
            RubyDestinationObservation.ObserveAsync(fixture.Prepared,
            new(Path.Combine(fixture.Root, "ruby"), Path.Combine(fixture.Root, "source.rb"),
                fixture.Directory("read")), null,
            (_, _) => Task.FromResult(reached), context.CancellationToken);
        context.WriteLine("native abnormality receipt: " + JsonSerializer.Serialize(result,
            TransferJson.Default.RubyObservationResult));
        Assert.AreEqual(ReleaseFileState.Unknown,
            Assert.ContainsSingle(result.Observation.Files).State);
        Assert.AreEqual(Enum.Parse<ReleaseCheckStatus>(status),
            Assert.ContainsSingle(result.Observation.Checks).Status);
        Assert.AreEqual(reached.Termination, result.Read.Command!.Termination);
        Assert.AreEqual(reached.ExitCode, result.Read.Command.ExitCode);
        Assert.AreEqual(reached.DurationSeconds, result.Read.Command.DurationSeconds);
        Assert.AreEqual("", result.Read.Command.Stdout);
        Assert.AreEqual("", result.Read.Command.Stderr);
        Assert.AreNotEqual(reached.Error, result.Read.Command.Error);
        Assert.IsNotEmpty(result.Read.Errors);
    }

    [TestMethod]
    [DataRow("subject")]
    [DataRow("output")]
    [DataRow("before-exact")]
    [DataRow("unbound-readback")]
    [DataRow("duration")]
    public void CompleteRejectsMismatchedPublicationEvidence(string defect)
    {
        using var fixture = new RubyCallerFixture();
        RubyObservationResult absent = fixture.Observe(ReleaseFileState.Absent), exact =
            fixture.Observe(ReleaseFileState.Exact);
        var publication = new RubyPublicationResult(fixture.Prepared.Subject, absent,
            fixture.Publish(RubyCallerFixture.Command()), exact, []);
        publication = defect switch
        {
            "subject" => publication with { Subject = publication.Subject with { Revision = new('b',
                40) } },
            "output" => publication with { Invocation = publication.Invocation! with { Upload =
                publication.Invocation.Upload with { Output = "another-output" } } },
            "before-exact" => publication with { Before = exact },
            "unbound-readback" => publication with { Invocation = null },
            _ => publication with { Invocation = fixture.Publish(RubyCallerFixture.Command() with {
                DurationSeconds = double.NaN }) },
        };
        Assert.ThrowsExactly<InvalidDataException>(() => RubyRelease.Complete(fixture.Prepared,
            absent, exact, fixture.Consumer(), publication));
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
            { cancelled.Cancel(); return Task.FromResult(fixture.Observe(ReleaseFileState.Absent));
                },
            (_, _, _) => { writes++; throw new
                AssertFailedException("Cancellation must stop mutation."); }, cancelled.Token));
        Assert.AreEqual(0, writes);
    }

    [TestMethod]
    [DataRow("buddy", "Exited", 0)]
    [DataRow("official", "Exited", 17)]
    [DataRow("official", "Cancelled", 17)]
    [DataRow("buddy", "TimedOut", 17)]
    public async Task NativePublisherControlledExecutionRetainsActualResult(string channel, string
        termination, int code)
    {
        using var fixture = new RubyCallerFixture(channel);
        var tools = new RubyPublisherTools(Path.Combine(fixture.Root, "ruby"),
            Path.Combine(fixture.Root, "gem"), fixture.Directory("publisher"));
        // Inert input for the controlled executor; never used for authentication.
        var platform = new Dictionary<string, string?> { [channel == "buddy" ? "GITHUB_TOKEN" :
            "GEM_HOST_API_KEY"] = "controlled-placeholder" };
        NativeCommandResult reached =
            RubyCallerFixture.Command(Enum.Parse<NativeTermination>(termination), code)
            with { Stdout = "unsafe partial native diagnostic", Stderr = "unsafe native diagnostic",
                Error = code == 0 ? null : "unsafe failure detail" };
        int invocations = 0;
        RubyPublishResult result = await RubyNativePublisher.PublishAsync(fixture.Preparation,
            fixture.Root, tools, platform,
            (command, _) =>
            {
                invocations++;
                Assert.AreEqual(tools.Ruby, command.Executable);
                Assert.AreEqual(180, command.DeadlineSeconds);
                CollectionAssert.AreEqual(new[] { tools.Gem, "push", "--norc", fixture.Archive,
                    "--host",
                    channel == "buddy" ? "https://rubygems.pkg.github.com/hcoona" :
                        "https://rubygems.org" }, command.Arguments);
                Assert.IsFalse(command.InheritEnvironment);
                Assert.IsTrue(command.CloseStandardInput);
                Assert.AreEqual(channel == "buddy" ? "Bearer controlled-placeholder" :
                    "controlled-placeholder", command.Environment!["GEM_HOST_API_KEY"]);
                foreach (string excluded in ExcludedPublisherEnvironment)
                    Assert.IsFalse(command.Environment.ContainsKey(excluded), excluded);
                Assert.StartsWith(tools.Scratch, command.Environment["HOME"]!);
                Assert.AreEqual(fixture.Original.Size, new FileInfo(fixture.Archive).Length);
                return Task.FromResult(reached);
            }, context.CancellationToken);
        context.WriteLine("native publisher receipt: " + JsonSerializer.Serialize(result,
            TransferJson.Default.RubyPublishResult));
        Assert.AreEqual(1, invocations);
        Assert.AreEqual(fixture.Prepared.Subject, result.Subject);
        Assert.AreEqual(fixture.Original.Output, result.Upload.Output);
        Assert.AreEqual(reached.Termination, result.Upload.Command.Termination);
        Assert.AreEqual(reached.ExitCode, result.Upload.Command.ExitCode);
        Assert.AreEqual(reached.DurationSeconds, result.Upload.Command.DurationSeconds);
        Assert.AreEqual(reached.Succeeded, result.Upload.Command.Succeeded);
        Assert.AreEqual("", result.Upload.Command.Stdout);
        Assert.AreEqual("", result.Upload.Command.Stderr);
        if (reached.Error is null) Assert.IsNull(result.Upload.Command.Error);
        else { Assert.AreNotEqual(reached.Error, result.Upload.Command.Error);
            Assert.Contains("effects remain uncertain", result.Upload.Command.Error!); }
    }

    [TestMethod]
    public async Task DryRunExcludesPublisherAndLiveCompletion()
    {
        using var fixture = new RubyCallerFixture(dry: true);
        int calls = 0;
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            RubyRelease.PublishAsync(fixture.Preparation, fixture.Root,
            fixture.Observe(ReleaseFileState.Absent),
            (_, _) => { calls++; throw new
                AssertFailedException("Dry run cannot refresh for push."); },
            (_, _, _) => { calls++; throw new AssertFailedException("Dry run cannot push."); },
                context.CancellationToken));
        RubyObservationResult exact = fixture.Observe(ReleaseFileState.Exact);
        Assert.ThrowsExactly<InvalidDataException>(() => RubyRelease.Complete(fixture.Prepared,
            exact, exact, fixture.Consumer(), null));
        Assert.AreEqual(0, calls);
    }
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
    internal RubyCallerFixture(string channel = "buddy", bool dry = false)
    {
        System.IO.Directory.CreateDirectory(Root);
        File.WriteAllText(Archive, "original");
        var subject = new ReleaseSubject(new('a', 40), channel == "official" ? "refs/heads/main" :
            "refs/heads/topic", "hcoona-release-smoke-ruby", "fixture/release-unit.yml", channel,
            channel == "official" ? "rubygems" : "github-packages-ruby", dry);
        var file = new ReleaseFile("gem-build", "gem-output", "ruby-gem",
            "hcoona-release-smoke-ruby", "1.2.3", "original.gem", 8,
            Convert.ToHexStringLower(SHA256.HashData("original"u8)));
        string[] required = ["native-input", "build", "contents", "consumer", "original-bytes"];
        var prepared = new PreparedRelease(subject, [file], required, required.Select(id => new
            ReleaseCheck(id, ReleaseCheckStatus.Passed)).ToArray());
        Preparation = new(prepared, new() { ["name"] = file.Name, ["version"] = file.Version,
            ["platform"] = "ruby", ["commit"] = subject.Revision, ["public-release"] = "true",
            ["ruby"] = "4.0.7", ["rubygems"] = "4.0.7", ["nbgv"] = "3.10.94" },
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
    internal RubyRemoteConsumerResult Consumer(ReleaseCheckStatus status =
        ReleaseCheckStatus.Passed) => new(Prepared.Subject, Original,
        Prepared.Subject.Destination == "rubygems" ? "https://rubygems.org" :
            "https://rubygems.pkg.github.com/hcoona",
        new(RubySourceState.Present, RemoteOriginal(), Command(), []), new("remote-" +
            Original.Output + "-consumer", status),
        [new("remote-original", Command()), new("remote-install", Command()), new("remote-consume",
            Command())], status == ReleaseCheckStatus.Passed ? [] :
            ["Controlled consumer did not pass"]);
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
