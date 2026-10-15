using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WorkflowDelivery.Destinations;
using WorkflowDelivery.Ecosystems;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Release;

namespace WorkflowDelivery.Tests.Release;

[TestClass]
public sealed class RubyDestinationObservationTests(TestContext context)
{
    private const string User = "read user:@/+?%";
    private const string Secret = "synthetic-token:@/+?%&= punctuation";
    private const string Unsafe = "unsafe-native-detail";
    private static readonly RubyReadCredential Credential = new(User, Secret);
    private static readonly string[] RequestFields = ["host", "name", "version", "directory"];
    private static readonly string[] CredentialVariables = ["RUBY_SOURCE_USER",
        "RUBY_SOURCE_TOKEN"];
    private static readonly JsonSerializerOptions NativeJson = new()
    { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static readonly string[] StateVariables = ["HOME", "USERPROFILE", "XDG_CONFIG_HOME",
        "XDG_CACHE_HOME", "XDG_DATA_HOME", "APPDATA", "LOCALAPPDATA", "GEM_HOME", "GEM_PATH",
        "GEM_SPEC_CACHE", "TMPDIR", "TMP", "TEMP"];

    [TestMethod]
    [DataRow("buddy", "github-packages-ruby", "exact")]
    [DataRow("buddy", "github-packages-ruby", "absent")]
    [DataRow("buddy", "github-packages-ruby", "different")]
    [DataRow("buddy", "github-packages-ruby", "unknown")]
    [DataRow("official", "rubygems", "exact")]
    [DataRow("official", "rubygems", "absent")]
    [DataRow("official", "rubygems", "different")]
    [DataRow("official", "rubygems", "unknown")]
    public async Task SupportedDestinationsBindOriginalAndObservation(string channel,
        string destination, string scenario)
    {
        using var fixture = new Fixture(channel, destination);
        RubyObservationResult result = await fixture.ObserveAsync(command => Success(scenario switch
        {
            "exact" => fixture.Present(command, Fixture.Original),
            "different" => fixture.Present(command, Fixture.DifferentOriginal),
            _ => JsonSerializer.Serialize(new { state = scenario }),
        }), context.CancellationToken);

        ReleaseFileState expected = scenario switch
        {
            "exact" => ReleaseFileState.Exact,
            "absent" => ReleaseFileState.Absent,
            "different" => ReleaseFileState.Conflict,
            _ => ReleaseFileState.Unknown,
        };
        AssertObservation(fixture.Prepared, result, expected,
            scenario is "exact" or "absent" ? ReleaseCheckStatus.Passed :
                ReleaseCheckStatus.Failed);
        Assert.AreEqual(scenario switch
        {
            "exact" or "different" => RubySourceState.Present,
            "absent" => RubySourceState.Absent,
            _ => RubySourceState.Unknown,
        }, result.Read.State);
        if (scenario is "exact" or "different")
        {
            RubySourceOriginal original = result.Read.Original!;
            Assert.IsNotNull(original);
            byte[] actual = File.ReadAllBytes(original.Path);
            CollectionAssert.AreEqual(scenario == "exact" ? Fixture.Original :
                Fixture.DifferentOriginal, actual);
            Assert.AreEqual((long)actual.Length, original.Size);
            Assert.AreEqual(Hash(actual), original.Sha256);
            Assert.AreEqual("ruby", original.Platform);
            Assert.AreEqual(fixture.Prepared.Files[0].Version, original.Version);
            Assert.AreEqual(fixture.Prepared.Files[0].Name, original.Name);
            Assert.AreEqual(original.Size, result.Observation.Files[0].Size);
            Assert.AreEqual(original.Sha256, result.Observation.Files[0].Sha256);
            Assert.IsEmpty(result.Read.Errors);
        }
        else
        {
            Assert.IsNull(result.Read.Original);
            Assert.IsNull(result.Observation.Files[0].Size);
            Assert.IsNull(result.Observation.Files[0].Sha256);
        }
        AssertSanitized(result, Success(""));
    }

    [TestMethod]
    [DataRow("buddy", "github-packages-ruby", "https://rubygems.pkg.github.com/hcoona")]
    [DataRow("official", "rubygems", "https://rubygems.org")]
    public async Task ReadCommandOwnsFreshStateAndKeepsCredentialsInEnvironment(string channel,
        string destination, string host)
    {
        var previousPaths = new HashSet<string>(StringComparer.Ordinal);
        for (int read = 0; read < 2; read++)
        {
            using var fixture = new Fixture(channel, destination);
            int calls = 0;
            RubyObservationResult result = await fixture.ObserveAsync(command =>
            {
                calls++;
                Assert.AreEqual(fixture.Tools.Ruby, command.Executable);
                Assert.AreEqual(fixture.Tools.Scratch, command.Directory);
                Assert.AreEqual(90, command.DeadlineSeconds);
                Assert.IsFalse(command.InheritEnvironment);
                Assert.IsTrue(command.CloseStandardInput);
                CollectionAssert.AreEqual(new[] { fixture.Tools.Helper,
                    Path.Combine(fixture.Tools.Scratch, "request.json") }, command.Arguments);
                JsonObject request = Request(command);
                CollectionAssert.AreEquivalent(RequestFields,
                    request.Select(pair => pair.Key).ToArray());
                Assert.AreEqual(host, request["host"]!.GetValue<string>());
                Assert.AreEqual(fixture.Prepared.Files[0].Name,
                    request["name"]!.GetValue<string>());
                Assert.AreEqual(fixture.Prepared.Files[0].Version,
                    request["version"]!.GetValue<string>());
                string download = request["directory"]!.GetValue<string>();
                Assert.AreEqual(Path.Combine(fixture.Tools.Scratch, "download"), download);
                Assert.IsEmpty(Directory.EnumerateFileSystemEntries(download));
                Assert.IsNotNull(command.Environment);
                foreach (string variable in StateVariables)
                {
                    string path = command.Environment[variable]!;
                    Assert.StartsWith(fixture.Tools.Scratch + Path.DirectorySeparatorChar, path);
                    Assert.IsTrue(Directory.Exists(path));
                    Assert.IsEmpty(Directory.EnumerateFileSystemEntries(path));
                    Assert.IsTrue(previousPaths.Add(path),
                        "Each read must own fresh distinct state.");
                }
                string[] allowed = [.. StateVariables, "SystemRoot", "WINDIR", "PATHEXT", "PATH",
                    "LANG", "LC_ALL", .. channel == "buddy" ?
                        CredentialVariables : Array.Empty<string>()];
                CollectionAssert.AreEquivalent(allowed, command.Environment.Keys.ToArray());
                Assert.AreEqual(string.Join(Path.PathSeparator, Path.GetDirectoryName(
                    fixture.Tools.Ruby),
                    "/usr/bin", "/bin"), command.Environment["PATH"]);
                Assert.AreEqual("C.UTF-8", command.Environment["LANG"]);
                Assert.AreEqual("C.UTF-8", command.Environment["LC_ALL"]);
                if (channel == "buddy")
                {
                    Assert.AreEqual(User, command.Environment["RUBY_SOURCE_USER"]);
                    Assert.AreEqual(Secret, command.Environment["RUBY_SOURCE_TOKEN"]);
                }
                else
                {
                    Assert.DoesNotContain("RUBY_SOURCE_USER", command.Environment.Keys);
                    Assert.DoesNotContain("RUBY_SOURCE_TOKEN", command.Environment.Keys);
                    Assert.DoesNotContain(Secret, command.Environment.Values);
                }
                AssertNoSecrets(string.Join(" ", command.Arguments));
                AssertNoSecrets(File.ReadAllText(command.Arguments[1]));
                return Success(fixture.Present(command, Fixture.Original));
            }, context.CancellationToken);
            Assert.AreEqual(1, calls);
            AssertObservation(fixture.Prepared, result, ReleaseFileState.Exact,
                ReleaseCheckStatus.Passed);
            AssertNoSecrets(JsonSerializer.Serialize(result));
            foreach (string file in Directory.EnumerateFiles(fixture.Tools.Scratch, "*",
                SearchOption.AllDirectories)) AssertNoSecrets(File.ReadAllText(file));
        }
    }

    [TestMethod]
    [DataRow("malformed")]
    [DataRow("missing-state")]
    [DataRow("missing-original")]
    [DataRow("wrong-type")]
    [DataRow("unknown")]
    [DataRow("null")]
    [DataRow("utf8-oversized")]
    public async Task InvalidSourceAnswersRemainUnknown(string scenario)
    {
        using var fixture = new Fixture();
        string answer = scenario switch
        {
            "malformed" => "{" + Unsafe + Secret,
            "missing-state" => "{}",
            "missing-original" => "{\"state\":\"present\"}",
            "wrong-type" => "{\"state\":42}",
            "unknown" => "{\"state\":\"unknown\"}",
            "null" => "null",
            "oversized" => "{\"state\":\"absent\"}" + new string(' ', 256 * 1024),
            _ => JsonSerializer.Serialize(new { state = "absent", padding = new string('é',
                140000) },
                NativeJson),
        };
        NativeCommandResult reached = Success(answer);
        RubyObservationResult result = await fixture.ObserveAsync(_ => reached,
            context.CancellationToken);
        AssertUnknown(fixture, result, ReleaseCheckStatus.Failed);
        AssertSanitized(result, reached);
    }

    [TestMethod]
    public async Task SourceAnswerAtResultLimitCanEstablishAbsence()
    {
        using var fixture = new Fixture();
        const string answer = "{\"state\":\"absent\"}";
        string bounded = answer + new string(' ', 256 * 1024 - Encoding.UTF8.GetByteCount(answer));
        RubyObservationResult result = await fixture.ObserveAsync(_ => Success(bounded),
            context.CancellationToken);
        AssertObservation(fixture.Prepared, result, ReleaseFileState.Absent,
            ReleaseCheckStatus.Passed);
        Assert.IsEmpty(result.Read.Errors);
        AssertSanitized(result, Success(bounded));
    }

    [TestMethod]
    public async Task NativeOriginalAtSizeLimitCanBeExact()
    {
        using var fixture = new Fixture("official", "rubygems");
        byte[] bytes = Enumerable.Repeat((byte)'x', 8 * 1024 * 1024).ToArray();
        PreparedRelease prepared = fixture.Prepared with
        { Files = [fixture.Prepared.Files[0] with { Size = bytes.LongLength, Sha256 = Hash(
            bytes) }] };
        RubyObservationResult result = await RubyDestinationObservation.ObserveAsync(prepared,
            fixture.Tools, null, (command, _) =>
                Task.FromResult(Success(fixture.Present(command, bytes))),
                    context.CancellationToken);
        AssertObservation(prepared, result, ReleaseFileState.Exact, ReleaseCheckStatus.Passed);
        Assert.AreEqual(bytes.LongLength, result.Read.Original!.Size);
        Assert.AreEqual(Hash(bytes), result.Read.Original.Sha256);
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(result.Read.Original.Path));
        AssertSanitized(result, Success(""));
    }

