using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WorkflowDelivery.Ecosystems;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Release;

namespace WorkflowDelivery.Tests.Release;

[TestClass]
public sealed class RubyRemoteConsumerTests(TestContext context)
{
    private static readonly string[] StageOrder = ["source", "archive", "install", "consume"];
    private static readonly string[] ConsumerStageOrder =
        ["remote-original", "remote-install", "remote-consume"];
    private static readonly string[] SourceOnly = ["source"];
    private static readonly string[] SourceArchive = ["source", "archive"];
    private static readonly string[] InstalledPhases = ["install", "consume"];
    private static readonly string[] LibPaths = ["lib"];

    [TestMethod]
    [DataRow("buddy", "github-packages-ruby", "https://rubygems.pkg.github.com/hcoona")]
    [DataRow("official", "rubygems", "https://rubygems.org")]
    public async Task ExactOwnedOriginalIsConsumedFromSelectedSource(
        string channel, string destination, string host)
    {
        using var fixture = new Fixture(channel, destination);
        RubyRemoteConsumerResult result = await fixture.RunAsync(context.CancellationToken);

        RequireResult(fixture, result, ReleaseCheckStatus.Passed);
        Assert.AreEqual(host, result.Source);
        Assert.IsEmpty(result.Errors);
        Assert.IsNotNull(result.Read);
        Assert.AreEqual(RubySourceState.Present, result.Read.State);
        Assert.IsNotNull(result.Read.Original);
        Assert.AreEqual(fixture.Remote, result.Read.Original.Path);
        Assert.AreEqual(fixture.Prepared.Files[0].Sha256, Digest(fixture.Remote));
        Assert.AreEqual(fixture.Prepared.Files[0].Size, new FileInfo(fixture.Remote).Length);
        Assert.IsFalse(File.Exists(fixture.Prepared.Files[0].Path));
        CollectionAssert.AreEqual(
            StageOrder, fixture.Stages.ToArray());
        CollectionAssert.AreEqual(
            ConsumerStageOrder,
            result.Commands.Select(command => command.Stage).ToArray());
        Assert.AreEqual(fixture.Tools.Ruby, fixture.Commands[0].Executable);
        Assert.AreEqual(fixture.Tools.SourceHelper, fixture.Commands[0].Arguments[0]);
        JsonObject read = fixture.Requests[0];
        Assert.AreEqual(host, read["host"]!.GetValue<string>());
        Assert.AreEqual(Fixture.Name, read["name"]!.GetValue<string>());
        Assert.AreEqual(Fixture.Version, read["version"]!.GetValue<string>());
        Assert.AreEqual(Path.GetDirectoryName(fixture.Remote),
            read["directory"]!.GetValue<string>());
        Assert.AreEqual(fixture.Tools.FactsHelper, fixture.Commands[1].Arguments[0]);
        Assert.AreEqual("archive", fixture.Requests[1]["operation"]!.GetValue<string>());
        Assert.AreEqual(fixture.Remote, fixture.Requests[1]["path"]!.GetValue<string>());
        foreach (int index in new[] { 2, 3 })
        {
            NativeCommand command = fixture.Commands[index];
            Assert.AreEqual(fixture.Tools.ConsumerHelper, command.Arguments[0]);
            JsonObject request = fixture.Requests[index];
            Assert.AreEqual(fixture.Stages[index], request["operation"]!.GetValue<string>());
            Assert.AreEqual(fixture.Remote, request["original"]!.GetValue<string>());
            Assert.AreEqual(Fixture.Version, request["version"]!.GetValue<string>());
            Assert.AreEqual(Digest(fixture.Remote), request["sha256"]!.GetValue<string>());
            Assert.AreEqual(command.Environment!["GEM_HOME"],
                request["install_dir"]!.GetValue<string>());
            Assert.StartsWith(fixture.Scratch, command.Directory);
            Assert.DoesNotContain(Environment.CurrentDirectory, command.Directory);
        }
        Assert.AreNotEqual(fixture.Commands[2].Arguments[1], fixture.Commands[3].Arguments[1]);
        RequirePortable(result);
    }

