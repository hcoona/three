using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WorkflowDelivery.Destinations;
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
    [DataRow("buddy", "github-packages-ruby", "hcoona-release-smoke-ruby", true, "Exact")]
    [DataRow("buddy", "github-packages-ruby", "hcoona-release-smoke-ruby", false, "Conflict")]
    [DataRow("official", "rubygems", "asciidoctor-latexmath", true, "Exact")]
    public async Task PresentNativeOriginalIsComparedByteForByte(string channel,
        string destination, string unit, bool matches, string state)
    {
        using var fixture = new Fixture(channel, destination, unit);
        byte[] bytes = matches ? Fixture.Original : Fixture.DifferentOriginal;
        ReleaseObservationResult result = await fixture.ObserveAsync(command =>
            Success(fixture.Present(command, bytes)), context.CancellationToken);
        AssertObservation(fixture.Prepared, result, Enum.Parse<ReleaseFileState>(state),
            matches ? ReleaseCheckStatus.Passed : ReleaseCheckStatus.Failed);
        RubySourceReadResult read = result.RubySource!;
        Assert.AreEqual(RubySourceState.Present, read.State);
        Assert.IsNotNull(read.Original);
        Assert.AreEqual((long)bytes.Length, read.Original.Size);
        Assert.AreEqual(Hash(bytes), result.Observation.Files[0].Sha256);
        AssertSanitized(result, Success(""));
    }

    [TestMethod]
    [DataRow("buddy", "github-packages-ruby")]
    [DataRow("official", "rubygems")]
    public async Task SupportedDestinationCanEstablishAbsence(string channel, string destination)
    {
        using var fixture = new Fixture(channel, destination);
        ReleaseObservationResult result = await fixture.ObserveAsync(_ =>
            Success("""{"state":"absent"}"""), context.CancellationToken);
        AssertObservation(fixture.Prepared, result, ReleaseFileState.Absent,
            ReleaseCheckStatus.Passed);
        Assert.AreEqual(RubySourceState.Absent, result.RubySource!.State);
        Assert.IsNull(result.RubySource!.Original);
        Assert.IsNull(result.Observation.Files[0].Sha256);
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
            ReleaseObservationResult result = await fixture.ObserveAsync(command =>
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
    [DataRow("{" + Unsafe + Secret)]
    [DataRow("{}")]
    [DataRow("""{"state":"present"}""")]
    [DataRow("""{"state":42}""")]
    [DataRow("""{"state":"unknown"}""")]
    [DataRow("null")]
    public async Task InvalidSourceAnswersRemainUnknown(string answer) =>
        await AssertUnreadableAsync(answer);

    [TestMethod]
    public async Task Utf8AnswerOverByteLimitRemainsUnknown() =>
        await AssertUnreadableAsync(JsonSerializer.Serialize(new
        {
            state = "absent",
            padding = new string('é', 140000),
        }, NativeJson));

    private async Task AssertUnreadableAsync(string answer)
    {
        using var fixture = new Fixture();
        NativeCommandResult reached = Success(answer);
        ReleaseObservationResult result = await fixture.ObserveAsync(_ => reached,
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
        ReleaseObservationResult result = await fixture.ObserveAsync(_ => Success(bounded),
            context.CancellationToken);
        AssertObservation(fixture.Prepared, result, ReleaseFileState.Absent,
            ReleaseCheckStatus.Passed);
        Assert.IsEmpty(result.RubySource!.Errors);
        AssertSanitized(result, Success(bounded));
    }

    [TestMethod]
    [DataRow("name", "\"unrelated-package\"")]
    [DataRow("version", "\"9.0.0\"")]
    [DataRow("platform", "\"x86_64-linux\"")]
    [DataRow("size", "1")]
    [DataRow("sha256", "\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"")]
    [DataRow("path", "\"download/original.gem\"")]
    public async Task InconsistentNativeEvidenceCannotCertifyExact(string field, string json) =>
        await AssertOriginalRejectedAsync((_, answer) => answer[field] = JsonNode.Parse(json));

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AbsoluteOriginalOutsideDownloadCannotCertifyExact(bool prefixSibling) =>
        await AssertOriginalRejectedAsync((fixture, answer) =>
        {
            string directory = prefixSibling ? Directory.CreateDirectory(Path.Combine(
                fixture.Tools.Scratch, "download-other")).FullName : fixture.Root;
            string path = Path.Combine(directory, "original.gem");
            File.WriteAllBytes(path, Fixture.Original);
            answer["path"] = path;
        });

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task OriginalMustExistWithAdvertisedBytes(bool missing) =>
        await AssertOriginalRejectedAsync((_, answer) =>
        {
            string path = answer["path"]!.GetValue<string>();
            if (missing) File.Delete(path);
            else File.WriteAllBytes(path, Fixture.DifferentOriginal);
        });

    [TestMethod]
    public async Task EmptyOriginalCannotCertifyExact() =>
        await AssertOriginalRejectedAsync((_, answer) =>
        {
            byte[] empty = [];
            File.WriteAllBytes(answer["path"]!.GetValue<string>(), empty);
            answer["size"] = 0;
            answer["sha256"] = Hash(empty);
        });

    private async Task AssertOriginalRejectedAsync(Action<Fixture, JsonObject> change)
    {
        using var fixture = new Fixture();
        ReleaseObservationResult result = await fixture.ObserveAsync(command =>
        {
            JsonObject answer = JsonNode.Parse(fixture.Present(command,
                Fixture.Original))!.AsObject();
            change(fixture, answer);
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
    [DataRow("Cancelled", -1, true)]
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
        ReleaseObservationResult result = await fixture.ObserveAsync(_ => reached,
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
        ReleaseObservationResult result = await fixture.ObserveAsync(_ =>
        {
            cancellation.Cancel();
            return reached;
        }, cancellation.Token);
        AssertUnknown(fixture, result, ReleaseCheckStatus.Failed);
        AssertSanitized(result, reached);
    }

    [TestMethod]
    [DataRow(null, null)]
    [DataRow("", Secret)]
    [DataRow(User, " ")]
    public async Task MissingBuddyCredentialDoesNotRead(string? user, string? token)
    {
        using var fixture = new Fixture();
        RubyReadCredential? credential = user is not null && token is not null
            ? new(user, token) : null;
        int calls = 0;
        ReleaseObservationResult result = await RubyDestinationObservation.ObserveAsync(
            fixture.Prepared,
            fixture.Tools, credential, (_, _) =>
            {
                calls++;
                return Task.FromResult(Success("{\"state\":\"absent\"}"));
            }, context.CancellationToken);
        Assert.AreEqual(0, calls);
        AssertUnknown(fixture, result, ReleaseCheckStatus.Failed);
        Assert.IsNull(result.RubySource!.Command);
        Assert.IsEmpty(Directory.EnumerateFileSystemEntries(fixture.Tools.Scratch));
        AssertNoSecrets(JsonSerializer.Serialize(result));
    }

    [TestMethod]
    [DataRow("buddy", "unknown", null, "ruby-gem")]
    [DataRow("unknown", "github-packages-ruby", null, "ruby-gem")]
    [DataRow("buddy", "rubygems", null, "ruby-gem")]
    [DataRow("buddy", "github-packages-ruby", "unrelated-package", "ruby-gem")]
    [DataRow("buddy", "github-packages-ruby", null, "python-wheel")]
    public async Task UnsupportedPreparationDoesNotRead(string channel, string destination,
        string? name, string kind)
    {
        using var fixture = new Fixture(channel, destination);
        PreparedRelease prepared = fixture.Prepared with
        {
            Files = [fixture.Prepared.Files[0] with { Name = name ?? "hcoona-release-smoke-ruby",
            Kind = kind }]
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
    [DataRow("https://unselected.invalid", "hcoona-release-smoke-ruby", "1.2.3")]
    [DataRow("https://rubygems.org", "another-package", "1.2.3")]
    [DataRow("https://rubygems.org", "hcoona-release-smoke-ruby", " ")]
    public async Task UnsupportedSourceCoordinateCannotRead(string host, string name,
        string version)
    {
        using var fixture = new Fixture();
        await AssertNoSourceReadAsync(fixture.Tools, host, name, version);
    }

    [TestMethod]
    [DataRow("ruby", null, null)]
    [DataRow(null, "source.rb", null)]
    [DataRow(null, null, "scratch")]
    public async Task SourceToolsRequireAbsolutePaths(string? ruby, string? helper, string? scratch)
    {
        using var fixture = new Fixture();
        await AssertNoSourceReadAsync(new(ruby ?? fixture.Tools.Ruby,
            helper ?? fixture.Tools.Helper, scratch ?? fixture.Tools.Scratch));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SourceRequiresExistingEmptyScratch(bool missing)
    {
        using var fixture = new Fixture();
        if (missing) Directory.Delete(fixture.Tools.Scratch);
        else File.WriteAllText(Path.Combine(fixture.Tools.Scratch, "stale"), "stale");
        await AssertNoSourceReadAsync(fixture.Tools);
        Assert.IsFalse(File.Exists(Path.Combine(fixture.Tools.Scratch, "request.json")));
    }

    private async Task AssertNoSourceReadAsync(RubySourceTools tools,
        string host = "https://rubygems.org", string name = "hcoona-release-smoke-ruby",
        string version = "1.2.3") =>
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => RubySourceReader.ReadAsync(
            host, name, version, tools, null, (_, _) => throw new AssertFailedException(
                "Unsafe read request reached the native executor."), context.CancellationToken));

    [TestMethod]
    public async Task ExactObservationCannotCompleteWithMissingRequiredCheck()
    {
        using var fixture = new Fixture();
        ReleaseObservationResult result = await fixture.ObserveAsync(command =>
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

    private static void AssertObservation(PreparedRelease prepared, ReleaseObservationResult result,
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

    private static void AssertUnknown(Fixture fixture, ReleaseObservationResult result,
        ReleaseCheckStatus status)
    {
        AssertObservation(fixture.Prepared, result, ReleaseFileState.Unknown, status);
        Assert.AreEqual(RubySourceState.Unknown, result.RubySource!.State);
        Assert.IsNull(result.RubySource!.Original);
        Assert.IsNull(result.Observation.Files[0].Size);
        Assert.IsNull(result.Observation.Files[0].Sha256);
        Assert.IsNotEmpty(result.RubySource!.Errors);
    }

    private static void AssertSanitized(ReleaseObservationResult result,
        NativeCommandResult reached)
    {
        NativeCommandResult? command = result.RubySource!.Command;
        Assert.IsNotNull(command);
        Assert.AreEqual(reached.Termination, command.Termination);
        Assert.AreEqual(reached.ExitCode, command.ExitCode);
        Assert.AreEqual(reached.DurationSeconds, command.DurationSeconds);
        Assert.AreEqual("", command.Stdout);
        Assert.AreEqual("", command.Stderr);
        Assert.AreEqual(reached.Error is null ? null : "Native Ruby source process failed.",
            command.Error);
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

        internal Fixture(string channel = "buddy", string destination = "github-packages-ruby",
            string unit = "hcoona-release-smoke-ruby")
        {
            Directory.CreateDirectory(Root);
            Tools = new(Path.Combine(Root, "tools", "ruby"), Path.Combine(Root, "tools",
                "source.rb"),
                Directory.CreateDirectory(Path.Combine(Root, "read")).FullName);
            Prepared = new(new(new string('a', 40), "refs/heads/development", unit,
                "fixtures/ruby.json", channel, destination, false),
                [new("gem-build", "gem-original", "ruby-gem", unit,
                    "1.2.3.pre.beta.7", "original/fixture.gem", Original.Length, Hash(Original))],
                ["qualified-original"], [new("qualified-original", ReleaseCheckStatus.Passed)]);
        }

        internal Task<ReleaseObservationResult> ObserveAsync(Func<NativeCommand,
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
