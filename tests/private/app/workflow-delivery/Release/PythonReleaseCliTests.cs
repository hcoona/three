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
public sealed class PythonReleaseCliTests
{
    private static readonly string[] PreparationChecks = ["native-input", "build", "contents",
        "wheel-consumer", "sdist-consumer", "original-bytes"];

    [TestMethod]
    public void TypedTransfersRoundTripCompleteCallerResults()
    {
        using var f = new Fixture();
        var request = new PythonPreparationRequest(f.Prepared.Subject, f.Root, f.Root, f.Root);
        string path = f.Save(request, TransferJson.Default.PythonPreparationRequest);
        int prepares = 0;
        using var output = new StringWriter();
        int? exit = PythonReleaseCli.Run(["release", "prepare-python", path], output,
            prepare: (actual, _) =>
            {
                Assert.AreEqual(request, actual);
                prepares++;
                return Task.FromResult(f.Preparation);
            });
        Assert.AreEqual(0, exit);
        Assert.AreEqual(1, prepares);
        PythonPreparationResult result = Fixture.Parse(output,
            TransferJson.Default.PythonPreparationResult);
        Assert.AreEqual(f.Prepared.Subject, result.Prepared.Subject);
        CollectionAssert.AreEqual(f.Prepared.Files, result.Prepared.Files);
        CollectionAssert.AreEqual(PreparationChecks, result.Prepared.RequiredChecks);
        CollectionAssert.AreEqual(f.Prepared.Checks, result.Prepared.Checks);
        CollectionAssert.AreEquivalent(f.Preparation.Native.Keys.ToArray(),
            result.Native.Keys.ToArray());
        foreach (string key in f.Preparation.Native.Keys)
            Assert.AreEqual(f.Preparation.Native[key], result.Native[key]);
        Assert.AreEqual("build", Assert.ContainsSingle(result.Commands).Check);
        Assert.AreEqual(Fixture.Success, result.Commands[0].Result);
        Assert.IsEmpty(result.Errors);
        f.RoundTrip(f.IndexTools, TransferJson.Default.PythonIndexTools);
        f.RoundTrip(f.PublisherTools, TransferJson.Default.PythonPublisherTools);
        f.RoundTrip(f.PackageTools, TransferJson.Default.PythonPackageTools);
        f.RoundTrip(new PythonIndexToolRequest(f.PackageTools.Uv, f.PackageTools.Interpreter,
            path, f.Root), TransferJson.Default.PythonIndexToolRequest);
        f.RoundTrip(new PythonIndexToolResult(f.PackageTools.Interpreter, [], []),
            TransferJson.Default.PythonIndexToolResult);
        f.RoundTrip(f.Observe(), TransferJson.Default.PythonIndexObservationResult);
        f.RoundTrip(f.Consumer("python-wheel"), TransferJson.Default.PythonRemoteConsumerResult);
        f.RoundTrip(new PythonPublicationResult(f.Prepared.Subject, f.Observe(),
            [f.PublishResult("wheel")], [f.Observe(ReleaseFileState.Exact)], []),
            TransferJson.Default.PythonPublicationResult);
    }

    [TestMethod]
    [DynamicData(nameof(MalformedTransferCases))]
    public void TypedTransfersRejectMalformedRequiredFields(string transfer, string defect)
    {
        using var f = new Fixture();
        switch (transfer)
        {
            case "request":
                Reject(new PythonPreparationRequest(f.Prepared.Subject, f.Root, f.Root, f.Root),
                    TransferJson.Default.PythonPreparationRequest, "checkout");
                break;
            case "preparation": Reject(f.Preparation, TransferJson.Default.PythonPreparationResult,
                "native"); break;
            case "index-tools": Reject(f.IndexTools, TransferJson.Default.PythonIndexTools,
                "helper"); break;
            case "publisher-tools": Reject(f.PublisherTools,
                TransferJson.Default.PythonPublisherTools, "uv"); break;
            case "consumer-tools": Reject(f.PackageTools, TransferJson.Default.PythonPackageTools,
                "deadlineSeconds"); break;
            case "index-tool-request":
                Reject(new PythonIndexToolRequest(f.PublisherTools.Uv, f.IndexTools.Interpreter,
                    f.Root, f.Root),
                    TransferJson.Default.PythonIndexToolRequest, "requirements");
                break;
            case "observation": Reject(f.Observe(),
                TransferJson.Default.PythonIndexObservationResult, "observation"); break;
            case "consumer": Reject(f.Consumer("python-wheel"),
                TransferJson.Default.PythonRemoteConsumerResult, "original"); break;
            case "publication":
                Reject(new PythonPublicationResult(f.Prepared.Subject, f.Observe(), [], [], []),
                    TransferJson.Default.PythonPublicationResult, "invocations");
                break;
            default: Assert.Fail("Unmapped typed transfer."); break;
        }
        void Reject<T>(T value, JsonTypeInfo<T> type, string field)
        {
            string path = f.Save(value, type);
            Fixture.Corrupt(path, defect, field, "", "");
            if (defect == "duplicate") Assert.ThrowsExactly<InvalidDataException>(() =>
                ReleaseCli.Read(path, type));
            else Assert.ThrowsExactly<JsonException>(() => ReleaseCli.Read(path, type));
        }
    }