    [TestMethod]
    [DataRow("buddy", "github-packages-ruby", true)]
    [DataRow("official", "rubygems", false)]
    public async Task ReadCredentialsEndBeforeCleanConsumer(
        string channel, string destination, bool credentialed)
    {
        using var fixture = new Fixture(channel, destination);
        RubyRemoteConsumerResult result = await fixture.RunAsync(context.CancellationToken);
        RequireResult(fixture, result, ReleaseCheckStatus.Passed);
        NativeCommand source = fixture.Commands[0];
        Assert.AreEqual(credentialed, source.Environment!.ContainsKey("RUBY_SOURCE_TOKEN"));
        if (credentialed)
        {
            Assert.AreEqual(Fixture.Credential.User, source.Environment["RUBY_SOURCE_USER"]);
            Assert.AreEqual(Fixture.Credential.Token, source.Environment["RUBY_SOURCE_TOKEN"]);
        }
        foreach (NativeCommand command in fixture.Commands)
        {
            Assert.IsFalse(command.InheritEnvironment);
            Assert.AreEqual(90, command.DeadlineSeconds);
            Assert.IsNotNull(command.Environment);
            Assert.DoesNotContain(Fixture.Credential.Token, string.Join(" ", command.Arguments));
            string[] allowed = [.. Fixture.StateKeys, "SystemRoot", "WINDIR", "PATHEXT",
                "PATH", "LANG", "LC_ALL"];
            if (credentialed && command == source)
                allowed = [.. allowed, "RUBY_SOURCE_USER", "RUBY_SOURCE_TOKEN"];
            CollectionAssert.AreEquivalent(allowed, command.Environment.Keys.ToArray());
            foreach (string key in Fixture.StateKeys)
            {
                string path = command.Environment[key]!;
                Assert.StartsWith(fixture.Scratch + Path.DirectorySeparatorChar, path);
                Assert.IsTrue(Directory.Exists(path), key);
            }
            foreach (string excluded in new[] { "RUBYOPT", "RUBYLIB", "GEM_HOST_API_KEY",
                "BUNDLE_GEMFILE", "BUNDLE_PATH", "GITHUB_TOKEN", "HTTP_PROXY", "HTTPS_PROXY" })
                Assert.IsFalse(command.Environment.ContainsKey(excluded), excluded);
        }
        Assert.IsTrue(source.CloseStandardInput);
        foreach (NativeCommand command in fixture.Commands.Skip(1))
        {
            Assert.IsFalse(command.Environment!.ContainsKey("RUBY_SOURCE_TOKEN"));
            Assert.IsFalse(command.Environment.ContainsKey("RUBY_SOURCE_USER"));
            foreach (string key in Fixture.StateKeys)
                Assert.AreNotEqual(source.Environment[key], command.Environment[key], key);
        }
        foreach (NativeCommand command in fixture.Commands.Skip(2))
            Assert.IsTrue(command.CloseStandardInput);
        Assert.AreEqual(fixture.Commands[2].Environment!["GEM_HOME"],
            fixture.Commands[3].Environment!["GEM_PATH"]);
        foreach (string path in Directory.GetFiles(fixture.Scratch, "*.json",
            SearchOption.AllDirectories))
            Assert.DoesNotContain(Fixture.Credential.Token, File.ReadAllText(path));
        RequirePortable(result);
    }

    [TestMethod]
    public async Task InspectionClosesStandardInput()
    {
        using var fixture = new Fixture();
        RubyRemoteConsumerResult result = await fixture.RunAsync(context.CancellationToken);
        RequireResult(fixture, result, ReleaseCheckStatus.Passed);
        Assert.IsTrue(fixture.Commands[1].CloseStandardInput,
            "The remote archive inspector must receive EOF, as do source/install/API children.");
    }