    [TestMethod]
    [DataRow("outside")]
    [DataRow("prefix-sibling")]
    [DataRow("missing")]
    [DataRow("changed-bytes")]
    [DataRow("name")]
    [DataRow("version")]
    [DataRow("platform")]
    [DataRow("size")]
    [DataRow("digest")]
    [DataRow("relative")]
    [DataRow("empty")]
    [DataRow("oversized")]
    public async Task UnownedOrInconsistentOriginalCannotCertifyExact(string scenario)
    {
        using var fixture = new Fixture();
        RubyObservationResult result = await fixture.ObserveAsync(command =>
        {
            JsonObject answer = JsonNode.Parse(fixture.Present(command,
                Fixture.Original))!.AsObject();
            string path = answer["path"]!.GetValue<string>();
            switch (scenario)
            {
                case "outside":
                case "prefix-sibling":
                    string outside = scenario == "outside" ? Path.Combine(fixture.Root,
                        "outside.gem") :
                        Path.Combine(Directory.CreateDirectory(Path.Combine(fixture.Tools.Scratch,
                            "download-other")).FullName, "original.gem");
                    File.WriteAllBytes(outside, Fixture.Original);
                    answer["path"] = outside;
                    break;
                case "missing": File.Delete(path); break;
                case "changed-bytes":
                    // Same length and supplied expected hash cannot certify unrelated file bytes.
                    File.WriteAllBytes(path, Enumerable.Repeat((byte)'x',
                        Fixture.Original.Length).ToArray());
                    break;
                case "name": answer["name"] = "unrelated-package"; break;
                case "version": answer["version"] = "9.0.0"; break;
                case "platform": answer["platform"] = "x86_64-linux"; break;
                case "size": answer["size"] = Fixture.Original.Length + 1; break;
                case "digest": answer["sha256"] = new string('a', 64); break;
                case "relative": answer["path"] = "download/original.gem"; break;
                case "empty":
                case "oversized":
                    byte[] bytes = new byte[scenario == "empty" ? 0 : 8 * 1024 * 1024 + 1];
                    File.WriteAllBytes(path, bytes);
                    answer["size"] = bytes.Length;
                    answer["sha256"] = Hash(bytes);
                    break;
            }
            return Success(answer.ToJsonString());
        }, context.CancellationToken);
        AssertUnknown(fixture, result, ReleaseCheckStatus.Failed);
        AssertSanitized(result, Success(""));
    }