    private static readonly string[] TransferKinds = ["request", "preparation", "index-tools",
        "publisher-tools", "consumer-tools", "index-tool-request", "observation", "consumer",
            "publication"];
    private static readonly string[] TransferDefects = ["duplicate", "missing", "extra"];

    public static IEnumerable<object[]> MalformedTransferCases =>
        from transfer in TransferKinds
        from defect in TransferDefects
        select new object[] { transfer, defect };

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void PrepareRetainsActualFailedOrMissingPreparation(bool missingFiles)
    {
        using var f = new Fixture();
        var failure = Fixture.Success with
        {
            Termination = NativeTermination.TimedOut,
            ExitCode = null,
            Stdout = "partial preparation",
            Stderr = "deadline reached",
            Error = "actual timeout"
        };
        PythonPreparationResult failed = f.Preparation with
        {
            Prepared = missingFiles ? f.Prepared with { Files = [] } : f.Prepared,
            Commands = [f.Preparation.Commands[0] with { Result = failure }],
            Errors = missingFiles ? [] : ["actual timeout"],
        };
        using var output = new StringWriter();
        Assert.AreEqual(1, PythonReleaseCli.Run(["release", "prepare-python",
            f.Save(new PythonPreparationRequest(f.Prepared.Subject, f.Root, f.Root, f.Root),
                TransferJson.Default.PythonPreparationRequest)], output,
            prepare: (_, _) => Task.FromResult(failed)));
        var result = Fixture.Parse(output, TransferJson.Default.PythonPreparationResult);
        Assert.AreEqual(failure, Assert.ContainsSingle(result.Commands).Result);
        CollectionAssert.AreEqual(failed.Prepared.Files, result.Prepared.Files);
        CollectionAssert.AreEqual(failed.Errors, result.Errors);
        Assert.AreEqual(f.Prepared.Subject, result.Prepared.Subject);
    }

    [TestMethod]
    [DataRow("duplicate")]
    [DataRow("missing")]
    [DataRow("extra")]
    [DataRow("nested-duplicate")]
    [DataRow("nested-missing")]
    [DataRow("nested-extra")]
    public void PrepareRejectsMalformedTypedInput(string defect)
    {
        using var f = new Fixture();
        var request = new PythonPreparationRequest(f.Prepared.Subject, f.Root, f.Root, f.Root);
        string path = f.Save(request, TransferJson.Default.PythonPreparationRequest);
        Fixture.Corrupt(path, defect, "checkout", "subject", "revision");
        int calls = 0;
        using var output = new StringWriter();
        void Run() => PythonReleaseCli.Run(["release", "prepare-python", path], output,
            prepare: (_, _) => { calls++; return Task.FromResult(f.Preparation); });
        if (defect.EndsWith("duplicate", StringComparison.Ordinal))
            Assert.ThrowsExactly<InvalidDataException>(Run);
        else Assert.ThrowsExactly<JsonException>(Run);
        Assert.AreEqual(0, calls);
        Assert.AreEqual("", output.ToString());
    }

