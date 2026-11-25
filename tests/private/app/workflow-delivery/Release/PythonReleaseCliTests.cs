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
        f.RoundTrip(new PythonIndexToolRequest(f.PackageTools.Uv, f.PackageTools.Interpreter,
            path, f.Root), TransferJson.Default.PythonIndexToolRequest);
        f.RoundTrip(new PythonIndexToolResult(f.PackageTools.Interpreter, [], []),
            TransferJson.Default.PythonIndexToolResult);
        f.RoundTrip(f.Observe(), TransferJson.Default.PythonIndexObservationResult);
        f.RoundTrip(new PythonPublicationResult(f.Prepared.Subject, f.Observe(),
            [f.PublishResult("wheel")], []),
            TransferJson.Default.PythonPublicationResult);
    }

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
    public void PrepareRejectsMissingSubjectRevisionBeforeNativeWork()
    {
        using var f = new Fixture();
        var request = new PythonPreparationRequest(f.Prepared.Subject, f.Root, f.Root, f.Root);
        string path = f.Save(request, TransferJson.Default.PythonPreparationRequest);
        JsonObject input = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        input["subject"]!.AsObject().Remove("revision");
        File.WriteAllText(path, input.ToJsonString());
        int calls = 0;
        using var output = new StringWriter();
        void Run() => PythonReleaseCli.Run(["release", "prepare-python", path], output,
            prepare: (_, _) => { calls++; return Task.FromResult(f.Preparation); });
        Assert.ThrowsExactly<JsonException>(Run);
        Assert.AreEqual(0, calls);
        Assert.AreEqual("", output.ToString());
    }

    [TestMethod]
    public void PrepareIndexToolsRejectsMissingRequirementsBeforeNativeWork()
    {
        using var f = new Fixture();
        var request = new PythonIndexToolRequest(f.PublisherTools.Uv, f.IndexTools.Interpreter,
            f.Root, f.Root);
        JsonObject input = JsonNode.Parse(JsonSerializer.Serialize(request,
            TransferJson.Default.PythonIndexToolRequest))!.AsObject();
        input.Remove("requirements");
        string path = f.SaveText(input.ToJsonString());
        using var output = new StringWriter();
        using var error = new StringWriter();

        Assert.AreEqual(2, Program.Run(["release", "prepare-python-index-tools", path],
            output, error));

        Assert.AreEqual("", output.ToString());
        Assert.Contains("requirements", error.ToString());
    }

    [TestMethod]
    public void DescribeRejectsUnknownNestedFileProperty()
    {
        using var f = new Fixture(true);
        string preparation = f.Save(f.Preparation, TransferJson.Default.PythonPreparationResult);
        string observation = f.Save(f.Observe(), TransferJson.Default.PythonIndexObservationResult);
        JsonObject input = JsonNode.Parse(File.ReadAllText(observation))!.AsObject();
        input["observation"]!["files"]![0]!["unexpected"] = true;
        File.WriteAllText(observation, input.ToJsonString());
        using var output = new StringWriter();
        void Run() => PythonReleaseCli.Run(["release", "describe-python", preparation, observation],
            output,
            publish: (_, _, _, _, _) => throw new AssertFailedException("Publisher reached."));
        Assert.ThrowsExactly<JsonException>(Run);
        Assert.AreEqual("", output.ToString());
    }

    [TestMethod]
    [DataRow("missing-format")]
    [DataRow("duplicate-format")]
    [DataRow("native-name")]
    [DataRow("native-version")]
    [DataRow("native-commit")]
    [DataRow("native-missing")]
    [DataRow("commands-missing")]
    [DataRow("command-error")]
    public void InvalidCompletePreparationFailsReceipt(string defect)
    {
        using var f = new Fixture();
        PythonPreparationResult bad = f.Preparation;
        PreparedRelease p = bad.Prepared;
        bad = defect switch
        {
            "missing-format" => bad with { Prepared = p with { Files = [p.Files[0]] } },
            "duplicate-format" => bad with
            {
                Prepared = p with
                {
                    Files = [p.Files[0], p.Files[1]
                with { Kind = "python-wheel" }]
                }
            },
            "commands-missing" => bad with { Commands = [] },
            "command-error" => bad with
            {
                Commands = [bad.Commands[0] with { Result =
                Fixture.Success with { Error = "cleanup failed" } }]
            },
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
            Prepared = f.Prepared with
            {
                Subject = f.Prepared.Subject with
                { Channel = "official", Destination = "pypi", SelectedRef = "refs/heads/main" }
            },
            Native = native,
        };
        if (valid) PythonRelease.Validate(official);
        else Assert.ThrowsExactly<InvalidDataException>(() => PythonRelease.Validate(official));
    }

    [TestMethod]
    public void SubstitutedOriginalStopsBeforeObservation()
    {
        using var f = new Fixture();
        string original = Path.Combine(f.Root, f.Prepared.Files[0].Path);
        File.WriteAllBytes(original, new byte[checked((int)f.Prepared.Files[0].Size)]);
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
            publish: (_, _, _, _, _) =>
            {
                writes++; throw new
                AssertFailedException("Publisher reached.");
            }));
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
                reads++;
                return Task.FromResult(f.Observe());
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
        Assert.AreEqual(1, reads);
        Assert.HasCount(3, scratches);
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
    [DoNotParallelize]
    [OSCondition(OperatingSystems.Linux)]
    [TestCategory("PythonNativeContract")]
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

        PythonIndexObservationResult initial = f.Observe(ReleaseFileState.Exact);
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
            exit = PythonReleaseCli.Run(args, Console.Out,
                observe: (_, _, _) => Task.FromResult(initial));
        }
        finally
        {
            Console.SetOut(previousOutput);
            Console.SetError(previousError);
            foreach ((string name, string? value) in previous)
                Environment.SetEnvironmentVariable(name, value);
        }

        Assert.AreEqual(0, exit);
        var result = Fixture.Parse(output, TransferJson.Default.PythonPublicationResult);
        PythonPublishResult invocation = Assert.ContainsSingle(result.Invocations);
        Assert.AreEqual("sdist", invocation.Upload.Output);
        Assert.IsTrue(invocation.Upload.Command.Succeeded);
        Assert.IsFalse(invocation.MaskForwardingFailed);
        Assert.DoesNotContain("::add-mask::", output.ToString());
        Assert.DoesNotContain("controlled-cli-", output.ToString());
        Assert.AreEqual(StdoutMask + Environment.NewLine + StderrMask + Environment.NewLine,
            error.ToString());
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
            publish: (_, _, _, _, _) =>
            {
                writes++; throw new
                AssertFailedException("Unreviewed write.");
            }));
        var result = Fixture.Parse(output, TransferJson.Default.PythonPublicationResult);
        Assert.AreEqual(0, writes);
        Assert.IsEmpty(result.Invocations);
        Assert.Contains("enlarge", Assert.ContainsSingle(result.Errors));
    }

    [TestMethod]
    public void PublishDryRequestRejectsBeforeDelegates()
    {
        using var f = new Fixture(dry: true);
        int calls = 0;
        using var output = new StringWriter();
        Assert.ThrowsExactly<InvalidDataException>(() => PythonReleaseCli.Run(
            f.PublishArgs(f.Observe()), output,
            observe: (_, _, _) =>
            {
                calls++; throw new
                AssertFailedException("Observation reached.");
            },
            publish: (_, _, _, _, _) =>
            {
                calls++; throw new
                AssertFailedException("Publication reached.");
            }));
        Assert.AreEqual(0, calls);
        Assert.AreEqual("", output.ToString());
    }

    [TestMethod]
    [DataRow("Exact", "Satisfied", 0)]
    [DataRow("Unknown", "Unknown", 1)]
    public void CompleteUsesFinalIndexStateAndRetainsActualFailedPublish(string sdistState,
        string business, int exit)
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
            observe: (_, _, _) => { reads++; return Task.FromResult(f.Observe()); },
            publish: (_, _, selected, _, _) =>
            {
                writes++; return
                Task.FromResult(f.PublishResult(selected, failed));
            }));
        Assert.AreEqual(1, writes);
        Assert.AreEqual(1, reads);
        string publication = f.SaveText(published.ToString());
        using var output = new StringWriter();
        Assert.AreEqual(exit, PythonReleaseCli.Run(f.CompleteArgs(f.Observe(),
            f.Observe(ReleaseFileState.Exact, Enum.Parse<ReleaseFileState>(sdistState)),
            publication), output));
        var result = Fixture.Parse(output, TransferJson.Default.PythonCompletionResult);
        Assert.AreEqual(Enum.Parse<ReleaseBusinessState>(business), result.Outcome.State);
        Assert.AreEqual(true, result.MutationAttempted);
        Assert.IsNotNull(result.Publication);
        Assert.AreEqual(failed, Assert.ContainsSingle(result.Outcome.Uploads).Command);
        Assert.AreEqual(failed,
            Assert.ContainsSingle(result.Publication.Invocations).Upload.Command);
        Assert.AreEqual(f.Prepared.Subject, result.Outcome.Subject);
        if (exit == 0) Assert.IsEmpty(result.Outcome.Reasons);
        else Assert.Contains("sdist", string.Join(";", result.Outcome.Reasons));
        f.RoundTrip(result, TransferJson.Default.PythonCompletionResult);
    }

    [TestMethod]
    public void MissingPublisherResultKeepsEffectsUnknown()
    {
        using var f = new Fixture();
        using var output = new StringWriter();
        Assert.AreEqual(1, PythonReleaseCli.Run(f.CompleteArgs(f.Observe(),
            f.Observe(ReleaseFileState.Exact, ReleaseFileState.Exact), "-"), output));
        var result = Fixture.Parse(output, TransferJson.Default.PythonCompletionResult);
        Assert.AreEqual(ReleaseBusinessState.Unknown, result.Outcome.State);
        Assert.IsNull(result.MutationAttempted);
        Assert.IsNull(result.Publication);
        Assert.IsEmpty(result.Outcome.Uploads);
        Assert.Contains("effects remain unknown", string.Join(";", result.Outcome.Reasons));
    }

    [TestMethod]
    public void CompleteRejectsAnIncompleteFinalTransferOnStderr()
    {
        using var f = new Fixture();
        PythonIndexObservationResult exact = f.Observe(ReleaseFileState.Exact,
            ReleaseFileState.Exact);
        var incomplete = exact with
        {
            Observation = exact.Observation with { Files = [exact.Observation.Files[0]] }
        };
        using var output = new StringWriter();
        using var error = new StringWriter();

        Assert.AreEqual(2, Program.Run(f.CompleteArgs(exact, incomplete, "-"), output, error));

        Assert.AreEqual("", output.ToString());
        Assert.IsNotEmpty(error.ToString());
    }

    [TestMethod]
    public void ObserveRejectsResultForAnotherRevision()
    {
        using var f = new Fixture();
        PythonIndexObservationResult observed = f.Observe(ReleaseFileState.Exact,
            ReleaseFileState.Exact);
        ReleaseObservation value = observed.Observation;
        observed = observed with
        {
            Observation = value with
            {
                Subject = value.Subject with { Revision = new('b', 40) }
            },
        };
        int writes = 0;
        using var output = new StringWriter();
        Assert.ThrowsExactly<InvalidDataException>(() => PythonReleaseCli.Run(f.ObserveArgs(),
            output,
            observe: (_, _, _) => Task.FromResult(observed),
            publish: (_, _, _, _, _) =>
            {
                writes++; throw new
                AssertFailedException("Publisher reached.");
            }));
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
            Preparation = new(prepared, new()
            {
                ["name"] = subject.Unit,
                ["version"] = "1.0.0",
                ["commit"] = subject.Revision
            },
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
        internal string[] PublishArgs(PythonIndexObservationResult initial,
            string? originals = null) => ["release", "publish-python",
            Save(Preparation, TransferJson.Default.PythonPreparationResult), originals ?? Root,
            Save(initial, TransferJson.Default.PythonIndexObservationResult), Save(IndexTools,
            TransferJson.Default.PythonIndexTools), Save(PublisherTools,
            TransferJson.Default.PythonPublisherTools)];
        internal string[] CompleteArgs(PythonIndexObservationResult initial,
            PythonIndexObservationResult final, string publication) =>
            ["release", "complete-python", Save(Preparation,
                TransferJson.Default.PythonPreparationResult), Save(initial,
                TransferJson.Default.PythonIndexObservationResult),
                Save(final, TransferJson.Default.PythonIndexObservationResult), publication];
        public void Dispose() => Directory.Delete(Root, true);
    }
}