    [TestMethod]
    [DataRow("Exited", 7, false)]
    [DataRow("Exited", 0, true)]
    [DataRow("StartFailed", -1, true)]
    [DataRow("TimedOut", -1, true)]
    [DataRow("TimedOut", 0, false)]
    [DataRow("Cancelled", -1, true)]
    [DataRow("Cancelled", 0, false)]
    [DataRow("InvalidOutput", 0, true)]
    [DataRow("CleanupFailed", 0, true)]
    [DataRow("InputFailed", -1, true)]
    public async Task NativeAbnormalitiesPreserveProcessFactsWithoutUnsafeDiagnostics(
        string termination, int code, bool hasError)
    {
        using var fixture = new Fixture();
        var reached = new NativeCommandResult(Enum.Parse<NativeTermination>(termination),
            code < 0 ? null : code, "{\"state\":\"absent\"}", Unsafe + User + Secret,
            73.125, hasError ? Unsafe + User + Secret : null);
        RubyObservationResult result = await fixture.ObserveAsync(_ => reached,
            context.CancellationToken);
        AssertUnknown(fixture, result, termination switch
        {
            "TimedOut" => ReleaseCheckStatus.TimedOut,
            "Cancelled" => ReleaseCheckStatus.Cancelled,
            _ => ReleaseCheckStatus.Failed,
        });
        AssertSanitized(result, reached);
    }

