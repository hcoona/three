using System.Text.Json;
using System.Text.Json.Nodes;
using WorkflowDelivery.Destinations;
using WorkflowDelivery.Ecosystems;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Release;

namespace WorkflowDelivery.Tests.Release;

[TestClass]
public sealed class RubyReleaseCliTests(TestContext context)
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void PrepareTypedJsonRetainsQualifiedOrFailedCommand(bool failed)
    {
        using var fixture = new RubyCallerFixture();
        var request = new RubyPreparationRequest(fixture.Prepared.Subject, fixture.Root,
            fixture.Directory("preparation"),
            new(Path.Combine(fixture.Root, "ruby"), Path.Combine(fixture.Root, "gem"),
                Path.Combine(fixture.Root, "dotnet")));
        string path = fixture.Write("request.json", request,
            TransferJson.Default.RubyPreparationRequest);
        RubyPreparationResult native = fixture.Preparation;
        if (failed) native = native with { Errors = ["Controlled build failure"], Commands =
            [native.Commands[0] with { Result = RubyCallerFixture.Command(NativeTermination.Exited,
            17) }] };
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
        context.WriteLine("prepare-ruby receipt: " + output.ToString());
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
    [DataRow("Absent", 0)]
    [DataRow("Conflict", 1)]
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
        context.WriteLine("observe-ruby receipt: " + output.ToString());
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
    [DataRow("Exact", 0)]
    [DataRow("Absent", 1)]
    public void DescribeTypedJsonReportsOnlyHypotheticalWrites(string state, int count)
    {
        using var fixture = new RubyCallerFixture("official", dry: true);
        using var output = new StringWriter();
        using var error = new StringWriter();
        int exit = Program.Run(["release", "describe-ruby", Preparation(fixture),
            Observation(fixture, "observed.json", Enum.Parse<ReleaseFileState>(state))], output,
            error);
        Assert.AreEqual("", error.ToString());
        Assert.AreEqual(0, exit);
        context.WriteLine("describe-ruby receipt: " + output.ToString());
        ReleaseDryRun result = JsonSerializer.Deserialize(output.ToString(),
            TransferJson.Default.ReleaseDryRun)!;
        Assert.IsTrue(result.Ready);
        Assert.IsTrue(result.Subject.DryRun);
        Assert.AreEqual(fixture.Prepared.Subject, result.Subject);
        Assert.HasCount(count, result.HypotheticalWrites);
        if (count != 0) Assert.AreEqual(fixture.Original.Output,
            Assert.ContainsSingle(result.HypotheticalWrites));
        Assert.IsEmpty(result.Reasons);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void PublishTypedJsonRetainsSingleWriteAndAbnormalResult(bool abnormal)
    {
        using var fixture = new RubyCallerFixture("official");
        string readParent = fixture.Directory("read-parent"), writeParent =
            fixture.Directory("write-parent");
        var readTools = new RubySourceTools(Path.Combine(fixture.Root, "ruby"),
            Path.Combine(fixture.Root, "source.rb"), readParent);
        var writeTools = new RubyPublisherTools(readTools.Ruby, Path.Combine(fixture.Root, "gem"),
            writeParent);
        NativeCommandResult expectedNative = RubyCallerFixture.Command(abnormal ?
            NativeTermination.TimedOut : NativeTermination.Exited, abnormal ? 17 : 0);
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
                    ReleaseFileState.Exact));
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
        context.WriteLine("publish-ruby receipt: " + output.ToString());
        RubyPublicationResult result = JsonSerializer.Deserialize(output.ToString(),
            TransferJson.Default.RubyPublicationResult)!;
        Assert.AreEqual(fixture.Prepared.Subject, result.Subject);
        Assert.AreEqual(expectedNative, result.Invocation!.Upload.Command);
        Assert.AreEqual(fixture.Original.Output, result.Invocation.Upload.Output);
        Assert.AreEqual(ReleaseFileState.Exact, result.Readback!.Observation.Files[0].State);
        Assert.AreEqual(!abnormal, result.Invocation.Upload.Command.Succeeded);
        Assert.IsEmpty(result.Errors);
    }

    [TestMethod]
    [DataRow("Passed", 0)]
    [DataRow("Cancelled", 1)]
    public void ConsumeTypedJsonRetainsSeparateRequiredResult(string statusName, int expectedExit)
    {
        using var fixture = new RubyCallerFixture("official");
        var tools = new RubyRemoteConsumerTools(Path.Combine(fixture.Root, "ruby"),
            Path.Combine(fixture.Root, "facts.rb"), Path.Combine(fixture.Root, "source.rb"),
            Path.Combine(fixture.Root, "consumer.rb"), fixture.Directory("consumer"));
        RubyRemoteConsumerResult actual =
            fixture.Consumer(Enum.Parse<ReleaseCheckStatus>(statusName));
        if (statusName == "Cancelled") actual = actual with { Commands = [new("remote-original",
            RubyCallerFixture.Command(NativeTermination.Cancelled, 17))] };
        int calls = 0;
        using var output = new StringWriter();
        int? exit = RubyReleaseCli.Run(["release", "consume-ruby", Preparation(fixture),
            fixture.Root, fixture.Write("consumer-tools.json", tools,
            TransferJson.Default.RubyRemoteConsumerTools)], output,
            consume: (prepared, captured, credential, _) =>
            {
                calls++;
                Assert.AreEqual(fixture.Prepared.Subject, prepared.Subject);
                Assert.AreEqual(tools, captured);
                Assert.IsNull(credential);
                return Task.FromResult(actual);
            });
        Assert.AreEqual(expectedExit, exit);
        Assert.AreEqual(1, calls);
        context.WriteLine("consume-ruby receipt: " + output.ToString());
        RubyRemoteConsumerResult result = JsonSerializer.Deserialize(output.ToString(),
            TransferJson.Default.RubyRemoteConsumerResult)!;
        Assert.AreEqual(fixture.Prepared.Subject, result.Subject);
        Assert.AreEqual(fixture.Original, result.Original);
        Assert.AreEqual("https://rubygems.org", result.Source);
        Assert.AreEqual(actual.Check, result.Check);
        CollectionAssert.AreEqual(actual.Commands, result.Commands);
        CollectionAssert.AreEqual(actual.Errors, result.Errors);
    }

    [TestMethod]
    [DataRow("exact", 0, "Satisfied")]
    [DataRow("write", 0, "Satisfied")]
    [DataRow("failed-after-effect", 0, "Satisfied")]
    [DataRow("missing-publication", 1, "Unknown")]
    [DataRow("missing-consumer", 1, "Unsatisfied")]
    public void CompleteTypedJsonSeparatesEffectsCommandsAndSatisfaction(string journey, int
        expectedExit, string business)
    {
        using var fixture = new RubyCallerFixture();
        bool initiallyAbsent = journey is "write" or "failed-after-effect" or "missing-publication";
        string initial = Observation(fixture, "initial.json", initiallyAbsent ?
            ReleaseFileState.Absent : ReleaseFileState.Exact);
        string final = Observation(fixture, "final.json", ReleaseFileState.Exact);
        string consumer = journey == "missing-consumer" ? "-" : fixture.Write("consumer.json",
            fixture.Consumer(), TransferJson.Default.RubyRemoteConsumerResult);
        string publisher = "-";
        NativeCommandResult? native = null;
        if (journey is "write" or "failed-after-effect")
        {
            native = RubyCallerFixture.Command(NativeTermination.Exited, journey == "write" ? 0 :
                17);
            publisher = fixture.Write("publication.json", new
                RubyPublicationResult(fixture.Prepared.Subject,
                fixture.Observe(ReleaseFileState.Absent), fixture.Publish(native),
                fixture.Observe(ReleaseFileState.Exact), []),
                TransferJson.Default.RubyPublicationResult);
        }
        using var output = new StringWriter();
        using var error = new StringWriter();
        int exit = Program.Run(["release", "complete-ruby", Preparation(fixture), initial, final,
            consumer, publisher], output, error);
        Assert.AreEqual("", error.ToString());
        Assert.AreEqual(expectedExit, exit);
        context.WriteLine("complete-ruby receipt: " + output.ToString());
        RubyCompletionResult result = JsonSerializer.Deserialize(output.ToString(),
            TransferJson.Default.RubyCompletionResult)!;
        Assert.AreEqual(fixture.Prepared.Subject, result.Outcome.Subject);
        Assert.AreEqual(Enum.Parse<ReleaseBusinessState>(business), result.Outcome.State);
        if (journey == "missing-publication") Assert.IsNull(result.MutationAttempted);
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
    [DataRow("preparation", "missing-file")]
    [DataRow("observation", "missing-file")]
    [DataRow("consumer", "missing-file")]
    [DataRow("publication", "missing-file")]
    [DataRow("preparation", "corrupt")]
    [DataRow("observation", "corrupt")]
    [DataRow("consumer", "corrupt")]
    [DataRow("publication", "corrupt")]
    [DataRow("preparation", "null")]
    [DataRow("observation", "duplicate")]
    [DataRow("consumer", "unknown")]
    [DataRow("publication", "missing-property")]
    public void CompleteDamagedTransferCannotReportSuccess(string target, string defect)
    {
        using var fixture = new RubyCallerFixture();
        var paths = new Dictionary<string, string>
        {
            ["preparation"] = Preparation(fixture),
            ["observation"] = Observation(fixture, "final.json", ReleaseFileState.Exact),
            ["consumer"] = fixture.Write("consumer.json", fixture.Consumer(),
                TransferJson.Default.RubyRemoteConsumerResult),
            ["publication"] = fixture.Write("publication.json", new
                RubyPublicationResult(fixture.Prepared.Subject,
                fixture.Observe(ReleaseFileState.Absent),
                fixture.Publish(RubyCallerFixture.Command()),
                fixture.Observe(ReleaseFileState.Exact), []),
                TransferJson.Default.RubyPublicationResult),
        };
        string path = paths[target];
        if (defect == "missing-file") File.Delete(path);
        else if (defect == "corrupt") File.WriteAllText(path, "{broken");
        else if (defect == "null") File.WriteAllText(path, "null");
        else
        {
            string text = File.ReadAllText(path);
            JsonObject root = JsonNode.Parse(text)!.AsObject();
            if (defect == "unknown") root["unexpected"] = true;
            if (defect == "missing-property") root.Remove("before");
            File.WriteAllText(path, defect == "duplicate" ? "{\"observation\":" +
                root["observation"]!.ToJsonString() + "," + text.TrimStart()[1..] :
                root.ToJsonString());
        }
        using var output = new StringWriter();
        void Run() => RubyReleaseCli.Run(["release", "complete-ruby", paths["preparation"],
            Observation(fixture, "initial.json", ReleaseFileState.Absent), paths["observation"],
            paths["consumer"], paths["publication"]], output);
        if (defect == "missing-file") Assert.ThrowsExactly<FileNotFoundException>(Run);
        else if (defect is "null" or "duplicate") Assert.ThrowsExactly<InvalidDataException>(Run);
        else Assert.Throws<JsonException>(Run);
        Assert.AreEqual("", output.ToString());
    }

    [TestMethod]
    [DataRow("observation")]
    [DataRow("consumer")]
    [DataRow("publication")]
    public void CompleteMismatchedTransferRejectsAnotherRequest(string target)
    {
        using var fixture = new RubyCallerFixture();
        ReleaseSubject other = fixture.Prepared.Subject with { SelectedRef = "refs/heads/another" };
        RubyObservationResult exact = fixture.Observe(ReleaseFileState.Exact);
        RubyObservationResult final = target == "observation" ? exact with { Observation =
            exact.Observation with { Subject = other } } : exact;
        RubyRemoteConsumerResult consumer = fixture.Consumer();
        if (target == "consumer") consumer = consumer with { Subject = other };
        var publication = new RubyPublicationResult(target == "publication" ? other :
            fixture.Prepared.Subject, fixture.Observe(ReleaseFileState.Absent),
            fixture.Publish(RubyCallerFixture.Command()), exact, []);
        using var output = new StringWriter();
        Assert.ThrowsExactly<InvalidDataException>(() => RubyReleaseCli.Run(["release",
            "complete-ruby", Preparation(fixture), Observation(fixture, "initial.json",
            ReleaseFileState.Absent), fixture.Write("final.json", final,
            TransferJson.Default.RubyObservationResult), fixture.Write("consumer.json", consumer,
            TransferJson.Default.RubyRemoteConsumerResult), fixture.Write("publication.json",
            publication, TransferJson.Default.RubyPublicationResult)], output));
        Assert.AreEqual("", output.ToString());
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("changed")]
    public void ObserveOriginalTransferFailsBeforeDependency(string defect)
    {
        using var fixture = new RubyCallerFixture();
        if (defect == "missing") File.Delete(fixture.Archive);
        else File.WriteAllText(fixture.Archive, "replaced");
        int calls = 0;
        using var output = new StringWriter();
        Assert.ThrowsExactly<InvalidDataException>(() => RubyReleaseCli.Run(["release",
            "observe-ruby", Preparation(fixture), fixture.Root, Path.Combine(fixture.Root,
            "unread-tools.json")], output,
            observe: (_, _, _, _) => { calls++; throw new
                AssertFailedException("Invalid transfer must stop before read."); }));
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
}