    [TestMethod]
    [DataRow("duplicate")]
    [DataRow("missing")]
    [DataRow("extra")]
    [DataRow("nested-duplicate")]
    [DataRow("nested-missing")]
    [DataRow("nested-extra")]
    [DataRow("array-duplicate")]
    [DataRow("array-missing")]
    [DataRow("array-extra")]
    public void DescribeRejectsMalformedObservationTransfer(string defect)
    {
        using var f = new Fixture(true);
        string preparation = f.Save(f.Preparation, TransferJson.Default.PythonPreparationResult);
        string observation = f.Save(f.Observe(), TransferJson.Default.PythonIndexObservationResult);
        Fixture.Corrupt(observation, defect, "reads", "observation", "files");
        using var output = new StringWriter();
        void Run() => PythonReleaseCli.Run(["release", "describe-python", preparation, observation],
            output,
            publish: (_, _, _, _, _) => throw new AssertFailedException("Publisher reached."));
        if (defect.EndsWith("duplicate", StringComparison.Ordinal))
            Assert.ThrowsExactly<InvalidDataException>(Run);
        else Assert.ThrowsExactly<JsonException>(Run);
        Assert.AreEqual("", output.ToString());
    }

    [TestMethod]
    [DataRow("missing-format")]
    [DataRow("duplicate-format")]
    [DataRow("required-check")]
    [DataRow("failed-check")]
    [DataRow("native-name")]
    [DataRow("native-version")]
    [DataRow("native-commit")]
    [DataRow("native-missing")]
    [DataRow("commands-missing")]
    [DataRow("command-failed")]
    [DataRow("command-error")]
    [DataRow("errors")]
    [DataRow("destination")]
    [DataRow("size")]
    public void InvalidCompletePreparationFailsReceipt(string defect)
    {
        using var f = new Fixture();
        PythonPreparationResult bad = f.Preparation;
        PreparedRelease p = bad.Prepared;
        bad = defect switch
        {
            "missing-format" => bad with { Prepared = p with { Files = [p.Files[0]] } },
            "duplicate-format" => bad with { Prepared = p with { Files = [p.Files[0], p.Files[1]
                with { Kind = "python-wheel" }] } },
            "required-check" => bad with { Prepared = p with { RequiredChecks =
                p.RequiredChecks[..^1] } },
            "failed-check" => bad with { Prepared = p with { Checks = p.Checks.Select(c => c with {
                Status = ReleaseCheckStatus.Failed }).ToArray() } },
            "commands-missing" => bad with { Commands = [] },
            "command-failed" => bad with { Commands = [bad.Commands[0] with { Result =
                Fixture.Success with { ExitCode = 19 } }] },
            "command-error" => bad with { Commands = [bad.Commands[0] with { Result =
                Fixture.Success with { Error = "cleanup failed" } }] },
            "errors" => bad with { Errors = ["incomplete preparation"] },
            "destination" => bad with { Prepared = p with { Subject = p.Subject with { Destination =
                "pypi" } } },
            "size" => bad with { Prepared = p with { Files = [p.Files[0] with { Size =
                ReleasePolicy.ReadbackOriginalLimit + 1 }, p.Files[1]] } },
            _ => bad,
        };
        if (defect.StartsWith("native-", StringComparison.Ordinal))
        {
            var native = new Dictionary<string, string>(bad.Native);
            if (defect == "native-missing") native.Remove("commit");
            else native[defect[7..]] = "substituted";
            bad = bad with { Native = native };
        }
        // Each job's receive operation applies this one complete preparation check.
        Assert.ThrowsExactly<InvalidDataException>(() => PythonRelease.Validate(bad));
    }