    [TestMethod]
    public async Task CancellationAfterNativeSuccessCannotBecomeAbsent()
    {
        using var fixture = new Fixture();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            context.CancellationToken);
        NativeCommandResult reached = Success("{\"state\":\"absent\"}");
        RubyObservationResult result = await fixture.ObserveAsync(_ =>
        {
            cancellation.Cancel();
            return reached;
        }, cancellation.Token);
        AssertUnknown(fixture, result, ReleaseCheckStatus.Failed);
        AssertSanitized(result, reached);
    }

    [TestMethod]
    [DataRow("null")]
    [DataRow("empty-user")]
    [DataRow("empty-token")]
    [DataRow("blank-user")]
    [DataRow("blank-token")]
    public async Task MissingBuddyCredentialDoesNotRead(string scenario)
    {
        using var fixture = new Fixture();
        RubyReadCredential? credential = scenario switch
        {
            "null" => null,
            "empty-user" => Credential with { User = "" },
            "empty-token" => Credential with { Token = "" },
            "blank-user" => Credential with { User = " " },
            _ => Credential with { Token = " " },
        };
        int calls = 0;
        RubyObservationResult result = await RubyDestinationObservation.ObserveAsync(
            fixture.Prepared,
            fixture.Tools, credential, (_, _) =>
            {
                calls++;
                return Task.FromResult(Success("{\"state\":\"absent\"}"));
            }, context.CancellationToken);
        Assert.AreEqual(0, calls);
        AssertUnknown(fixture, result, ReleaseCheckStatus.Failed);
        Assert.IsNull(result.Read.Command);
        Assert.IsEmpty(Directory.EnumerateFileSystemEntries(fixture.Tools.Scratch));
        AssertNoSecrets(JsonSerializer.Serialize(result));
    }

    [TestMethod]
    [DataRow("destination")]
    [DataRow("channel")]
    [DataRow("crossed-destination")]
    [DataRow("name")]
    [DataRow("kind")]
    public async Task UnsupportedPreparationDoesNotRead(string scenario)
    {
        using var fixture = new Fixture();
        PreparedRelease prepared = fixture.Prepared;
        prepared = scenario switch
        {
            "destination" => prepared with { Subject = prepared.Subject with { Destination =
                "unknown" } },
            "channel" => prepared with { Subject = prepared.Subject with { Channel = "unknown" } },
            "crossed-destination" => prepared with { Subject =
                prepared.Subject with { Destination = "rubygems" } },
            "name" => prepared with { Files = [prepared.Files[0] with { Name =
                "unrelated-package" }] },
            _ => prepared with { Files = [prepared.Files[0] with { Kind = "python-wheel" }] },
        };
        int calls = 0;
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            RubyDestinationObservation.ObserveAsync(prepared, fixture.Tools, Credential, (_, _) =>
            {
                calls++;
                return Task.FromResult(Success("{\"state\":\"absent\"}"));
            }, context.CancellationToken));
        Assert.AreEqual(0, calls);
        Assert.IsEmpty(Directory.EnumerateFileSystemEntries(fixture.Tools.Scratch));
    }

    [TestMethod]
    [DataRow("host")]
    [DataRow("blank-version")]
    [DataRow("relative-ruby")]
    [DataRow("relative-helper")]
    [DataRow("relative-state")]
    [DataRow("missing-state")]
    [DataRow("used-state")]
    public async Task SourceRequiresSupportedHostAndFreshAbsoluteState(string scenario)
    {
        using var fixture = new Fixture();
        RubySourceTools tools = scenario switch
        {
            "relative-ruby" => fixture.Tools with { Ruby = "ruby" },
            "relative-helper" => fixture.Tools with { Helper = "source.rb" },
            "relative-state" => fixture.Tools with { Scratch = "scratch" },
            "missing-state" => fixture.Tools with { Scratch = Path.Combine(fixture.Root,
                "missing") },
            _ => fixture.Tools,
        };
        if (scenario == "used-state") File.WriteAllText(Path.Combine(tools.Scratch, "stale"),
            "stale");
        int calls = 0;
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => RubySourceReader.ReadAsync(
            scenario == "host" ? "https://unselected.invalid" : "https://rubygems.org",
            scenario == "blank-version" ? " " : fixture.Prepared.Files[0].Version,
            tools, null, (_, _) =>
            {
                calls++;
                return Task.FromResult(Success("{\"state\":\"absent\"}"));
            }, context.CancellationToken));
        Assert.AreEqual(0, calls);
        Assert.IsFalse(File.Exists(Path.Combine(tools.Scratch, "request.json")));
    }

    [TestMethod]
    public async Task ExactObservationCannotCompleteWithMissingRequiredCheck()
    {
        using var fixture = new Fixture();
        RubyObservationResult result = await fixture.ObserveAsync(command =>
            Success(fixture.Present(command, Fixture.Original)), context.CancellationToken);
        AssertObservation(fixture.Prepared, result, ReleaseFileState.Exact,
            ReleaseCheckStatus.Passed);
        ReleaseObservation awaitingCheck = result.Observation with
        { RequiredChecks = [.. result.Observation.RequiredChecks, "required-additional-check"] };
        ReleaseOutcome outcome = ReleaseCompletion.Evaluate(fixture.Prepared, awaitingCheck, []);
        Assert.AreEqual(ReleaseBusinessState.Unsatisfied, outcome.State);
        Assert.Contains("Required check is not passed: required-additional-check.",
            outcome.Reasons);
        Assert.IsEmpty(outcome.Uploads);
        Assert.AreEqual(fixture.Prepared.Subject, outcome.Subject);
    }

    private static void AssertObservation(PreparedRelease prepared, RubyObservationResult result,
        ReleaseFileState state, ReleaseCheckStatus status)
    {
        Assert.AreEqual(prepared.Subject, result.Observation.Subject);
        ReleaseFileObservation file = Assert.ContainsSingle(result.Observation.Files);
        Assert.AreEqual(prepared.Files[0].Output, file.Output);
        Assert.AreEqual(prepared.Files[0].Name, file.Name);
        Assert.AreEqual(prepared.Files[0].Version, file.Version);
        Assert.AreEqual(state, file.State);
        Assert.AreEqual("native-source-original", Assert.ContainsSingle(
            result.Observation.RequiredChecks));
        Assert.AreEqual(new ReleaseCheck("native-source-original", status),
            Assert.ContainsSingle(result.Observation.Checks));
    }

    private static void AssertUnknown(Fixture fixture, RubyObservationResult result,
        ReleaseCheckStatus status)
    {
        AssertObservation(fixture.Prepared, result, ReleaseFileState.Unknown, status);
        Assert.AreEqual(RubySourceState.Unknown, result.Read.State);
        Assert.IsNull(result.Read.Original);
        Assert.IsNull(result.Observation.Files[0].Size);
        Assert.IsNull(result.Observation.Files[0].Sha256);
        Assert.IsNotEmpty(result.Read.Errors);
    }

    private static void AssertSanitized(RubyObservationResult result, NativeCommandResult reached)
    {
        Assert.IsNotNull(result.Read.Command);
        Assert.AreEqual(reached.Termination, result.Read.Command.Termination);
        Assert.AreEqual(reached.ExitCode, result.Read.Command.ExitCode);
        Assert.AreEqual(reached.DurationSeconds, result.Read.Command.DurationSeconds);
        Assert.AreEqual("", result.Read.Command.Stdout);
        Assert.AreEqual("", result.Read.Command.Stderr);
        Assert.AreEqual(reached.Error is null ? null : "Native Ruby source process failed.",
            result.Read.Command.Error);
        string persisted = JsonSerializer.Serialize(result);
        Assert.DoesNotContain(Unsafe, persisted);
        AssertNoSecrets(persisted);
    }

    private static void AssertNoSecrets(string value)
    {
        Assert.DoesNotContain(User, value);
        Assert.DoesNotContain(Secret, value);
    }

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static JsonObject Request(NativeCommand command) =>
        JsonNode.Parse(File.ReadAllText(command.Arguments[1]))!.AsObject();
    private static NativeCommandResult Success(string answer) =>
        new(NativeTermination.Exited, 0, answer, Unsafe + User + Secret, 1.125, null);

    private sealed class Fixture : IDisposable
    {
        internal static readonly byte[] Original = "owned native original"u8.ToArray();
        internal static readonly byte[] DifferentOriginal = Enumerable.Repeat((byte)'x',
            Original.Length).ToArray();
        internal string Root { get; } = Path.Combine(Path.GetTempPath(),
            "ruby-observation-" + Guid.NewGuid().ToString("N"));
        internal RubySourceTools Tools { get; }
        internal PreparedRelease Prepared { get; }

        internal Fixture(string channel = "buddy", string destination = "github-packages-ruby")
        {
            Directory.CreateDirectory(Root);
            Tools = new(Path.Combine(Root, "tools", "ruby"), Path.Combine(Root, "tools",
                "source.rb"),
                Directory.CreateDirectory(Path.Combine(Root, "read")).FullName);
            Prepared = new(new(new string('a', 40), "refs/heads/development", "ruby-fixture",
                "fixtures/ruby.json", channel, destination, false),
                [new("gem-build", "gem-original", "ruby-gem", "hcoona-release-smoke-ruby",
                    "1.2.3.pre.beta.7", "original/fixture.gem", Original.Length, Hash(Original))],
                ["qualified-original"], [new("qualified-original", ReleaseCheckStatus.Passed)]);
        }

        internal Task<RubyObservationResult> ObserveAsync(Func<NativeCommand,
            NativeCommandResult> execute,
            CancellationToken token) => RubyDestinationObservation.ObserveAsync(Prepared, Tools,
                Credential,
                (command, reachedToken) =>
                {
                    Assert.AreEqual(token, reachedToken);
                    return Task.FromResult(execute(command));
                }, token);

        internal string Present(NativeCommand command, byte[] bytes)
        {
            Assert.AreEqual(Tools.Scratch, command.Directory);
            JsonObject request = Request(command);
            string path = Path.Combine(request["directory"]!.GetValue<string>(),
                "native-original.gem");
            File.WriteAllBytes(path, bytes);
            return JsonSerializer.Serialize(new
            {
                state = "present",
                path,
                name = request["name"]!.GetValue<string>(),
                version = request["version"]!.GetValue<string>(),
                platform = "ruby",
                size = bytes.LongLength,
                sha256 = Hash(bytes)
            });
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