    [TestMethod]
    public async Task SourceCompletionPrecedesInspectionAndSeparateConsumers()
    {
        using var fixture = new Fixture();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Before = async (stage, token) =>
        {
            if (stage == "source")
            {
                entered.SetResult();
                await completed.Task.WaitAsync(token);
            }
            else
                Assert.IsTrue(completed.Task.IsCompletedSuccessfully,
                    "Product inspection must wait for the credentialed source process result.");
        };
        Task<RubyRemoteConsumerResult> pending = fixture.RunAsync(context.CancellationToken);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), context.CancellationToken);
        try
        {
            Assert.IsFalse(pending.IsCompleted);
            CollectionAssert.AreEqual(SourceOnly, fixture.Stages.ToArray());
        }
        finally
        {
            completed.TrySetResult();
        }
        RubyRemoteConsumerResult result = await pending;
        RequireResult(fixture, result, ReleaseCheckStatus.Passed);
        CollectionAssert.AreEqual(
            StageOrder, fixture.Stages.ToArray());
    }

    [TestMethod]
    [DataRow("absent", "Absent")]
    [DataRow("unknown", "Unknown")]
    [DataRow("malformed", "Unknown")]
    [DataRow("oversized", "Unknown")]
    [DataRow("missing", "Unknown")]
    [DataRow("outside", "Unknown")]
    [DataRow("relative", "Unknown")]
    [DataRow("name", "Unknown")]
    [DataRow("version", "Unknown")]
    [DataRow("platform", "Unknown")]
    [DataRow("size", "Unknown")]
    [DataRow("reported-hash", "Unknown")]
    [DataRow("changed", "Unknown")]
    [DataRow("different", "Present")]
    public async Task SourceCannotCertifyUnrelatedOrUnavailableOriginal(
        string defect, string expectedState)
    {
        using var fixture = new Fixture();
        fixture.Answer = (stage, value) =>
        {
            if (stage != "source") return value;
            JsonObject answer = JsonNode.Parse(value)!.AsObject();
            switch (defect)
            {
                case "absent": return "{\"state\":\"absent\"}";
                case "unknown": return "{\"state\":\"unknown\"}";
                case "malformed": return "{" + Fixture.Unsafe;
                case "oversized":
                    answer["padding"] = new string('x', 256 * 1024);
                    break;
                case "missing": File.Delete(fixture.Remote); break;
                case "outside":
                    string outside = Path.Combine(fixture.Root, "unowned.gem");
                    File.Copy(fixture.Remote, outside);
                    answer["path"] = outside;
                    break;
                case "relative": answer["path"] = "original.gem"; break;
                case "size": answer["size"] = Fixture.Bytes.Length + 1; break;
                case "reported-hash": answer["sha256"] = new string('0', 64); break;
                case "changed": File.WriteAllBytes(fixture.Remote, Fixture.OtherBytes); break;
                case "different":
                    File.WriteAllBytes(fixture.Remote, Fixture.OtherBytes);
                    answer["sha256"] = Digest(fixture.Remote);
                    break;
                default: answer[defect] = "other"; break;
            }
            return answer.ToJsonString();
        };
        RubyRemoteConsumerResult result = await fixture.RunAsync(context.CancellationToken);
        RequireResult(fixture, result, ReleaseCheckStatus.Failed);
        Assert.IsNotNull(result.Read);
        Assert.AreEqual(Enum.Parse<RubySourceState>(expectedState), result.Read.State);
        CollectionAssert.AreEqual(SourceOnly, fixture.Stages.ToArray());
        Assert.IsEmpty(result.Commands);
        RequirePortable(result);
    }

    [TestMethod]
    [DataRow("name")]
    [DataRow("version")]
    [DataRow("platform")]
    [DataRow("archive")]
    [DataRow("dependencies")]
    [DataRow("extensions")]
    [DataRow("missing-extensions")]
    [DataRow("repository")]
    [DataRow("missing-repository")]
    [DataRow("require-paths")]
    [DataRow("missing-content")]
    [DataRow("extra-content")]
    [DataRow("witness")]
    [DataRow("duplicate-content")]
    [DataRow("missing")]
    [DataRow("malformed")]
    public async Task UnqualifiedArchiveBlocksInstallation(string defect)
    {
        using var fixture = new Fixture();
        fixture.Answer = (stage, value) =>
        {
            if (stage != "archive") return value;
            if (defect == "missing") return "{}";
            if (defect == "malformed") return "[" + Fixture.Unsafe;
            JsonObject answer = JsonNode.Parse(value)!.AsObject();
            switch (defect)
            {
                case "dependencies":
                    answer["runtime_dependencies"] = new JsonArray(new JsonObject
                    { ["name"] = "other", ["requirement"] = ">= 0" });
                    break;
                case "extensions": answer["extensions"] = new JsonArray("ext/extconf.rb"); break;
                case "missing-extensions": answer.Remove("extensions"); break;
                case "repository": answer["metadata"]!["github_repo"] = "https://other"; break;
                case "missing-repository": answer.Remove("metadata"); break;
                case "require-paths": answer["require_paths"] = new JsonArray("other"); break;
                case "missing-content": answer["contents"]!.AsArray().RemoveAt(0); break;
                case "extra-content": answer["contents"]!.AsArray().Add("extra.rb"); break;
                case "duplicate-content": answer["contents"]!.AsArray().Add("LICENSE"); break;
                case "witness":
                    answer["contents"]!.AsArray().Add(
                        "lib/hcoona_release_smoke_ruby/_workflow_delivery_provenance.json");
                    break;
                case "archive": answer["archive"] = Path.Combine(fixture.Root, "other.gem"); break;
                default: answer[defect] = "other"; break;
            }
            return answer.ToJsonString();
        };
        RubyRemoteConsumerResult result = await fixture.RunAsync(context.CancellationToken);
        RequireResult(fixture, result, ReleaseCheckStatus.Failed);
        CollectionAssert.AreEqual(SourceArchive, fixture.Stages.ToArray());
        Assert.HasCount(1, result.Commands);
        RequirePortable(result);
    }

    [TestMethod]
    [DataRow("archive", 2)]
    [DataRow("install", 3)]
    [DataRow("consume", 4)]
    public async Task ChangedOriginalBlocksFollowingConsumer(string after, int reached)
    {
        using var fixture = new Fixture();
        fixture.Answer = (stage, value) =>
        {
            if (stage == after) File.WriteAllBytes(fixture.Remote, Fixture.OtherBytes);
            return value;
        };
        RubyRemoteConsumerResult result = await fixture.RunAsync(context.CancellationToken);
        RequireResult(fixture, result, ReleaseCheckStatus.Failed);
        Assert.HasCount(reached, fixture.Stages);
        Assert.AreNotEqual(fixture.Prepared.Files[0].Sha256, Digest(fixture.Remote));
        Assert.AreEqual(fixture.Prepared.Files[0].Size, new FileInfo(fixture.Remote).Length);
        RequirePortable(result);
    }

    [TestMethod]
    [DataRow("install", "name", 3)]
    [DataRow("install", "version", 3)]
    [DataRow("install", "false", 3)]
    [DataRow("install", "missing", 3)]
    [DataRow("install", "malformed", 3)]
    [DataRow("consume", "name", 4)]
    [DataRow("consume", "version", 4)]
    [DataRow("consume", "false", 4)]
    [DataRow("consume", "missing", 4)]
    [DataRow("consume", "malformed", 4)]
    public async Task IncompleteConsumerBlocksSuccessAndFollowingChildren(
        string phase, string defect, int reached)
    {
        using var fixture = new Fixture();
        fixture.Answer = (stage, value) =>
        {
            if (stage != phase) return value;
            if (defect == "missing") return "{}";
            if (defect == "malformed") return Fixture.Unsafe;
            JsonObject answer = JsonNode.Parse(value)!.AsObject();
            if (defect == "false") answer["passed"] = false;
            else answer[defect] = "other";
            return answer.ToJsonString();
        };
        RubyRemoteConsumerResult result = await fixture.RunAsync(context.CancellationToken);
        RequireResult(fixture, result, ReleaseCheckStatus.Failed);
        Assert.HasCount(reached, fixture.Stages);
        RequirePortable(result);
    }

    public static IEnumerable<(string phase, string termination, int? exit,
        bool error)> TerminalCases =>
        from phase in StageOrder
        from failure in new (NativeTermination Termination, int? Exit, bool Error)[]
        {
            (NativeTermination.Exited, 17, false),
            (NativeTermination.Exited, 0, true),
            (NativeTermination.StartFailed, null, true),
            (NativeTermination.Cancelled, 137, true),
            (NativeTermination.TimedOut, 143, true),
            (NativeTermination.InvalidOutput, 0, true),
            (NativeTermination.InputFailed, 2, true),
            (NativeTermination.CleanupFailed, null, true),
        }
        select (phase, failure.Termination.ToString(), failure.Exit, failure.Error);

    [TestMethod]
    [DynamicData(nameof(TerminalCases))]
    public async Task NativeTerminationRemainsTruthfulAndPortable(
        string phase, string terminationName, int? exit, bool error)
    {
        NativeTermination termination = Enum.Parse<NativeTermination>(terminationName);
        using var fixture = new Fixture();
        fixture.Outcome = (stage, result) => stage == phase ? result with
        {
            Termination = termination,
            ExitCode = exit,
            Error = error ? Fixture.Unsafe : null,
            DurationSeconds = 12.375,
        } : result;
        RubyRemoteConsumerResult result = await fixture.RunAsync(context.CancellationToken);
        ReleaseCheckStatus expected = termination switch
        {
            NativeTermination.Cancelled => ReleaseCheckStatus.Cancelled,
            NativeTermination.TimedOut => ReleaseCheckStatus.TimedOut,
            _ => ReleaseCheckStatus.Failed,
        };
        RequireResult(fixture, result, expected);
        Assert.AreEqual(phase, fixture.Stages[^1]);
        NativeCommandResult actual = phase == "source"
            ? result.Read!.Command! : result.Commands[^1].Result;
        Assert.AreEqual(termination, actual.Termination);
        Assert.AreEqual(exit, actual.ExitCode);
        Assert.AreEqual(12.375, actual.DurationSeconds);
        Assert.AreEqual(error, actual.Error is not null);
        RequirePortable(result);
    }

    [TestMethod]
    [DataRow("archive")]
    [DataRow("install")]
    [DataRow("consume")]
    public async Task OversizedFactsCannotCertifyConsumer(string phase)
    {
        using var fixture = new Fixture();
        // NativeProcess rejects command output over 32 MiB. Simulate that actual terminal
        // boundary, rather than inventing another JSON ceiling for delegated archive facts.
        fixture.Outcome = (stage, result) => stage == phase ? result with
        {
            Termination = NativeTermination.InvalidOutput,
            Stdout = new string('x', 32 * 1024 * 1024),
            Error = "Native command output exceeded 33,554,432 characters." + Fixture.Unsafe,
        } : result;
        RubyRemoteConsumerResult result = await fixture.RunAsync(context.CancellationToken);
        RequireResult(fixture, result, ReleaseCheckStatus.Failed);
        Assert.AreEqual(phase, fixture.Stages[^1]);
        Assert.AreEqual(NativeTermination.InvalidOutput, result.Commands[^1].Result.Termination);
        RequirePortable(result);
    }

    [TestMethod]
    [DataRow("source")]
    [DataRow("archive")]
    [DataRow("install")]
    [DataRow("consume")]
    public async Task CallerCancellationStopsNextChild(string phase)
    {
        using var fixture = new Fixture();
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(
            context.CancellationToken);
        fixture.Outcome = (stage, result) =>
        {
            if (stage != phase) return result;
            caller.Cancel();
            return result with
            {
                Termination = NativeTermination.Cancelled,
                ExitCode = 137,
                DurationSeconds = 0.625,
                Error = Fixture.Unsafe
            };
        };
        RubyRemoteConsumerResult result = await fixture.RunAsync(caller.Token);
        RequireResult(fixture, result, ReleaseCheckStatus.Cancelled);
        Assert.AreEqual(phase, fixture.Stages[^1]);
        NativeCommandResult actual = phase == "source"
            ? result.Read!.Command! : result.Commands[^1].Result;
        Assert.AreEqual(NativeTermination.Cancelled, actual.Termination);
        Assert.AreEqual(137, actual.ExitCode);
        Assert.AreEqual(0.625, actual.DurationSeconds);
        RequirePortable(result);
    }

    [TestMethod]
    public async Task AlreadyCancelledStartsNoChild()
    {
        using var fixture = new Fixture();
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(
            context.CancellationToken);
        await caller.CancelAsync();
        RubyRemoteConsumerResult result = await fixture.RunAsync(caller.Token);
        RequireResult(fixture, result, ReleaseCheckStatus.Cancelled);
        Assert.IsEmpty(fixture.Commands);
        Assert.IsNull(result.Read);
    }

    [TestMethod]
    [DataRow("unit")]
    [DataRow("kind")]
    [DataRow("name")]
    [DataRow("destination")]
    [DataRow("channel")]
    [DataRow("relative-tools")]
    [DataRow("dirty-state")]
    [DataRow("missing-state")]
    public async Task UnsupportedPreparationNeverStartsNativeWork(string defect)
    {
        using var fixture = new Fixture();
        PreparedRelease prepared = fixture.Prepared;
        ReleaseFile file = prepared.Files[0];
        switch (defect)
        {
            case "unit":
                prepared = prepared with
                { Subject = prepared.Subject with { Unit = "other" } }; break;
            case "kind": prepared = prepared with { Files = [file with { Kind = "other" }] }; break;
            case "name": prepared = prepared with { Files = [file with { Name = "other" }] }; break;
            case "oversized":
                prepared = prepared with
                { Files = [file with { Size = 8 * 1024 * 1024 + 1 }] }; break;
            case "checks":
                prepared = prepared with
                { RequiredChecks = ["other"], Checks = [new("other", ReleaseCheckStatus.Passed)] };
                break;
            case "missing-check":
                prepared = prepared with { Checks = prepared.Checks[..^1] }; break;
            case "failed-check":
                prepared = prepared with
                {
                    Checks = prepared.Checks.Select(check =>
                check.Id == "contents" ? check with { Status = ReleaseCheckStatus.Failed } : check)
                .ToArray()
                }; break;
            case "destination":
                prepared = prepared with
                { Subject = prepared.Subject with { Destination = "rubygems" } }; break;
            case "channel":
                prepared = prepared with
                { Subject = prepared.Subject with { Channel = "other" } }; break;
            case "relative-tools": fixture.Tools = fixture.Tools with { Ruby = "ruby" }; break;
            case "dirty-state":
                File.WriteAllText(Path.Combine(fixture.Scratch, "dirty"), "x"); break;
            case "missing-state": Directory.Delete(fixture.Scratch); break;
        }
        fixture.Prepared = prepared;
        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => fixture.RunAsync(context.CancellationToken));
        Assert.IsEmpty(fixture.Commands);
    }

    [TestMethod]
    public async Task MissingReadCredentialBlocksConsumer()
    {
        using var fixture = new Fixture();
        foreach (RubyReadCredential? credential in new RubyReadCredential?[]
            { null, new("", "token"), new("user", " ") })
        {
            RubyRemoteConsumerResult result = await RubyRemoteConsumer.RunAsync(fixture.Prepared,
                fixture.Tools, credential, fixture.ExecuteAsync, context.CancellationToken);
            RequireResult(fixture, result, ReleaseCheckStatus.Failed);
            Assert.IsNotNull(result.Read);
            Assert.AreEqual(RubySourceState.Unknown, result.Read.State);
            Assert.IsNull(result.Read.Command);
            Assert.IsEmpty(fixture.Commands);
            Directory.Delete(Path.Combine(fixture.Scratch, "source"), true);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ExactObservationStillRequiresIndependentConsumer(bool dry)
    {
        using var fixture = new Fixture();
        fixture.Prepared = fixture.Prepared with
        { Subject = fixture.Prepared.Subject with { DryRun = dry } };
        RubyRemoteConsumerResult result = await fixture.RunAsync(context.CancellationToken);
        RequireResult(fixture, result, ReleaseCheckStatus.Passed);
        Assert.HasCount(4, fixture.Commands);
        ReleaseFile original = fixture.Prepared.Files[0];
        string[] required = ["observation", result.Check.Id];
        var observation = new ReleaseObservation(fixture.Prepared.Subject,
            [new(original.Output, original.Name, original.Version, ReleaseFileState.Exact,
                original.Size, original.Sha256)], required,
            [new("observation", ReleaseCheckStatus.Passed)]);
        foreach (ReleaseCheck? check in new ReleaseCheck?[]
            { null, result.Check with { Status = ReleaseCheckStatus.Failed }, result.Check })
        {
            ReleaseObservation current = observation with
            {
                Checks = check is null
                ? observation.Checks : [.. observation.Checks, check]
            };
            bool satisfied = dry ? ReleaseCompletion.Describe(fixture.Prepared, current, []).Ready
                : ReleaseCompletion.Evaluate(fixture.Prepared, current, []).State
                    == ReleaseBusinessState.Satisfied;
            Assert.AreEqual(check?.Status == ReleaseCheckStatus.Passed, satisfied);
        }
    }

    private static void RequireResult(Fixture fixture, RubyRemoteConsumerResult result,
        ReleaseCheckStatus status)
    {
        Assert.AreEqual(fixture.Prepared.Subject, result.Subject);
        Assert.AreEqual(fixture.Prepared.Files[0], result.Original);
        Assert.AreEqual("remote-gem-output-consumer", result.Check.Id);
        Assert.AreEqual(status, result.Check.Status);
        if (status != ReleaseCheckStatus.Passed) Assert.IsNotEmpty(result.Errors);
    }

    private static void RequirePortable(RubyRemoteConsumerResult result)
    {
        NativeCommandResult[] receipts = result.Read?.Command is { } read
            ? [read, .. result.Commands.Select(command => command.Result)]
            : result.Commands.Select(command => command.Result).ToArray();
        foreach (NativeCommandResult receipt in receipts)
        {
            Assert.AreEqual("", receipt.Stdout);
            Assert.AreEqual("", receipt.Stderr);
        }
        string portable = JsonSerializer.Serialize(result);
        foreach (string unsafeText in new[] { Fixture.Credential.Token, Fixture.Credential.User,
            Fixture.Unsafe, "RUBY_SOURCE_TOKEN", "RUBY_SOURCE_USER", "GEM_HOST_API_KEY",
            "\"Environment\"" })
            Assert.DoesNotContain(unsafeText, portable);
    }

    private static string Digest(string path) =>
        Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    private sealed class Fixture : IDisposable
    {
        internal const string Name = "hcoona-release-smoke-ruby";
        internal const string Version = "1.2.3.pre.beta.4";
        internal const string Unsafe = "UNSAFE-native-URI-output-and-error";
        internal static readonly RubyReadCredential Credential =
            new("controlled user:@/+", "controlled token:?@/+&=%");
        internal static readonly byte[] Bytes = Encoding.UTF8.GetBytes("owned original bytes");
        internal static readonly byte[] OtherBytes = Encoding.UTF8.GetBytes("other original bytes");
        internal static readonly string[] StateKeys =
            ["HOME", "USERPROFILE", "XDG_CONFIG_HOME", "XDG_CACHE_HOME", "XDG_DATA_HOME",
                "APPDATA", "LOCALAPPDATA", "GEM_HOME", "GEM_PATH", "GEM_SPEC_CACHE",
                "TMPDIR", "TMP", "TEMP"];
        private static readonly string[] Files = ["LICENSE", "README.md",
            "lib/hcoona_release_smoke_ruby.rb", "lib/hcoona_release_smoke_ruby/version.rb"];
        internal string Root { get; } = Path.Combine(Path.GetTempPath(),
            "ruby-remote-consumer-" + Guid.NewGuid());
        internal string Scratch { get; }
        internal string Remote => Path.Combine(Scratch, "source", "download", "original.gem");
        internal RubyRemoteConsumerTools Tools { get; set; }
        internal PreparedRelease Prepared { get; set; }
        internal List<NativeCommand> Commands { get; } = [];
        internal List<string> Stages { get; } = [];
        internal List<JsonObject> Requests { get; } = [];
        internal Func<string, string, string>? Answer { get; set; }
        internal Func<string, NativeCommandResult, NativeCommandResult>? Outcome { get; set; }
        internal Func<string, CancellationToken, Task>? Before { get; set; }

        internal Fixture(string channel = "buddy", string destination = "github-packages-ruby")
        {
            Scratch = Directory.CreateDirectory(Path.Combine(Root, "fresh state")).FullName;
            Tools = new(Path.Combine(Root, "tools", "ruby"),
                Path.Combine(Root, "tools", "facts.rb"), Path.Combine(Root, "tools", "source.rb"),
                Path.Combine(Root, "tools", "consumer.rb"), Scratch);
            string digest = Convert.ToHexStringLower(SHA256.HashData(Bytes));
            Prepared = new(new(new string('a', 40), "refs/heads/topic", Name,
                "product/workflow-delivery.release-unit.yml", channel, destination, false),
                [new("gem-build", "gem-output", "ruby-gem", Name, Version,
                    "never-substitute-local-original.gem", Bytes.Length, digest)],
                RubyPreparation.Required.ToArray(), RubyPreparation.Required.Select(id =>
                    new ReleaseCheck(id, ReleaseCheckStatus.Passed)).ToArray());
        }

        internal Task<RubyRemoteConsumerResult> RunAsync(CancellationToken token) =>
            RubyRemoteConsumer.RunAsync(Prepared, Tools, Credential, ExecuteAsync, token);

        internal async Task<NativeCommandResult> ExecuteAsync(NativeCommand command,
            CancellationToken token)
        {
            Assert.IsTrue(token.CanBeCanceled);
            Assert.AreEqual(Tools.Ruby, command.Executable);
            Assert.HasCount(2, command.Arguments);
            JsonObject request = JsonNode.Parse(await File.ReadAllTextAsync(
                command.Arguments[1], token))!.AsObject();
            string stage = command.Arguments[0] == Tools.SourceHelper ? "source"
                : request["operation"]!.GetValue<string>();
            Commands.Add(command);
            Stages.Add(stage);
            Requests.Add(request);
            if (Before is not null) await Before(stage, token);
            string answer;
            if (stage == "source")
            {
                foreach (string key in StateKeys)
                    Assert.IsEmpty(Directory.EnumerateFileSystemEntries(
                        command.Environment![key]!));
                await File.WriteAllBytesAsync(Remote, Bytes, token);
                answer = JsonSerializer.Serialize(new
                {
                    state = "present",
                    name = Name,
                    version = Version,
                    platform = "ruby",
                    path = Remote,
                    size = Bytes.Length,
                    sha256 = Digest(Remote)
                });
            }
            else if (stage == "archive")
            {
                foreach (string key in StateKeys)
                    Assert.IsEmpty(Directory.EnumerateFileSystemEntries(
                        command.Environment![key]!));
                answer = JsonSerializer.Serialize(new
                {
                    archive = Remote,
                    name = Name,
                    version = Version,
                    platform = "ruby",
                    require_paths = LibPaths,
                    files = Files,
                    contents = Files,
                    runtime_dependencies = Array.Empty<object>(),
                    extensions = Array.Empty<string>(),
                    metadata = new { github_repo = "https://github.com/hcoona/three" }
                });
            }
            else
            {
                Assert.Contains(stage, InstalledPhases);
                Assert.AreEqual(Prepared.Files[0].Sha256, Digest(Remote));
                answer = JsonSerializer.Serialize(new
                {
                    name = Name,
                    version = Version,
                    passed = true
                });
            }
            answer = Answer?.Invoke(stage, answer) ?? answer;
            var result = new NativeCommandResult(NativeTermination.Exited, 0, answer,
                Unsafe + Credential.Token, 0.125, null);
            return Outcome?.Invoke(stage, result) ?? result;
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