    [TestMethod]
    [DataRow("true", true)]
    [DataRow("false", false)]
    [DataRow(null, false)]
    public void LiveOfficialPreparationRequiresNativePublicRelease(string? release,
        bool valid)
    {
        using var f = new Fixture();
        var native = new Dictionary<string, string>(f.Preparation.Native);
        if (release is not null) native["public-release"] = release;
        PythonPreparationResult official = f.Preparation with
        {
            Prepared = f.Prepared with { Subject = f.Prepared.Subject with
            { Channel = "official", Destination = "pypi", SelectedRef = "refs/heads/main" } },
            Native = native,
        };
        if (valid) PythonRelease.Validate(official);
        else Assert.ThrowsExactly<InvalidDataException>(() => PythonRelease.Validate(official));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void SubstitutedOriginalStopsBeforeObservation(bool missing)
    {
        using var f = new Fixture();
        string original = Path.Combine(f.Root, f.Prepared.Files[0].Path);
        if (missing) File.Delete(original);
        else File.WriteAllBytes(original, new byte[checked((int)f.Prepared.Files[0].Size)]);
        int reads = 0;
        using var output = new StringWriter();
        Assert.ThrowsExactly<InvalidDataException>(() => PythonReleaseCli.Run(f.ObserveArgs(),
            output,
            observe: (_, _, _) => { reads++; return Task.FromResult(f.Observe()); }));
        Assert.AreEqual(0, reads);
        Assert.AreEqual("", output.ToString());
    }

    [TestMethod]
    public void DryDescribeDoesNotInvokePublisher()
    {
        using var f = new Fixture(true);
        int writes = 0;
        using var output = new StringWriter();
        Assert.AreEqual(0, PythonReleaseCli.Run(["release", "describe-python",
            f.Save(f.Preparation, TransferJson.Default.PythonPreparationResult),
            f.Save(f.Observe(ReleaseFileState.Exact),
                TransferJson.Default.PythonIndexObservationResult)], output,
            publish: (_, _, _, _, _) => { writes++; throw new
                AssertFailedException("Publisher reached."); }));
        ReleaseDryRun result = Fixture.Parse(output, TransferJson.Default.ReleaseDryRun);
        Assert.AreEqual(f.Prepared.Subject, result.Subject);
        Assert.IsTrue(result.Ready);
        CollectionAssert.AreEqual((string[])["sdist"], result.HypotheticalWrites);
        Assert.IsEmpty(result.Reasons);
        Assert.AreEqual(0, writes);
        f.RoundTrip(result, TransferJson.Default.ReleaseDryRun);
    }

    [TestMethod]
    public void ObserveBindsPreparedSubjectAndIndexTools()
    {
        using var f = new Fixture();
        int reads = 0;
        using var output = new StringWriter();
        Assert.AreEqual(0, PythonReleaseCli.Run(f.ObserveArgs(), output, observe: (prepared, tools,
            token) =>
        {
            Assert.AreEqual(f.Prepared.Subject, prepared.Subject);
            CollectionAssert.AreEqual(f.Prepared.Files, prepared.Files);
            Assert.AreEqual(f.IndexTools, tools);
            Assert.IsFalse(token.IsCancellationRequested);
            reads++;
            return Task.FromResult(f.Observe());
        }));
        var result = Fixture.Parse(output, TransferJson.Default.PythonIndexObservationResult);
        Assert.AreEqual(1, reads);
        Assert.AreEqual(f.Prepared.Subject, result.Observation.Subject);
        CollectionAssert.AreEqual((string[])["wheel", "sdist"], result.Observation.Files.Select(v =>
            v.Output).ToArray());
        Assert.IsTrue(result.Observation.Files.All(v => v.State == ReleaseFileState.Absent));
        Assert.AreEqual("controlled read", Assert.ContainsSingle(result.Reads).Detail);
    }

    [TestMethod]
    public void PublishUsesFreshPhaseScratchAndRetainsOriginalWrites()
    {
        using var f = new Fixture();
        var scratches = new HashSet<string>(StringComparer.Ordinal);
        var writes = new List<string>();
        int reads = 0;
        using var output = new StringWriter();
        Assert.AreEqual(0, PythonReleaseCli.Run(f.PublishArgs(f.Observe()), output,
            observe: (prepared, tools, _) =>
            {
                Assert.AreEqual(f.Prepared.Subject, prepared.Subject);
                Assert.AreEqual(f.IndexTools.Interpreter, tools.Interpreter);
                Assert.AreEqual(f.IndexTools.Helper, tools.Helper);
                Fresh(tools.Scratch, f.IndexTools.Scratch);
                return Task.FromResult(reads++ switch
                {
                    0 => f.Observe(),
                    1 => f.Observe(ReleaseFileState.Exact),
                    _ => f.Observe(ReleaseFileState.Exact, ReleaseFileState.Exact),
                });
            }, publish: (prepared, originals, selected, tools, _) =>
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
                return Task.FromResult(f.PublishResult(selected));
            }));
        var result = Fixture.Parse(output, TransferJson.Default.PythonPublicationResult);
        CollectionAssert.AreEqual((string[])["wheel", "sdist"], writes);
        CollectionAssert.AreEqual(writes.ToArray(), result.Invocations.Select(v =>
            v.Upload.Output).ToArray());
        Assert.AreEqual(3, reads);
        Assert.HasCount(5, scratches);
        Assert.HasCount(2, result.Readbacks);
        Assert.IsEmpty(result.Errors);
        Assert.AreEqual(ReleaseFileState.Absent, result.Before.Observation.Files[0].State);
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
    public void PublishCannotEnlargeInitialBoundRequest()
    {
        using var f = new Fixture();
        int writes = 0;
        using var output = new StringWriter();
        Assert.AreEqual(1, PythonReleaseCli.Run(f.PublishArgs(f.Observe(ReleaseFileState.Exact)),
            output,
            observe: (_, _, _) => Task.FromResult(f.Observe()),
            publish: (_, _, _, _, _) => { writes++; throw new
                AssertFailedException("Unreviewed write."); }));
        var result = Fixture.Parse(output, TransferJson.Default.PythonPublicationResult);
        Assert.AreEqual(0, writes);
        Assert.IsEmpty(result.Invocations);
        Assert.Contains("enlarge", Assert.ContainsSingle(result.Errors));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void PublishExactOrDryRequestRejectsBeforeDelegates(bool dry)
    {
        using var f = new Fixture(dry);
        int calls = 0;
        using var output = new StringWriter();
        Assert.ThrowsExactly<InvalidDataException>(() => PythonReleaseCli.Run(
            f.PublishArgs(dry ? f.Observe() : f.Observe(ReleaseFileState.Exact,
                ReleaseFileState.Exact)), output,
            observe: (_, _, _) => { calls++; throw new
                AssertFailedException("Observation reached."); },
            publish: (_, _, _, _, _) => { calls++; throw new
                AssertFailedException("Publication reached."); }));
        Assert.AreEqual(0, calls);
        Assert.AreEqual("", output.ToString());
    }

    [TestMethod]
    [DataRow("python-wheel", "Passed", 0)]
    [DataRow("python-sdist", "Passed", 0)]
    [DataRow("python-wheel", "Failed", 1)]
    [DataRow("python-sdist", "Cancelled", 1)]
    public void ConsumeReturnsActualFormatResult(string kind, string status, int expectedExit)
    {
        using var f = new Fixture();
        var actual = f.Consumer(kind, Enum.Parse<ReleaseCheckStatus>(status));
        int calls = 0;
        using var output = new StringWriter();
        Assert.AreEqual(expectedExit, PythonReleaseCli.Run(["release", "consume-python",
            f.Save(f.Preparation, TransferJson.Default.PythonPreparationResult), f.Root, kind,
            f.Save(f.PackageTools, TransferJson.Default.PythonPackageTools)], output,
            consume: (prepared, requested, tools, _) =>
            {
                Assert.AreEqual(f.Prepared.Subject, prepared.Subject);
                Assert.AreEqual(kind, requested);
                Assert.AreEqual(f.PackageTools.Scratch, tools.Scratch);
                Assert.AreEqual(f.PackageTools.Uv, tools.Uv);
                Assert.AreEqual(f.PackageTools.Helper, tools.Helper);
                Assert.AreEqual(f.PackageTools.Interpreter, tools.Interpreter);
                Assert.AreEqual(f.PackageTools.Checkout, tools.Checkout);
                Assert.AreEqual(180, tools.DeadlineSeconds);
                Assert.IsEmpty(tools.Environment!);
                Assert.IsFalse(tools.InheritEnvironment);
                calls++;
                return Task.FromResult(actual);
            }));
        var result = Fixture.Parse(output, TransferJson.Default.PythonRemoteConsumerResult);
        Assert.AreEqual(1, calls);
        Assert.AreEqual(actual.Subject, result.Subject);
        Assert.AreEqual(actual.Original, result.Original);
        Assert.AreEqual(actual.Index, result.Index);
        Assert.AreEqual(actual.Check, result.Check);
        CollectionAssert.AreEqual(actual.Errors, result.Errors);
        CollectionAssert.AreEqual(actual.Commands.Select(v => v.Result).ToArray(),
            result.Commands.Select(v => v.Result).ToArray());
    }

    [TestMethod]
    [DataRow("Passed", "Satisfied", 0)]
    [DataRow("Failed", "Unsatisfied", 1)]
    [DataRow("Cancelled", "Unsatisfied", 1)]
    public void CompleteRetainsBothConsumersAndActualFailedPublish(string sdistStatus, string
        business, int exit)
    {
        using var f = new Fixture();
        var failed = Fixture.Success with
        {
            Termination = NativeTermination.TimedOut,
            ExitCode = null,
            Stdout = "partial response",
            Stderr = "actual timeout",
            Error = "deadline reached"
        };
        int reads = 0;
        int writes = 0;
        using var published = new StringWriter();
        Assert.AreEqual(0, PythonReleaseCli.Run(f.PublishArgs(f.Observe()), published,
            observe: (_, _, _) => Task.FromResult(reads++ == 0 ? f.Observe() :
                f.Observe(ReleaseFileState.Exact, ReleaseFileState.Exact)),
            publish: (_, _, selected, _, _) => { writes++; return
                Task.FromResult(f.PublishResult(selected, failed)); }));
        Assert.AreEqual(1, writes);
        Assert.AreEqual(2, reads);
        string publication = f.SaveText(published.ToString());
        using var output = new StringWriter();
        Assert.AreEqual(exit, PythonReleaseCli.Run(f.CompleteArgs(f.Observe(),
            f.Consumer("python-sdist", Enum.Parse<ReleaseCheckStatus>(sdistStatus)), publication),
                output));
        var result = Fixture.Parse(output, TransferJson.Default.PythonCompletionResult);
        Assert.AreEqual(Enum.Parse<ReleaseBusinessState>(business), result.Outcome.State);
        Assert.AreEqual(true, result.MutationAttempted);
        Assert.IsNotNull(result.Publication);
        Assert.AreEqual(failed, Assert.ContainsSingle(result.Outcome.Uploads).Command);
        Assert.AreEqual(failed,
            Assert.ContainsSingle(result.Publication.Invocations).Upload.Command);
        Assert.AreEqual(f.Prepared.Subject, result.Outcome.Subject);
        if (exit == 0) Assert.IsEmpty(result.Outcome.Reasons);
        else Assert.Contains("remote-sdist-consumer", string.Join(";", result.Outcome.Reasons));
        f.RoundTrip(result, TransferJson.Default.PythonCompletionResult);
    }

    [TestMethod]
    public void MissingPublisherResultKeepsEffectsUnknown()
    {
        using var f = new Fixture();
        using var output = new StringWriter();
        Assert.AreEqual(1, PythonReleaseCli.Run(f.CompleteArgs(f.Observe(),
            f.Consumer("python-sdist"), "-"), output));
        var result = Fixture.Parse(output, TransferJson.Default.PythonCompletionResult);
        Assert.AreEqual(ReleaseBusinessState.Unknown, result.Outcome.State);
        Assert.IsNull(result.MutationAttempted);
        Assert.IsNull(result.Publication);
        Assert.IsEmpty(result.Outcome.Uploads);
        Assert.Contains("effects remain unknown", string.Join(";", result.Outcome.Reasons));
    }

    [TestMethod]
    [DataRow("wheel")]
    [DataRow("sdist")]
    public void CompleteRequiresBothConsumerTransfers(string missing)
    {
        using var f = new Fixture();
        string[] args = f.CompleteArgs(f.Observe(ReleaseFileState.Exact, ReleaseFileState.Exact),
            f.Consumer("python-sdist"), "-");
        args[missing == "wheel" ? 5 : 6] = "-";
        using var output = new StringWriter();
        Assert.AreEqual(1, PythonReleaseCli.Run(args, output));
        var result = Fixture.Parse(output, TransferJson.Default.PythonCompletionResult);
        Assert.AreEqual(ReleaseBusinessState.Unsatisfied, result.Outcome.State);
        Assert.AreEqual(false, result.MutationAttempted);
        Assert.IsEmpty(result.Outcome.Uploads);
        Assert.Contains("remote-" + missing + "-consumer", string.Join(";",
            result.Outcome.Reasons));
    }

    [TestMethod]
    [DataRow("subject")]
    [DataRow("missing-file")]
    [DataRow("extra-file")]
    [DataRow("digest")]
    [DataRow("size")]
    [DataRow("required-check")]
    public void ObserveRejectsMismatchedCompleteResult(string defect)
    {
        using var f = new Fixture();
        PythonIndexObservationResult observed = f.Observe(ReleaseFileState.Exact,
            ReleaseFileState.Exact);
        ReleaseObservation value = observed.Observation;
        observed = observed with
        {
            Observation = defect switch
            {
                "subject" => value with { Subject = value.Subject with { Revision = new string('b',
                    40) } },
                "missing-file" => value with { Files = [value.Files[0]] },
                "extra-file" => value with { Files = [.. value.Files, value.Files[0] with { Output =
                    "other" }] },
                "digest" => value with { Files = [value.Files[0] with { Sha256 = new string('f', 64)
                    }, value.Files[1]] },
                "size" => value with { Files = [value.Files[0] with { Size = value.Files[0].Size + 1
                    }, value.Files[1]] },
                _ => value with { RequiredChecks = [] },
            },
        };
        int writes = 0;
        using var output = new StringWriter();
        Assert.ThrowsExactly<InvalidDataException>(() => PythonReleaseCli.Run(f.ObserveArgs(),
            output,
            observe: (_, _, _) => Task.FromResult(observed),
            publish: (_, _, _, _, _) => { writes++; throw new
                AssertFailedException("Publisher reached."); }));
        Assert.AreEqual(0, writes);
        Assert.AreEqual("", output.ToString());
    }

    private sealed class Fixture : IDisposable
    {
        internal static readonly NativeCommandResult Success = new(NativeTermination.Exited, 0,
            "controlled", "", 0.25, null);
        internal string Root { get; } = Directory.CreateTempSubdirectory("python-cli-").FullName;
        internal PythonPreparationResult Preparation { get; }
        internal PreparedRelease Prepared => Preparation.Prepared;
        internal PythonIndexTools IndexTools { get; }
        internal PythonPublisherTools PublisherTools { get; }
        internal PythonPackageTools PackageTools { get; }
        internal Fixture(bool dry = false)
        {
            Directory.CreateDirectory(Path.Combine(Root, "archives"));
            var subject = new ReleaseSubject(new string('a', 40), "refs/heads/development",
                "hcoona-release-smoke-python", "release.yml", "buddy", "testpypi", dry);
            ReleaseFile[] files = [Original("wheel", "python-wheel", "package.whl"),
                Original("sdist", "python-sdist", "package.tar.gz")];
            PreparedRelease prepared = new(subject, files, PreparationChecks,
                PreparationChecks.Select(id => new ReleaseCheck(id,
                    ReleaseCheckStatus.Passed)).ToArray());
            Preparation = new(prepared, new() { ["name"] = subject.Unit, ["version"] = "1.0.0",
                ["commit"] = subject.Revision },
                [new("build", Command(), Success)], []);
            IndexTools = new(Path.Combine(Root, "python"), Path.Combine(Root, "packages.py"),
                Directory.CreateDirectory(Path.Combine(Root, "index")).FullName);
            PublisherTools = new(Path.Combine(Root, "uv"),
                Directory.CreateDirectory(Path.Combine(Root, "publisher")).FullName);
            PackageTools = new(Root, Directory.CreateDirectory(Path.Combine(Root,
                "consumer")).FullName,
                PublisherTools.Uv, IndexTools.Interpreter, IndexTools.Helper, 180, new
                    Dictionary<string, string?>(), false);
        }
        private ReleaseFile Original(string output, string kind, string name)
        {
            string relative = "archives/" + name;
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes("controlled original " + kind);
            File.WriteAllBytes(Path.Combine(Root, relative), bytes);
            return new("distributions", output, kind, "hcoona-release-smoke-python", "1.0.0",
                relative,
                bytes.LongLength, Convert.ToHexStringLower(SHA256.HashData(bytes)));
        }
        private NativeCommand Command() => new(Path.Combine(Root, "uv"), Root, ["controlled"], 180);
        internal PythonIndexObservationResult Observe(ReleaseFileState wheel =
            ReleaseFileState.Absent,
            ReleaseFileState sdist = ReleaseFileState.Absent) => new(new(Prepared.Subject,
            Prepared.Files.Select(file => new ReleaseFileObservation(file.Output, file.Name,
                file.Version,
                file.Output == "wheel" ? wheel : sdist, file.Size, file.Sha256)).ToArray(),
            ["public-index-files"], [new("public-index-files", ReleaseCheckStatus.Passed)]),
            [new("index", 200, "controlled read")]);
        internal PythonPublishResult PublishResult(string output, NativeCommandResult? command =
            null)
        {
            Assert.Contains(output, Prepared.Files.Select(file => file.Output).ToArray());
            return new(new(output, command ?? Success), false, "controlled publisher");
        }
        internal PythonRemoteConsumerResult Consumer(string kind, ReleaseCheckStatus status =
            ReleaseCheckStatus.Passed)
        {
            ReleaseFile file = Prepared.Files.Single(v => v.Kind == kind);
            NativeCommandResult actual = status == ReleaseCheckStatus.Passed ? Success : Success
                with
            {
                Termination = status == ReleaseCheckStatus.Cancelled ? NativeTermination.Cancelled :
                    NativeTermination.Exited,
                ExitCode = 23,
                Error = "controlled consumer failure"
            };
            return new(Prepared.Subject, file, "https://test.pypi.org/simple/",
                new("remote-" + file.Output + "-consumer", status), [new("install", Command(),
                    Success), new("consume", Command(), actual)],
                status == ReleaseCheckStatus.Passed ? [] : ["controlled consumer failure"]);
        }
        internal string Save<T>(T value, JsonTypeInfo<T> type) =>
            SaveText(JsonSerializer.Serialize(value, type));
        internal string SaveText(string json)
        {
            string path = Path.Combine(Root, Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(path, json);
            return path;
        }
        internal void RoundTrip<T>(T value, JsonTypeInfo<T> type)
        {
            string original = JsonSerializer.Serialize(value, type);
            T copy = ReleaseCli.Read(Save(value, type), type);
            Assert.AreEqual(original, JsonSerializer.Serialize(copy, type));
        }
        internal static T Parse<T>(StringWriter output, JsonTypeInfo<T> type) =>
            JsonSerializer.Deserialize(output.ToString(), type) ?? throw new
                AssertFailedException("Missing typed CLI output.");
        internal string[] ObserveArgs() => ["release", "observe-python", Save(Preparation,
            TransferJson.Default.PythonPreparationResult), Root, Save(IndexTools,
            TransferJson.Default.PythonIndexTools)];
        internal string[] PublishArgs(PythonIndexObservationResult initial) => ["release",
            "publish-python", Save(Preparation, TransferJson.Default.PythonPreparationResult), Root,
            Save(initial, TransferJson.Default.PythonIndexObservationResult), Save(IndexTools,
            TransferJson.Default.PythonIndexTools), Save(PublisherTools,
            TransferJson.Default.PythonPublisherTools)];
        internal string[] CompleteArgs(PythonIndexObservationResult initial,
            PythonRemoteConsumerResult sdist, string publication) =>
            ["release", "complete-python", Save(Preparation,
                TransferJson.Default.PythonPreparationResult), Save(initial,
                TransferJson.Default.PythonIndexObservationResult),
                Save(Observe(ReleaseFileState.Exact, ReleaseFileState.Exact),
                    TransferJson.Default.PythonIndexObservationResult),
                Save(Consumer("python-wheel"), TransferJson.Default.PythonRemoteConsumerResult),
                    Save(sdist, TransferJson.Default.PythonRemoteConsumerResult), publication];
        internal static void Corrupt(string path, string defect, string field, string nested, string
            nestedField)
        {
            JsonObject root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            JsonObject target = defect.StartsWith("array-", StringComparison.Ordinal)
                ? root[nested]![nestedField]!.AsArray()[0]!.AsObject()
                : defect.StartsWith("nested-", StringComparison.Ordinal) ? root[nested]!.AsObject()
                    : root;
            string key = defect.StartsWith("array-", StringComparison.Ordinal) ? "output"
                : defect.StartsWith("nested-", StringComparison.Ordinal) ? nestedField : field;
            if (defect.EndsWith("missing", StringComparison.Ordinal)) target.Remove(key);
            else if (defect.EndsWith("extra", StringComparison.Ordinal)) target["unrecognized"] =
                true;
            string json = root.ToJsonString();
            if (defect.EndsWith("duplicate", StringComparison.Ordinal))
            {
                string needle = JsonSerializer.Serialize(key) + ":" + target[key]!.ToJsonString();
                json = json.Replace(needle, needle + "," + needle, StringComparison.Ordinal);
            }
            File.WriteAllText(path, json);
        }
        public void Dispose() => Directory.Delete(Root, true);
    }
}
