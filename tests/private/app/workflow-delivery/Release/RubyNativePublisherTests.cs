using System.Collections;
using System.Security.Cryptography;
using System.Text.Json;
using WorkflowDelivery.Ecosystems;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Release;

namespace WorkflowDelivery.Tests.Release;

[TestClass]
public sealed class RubyNativePublisherTests(TestContext context)
{
    private const string RepositoryKey = "controlled-repository-write-key";
    private const string TrustedKey = "controlled-action-exported-trusted-key";
    private const string AmbientKey = "controlled-unselected-ambient-key";
    private const string Uncertainty =
        "Native Ruby publisher process failed; effects remain uncertain.";
    private static readonly string[] WindowsVariables = ["SystemRoot", "WINDIR", "PATHEXT"];
    private static readonly string[] OtherAllowedVariables =
    [
        "SystemRoot",
        "WINDIR",
        "PATHEXT",
        "PATH",
        "LANG",
        "LC_ALL",
        "GEM_HOST_API_KEY",
    ];
    private static readonly string[] ResultFields = ["Subject", "Upload"];
    private static readonly string[] RequiredChecks =
    [
        "native-input",
        "build",
        "contents",
        "consumer",
        "original-bytes",
    ];
    private static readonly string[] StateVariables =
    [
        "HOME",
        "USERPROFILE",
        "XDG_CONFIG_HOME",
        "XDG_CACHE_HOME",
        "XDG_DATA_HOME",
        "APPDATA",
        "LOCALAPPDATA",
        "GEM_HOME",
        "GEM_PATH",
        "GEM_SPEC_CACHE",
        "TMPDIR",
        "TMP",
        "TEMP",
    ];
    private static readonly string[] ExcludedVariables =
    [
        "GITHUB_TOKEN",
        "GH_TOKEN",
        "HTTP_PROXY",
        "HTTPS_PROXY",
        "ALL_PROXY",
        "NO_PROXY",
        "http_proxy",
        "https_proxy",
        "all_proxy",
        "no_proxy",
        "BUNDLE_GEMS__RUBYGEMS__ORG",
        "BUNDLE_RUBYGEMS__PKG__GITHUB__COM",
        "BUNDLE_USER_CONFIG",
        "BUNDLE_PATH",
        "BUNDLE_GEMFILE",
        "RUBYOPT",
        "RUBYLIB",
        "RUBYGEMS_API_KEY",
        "GEM_API_KEY",
        "RUBYGEMS_HOST",
        "GEM_HOST",
        "GEM_CREDENTIALS",
        "RUBYGEMS_EMAIL",
        "RUBYGEMS_PASSWORD",
        "GEM_USERNAME",
        "GEM_PASSWORD",
        "SOURCE_DATE_EPOCH",
        "ACTIONS_ID_TOKEN_REQUEST_TOKEN",
        "ACTIONS_ID_TOKEN_REQUEST_URL",
    ];

    [TestMethod]
    [DoNotParallelize]
    [DataRow(
        "buddy",
        "github-packages-ruby",
        "refs/heads/development",
        "false",
        "https://rubygems.pkg.github.com/hcoona",
        "Bearer " + RepositoryKey
    )]
    [DataRow("official", "rubygems", "refs/heads/main", "true", "https://rubygems.org", TrustedKey)]
    public async Task NativePushUsesQualifiedOriginalSelectedKeyAndClosedState(
        string channel,
        string destination,
        string selectedRef,
        string publicRelease,
        string host,
        string expectedKey
    )
    {
        using var fixture = new Fixture();
        RubyPreparationResult preparation = fixture.ForChannel(channel, destination, selectedRef);
        preparation.Native["public-release"] = publicRelease;
        var previous = ExcludedVariables
            .Append("GEM_HOST_API_KEY")
            .ToDictionary(name => name, Environment.GetEnvironmentVariable, StringComparer.Ordinal);
        try
        {
            foreach (string name in previous.Keys)
                Environment.SetEnvironmentVariable(name, AmbientKey);
            int calls = 0;
            RubyPublishResult result = await RubyNativePublisher.PublishAsync(
                preparation,
                fixture.Originals,
                fixture.Tools,
                fixture.Platform,
                (command, token) =>
                {
                    calls++;
                    Assert.AreEqual(context.CancellationToken, token);
                    Assert.AreEqual(fixture.Tools.Ruby, command.Executable);
                    Assert.AreEqual(fixture.Tools.Scratch, command.Directory);
                    Assert.AreEqual(180, command.DeadlineSeconds);
                    Assert.IsFalse(command.InheritEnvironment);
                    Assert.IsTrue(command.CloseStandardInput);
                    CollectionAssert.AreEqual(
                        new[]
                        {
                            fixture.Tools.Gem,
                            "push",
                            "--norc",
                            fixture.OriginalPath,
                            "--host",
                            host,
                        },
                        command.Arguments
                    );
                    Assert.IsNotNull(command.Environment);
                    Assert.AreEqual(expectedKey, command.Environment["GEM_HOST_API_KEY"]);
                    Assert.AreEqual("C.UTF-8", command.Environment["LANG"]);
                    Assert.AreEqual("C.UTF-8", command.Environment["LC_ALL"]);
                    CollectionAssert.AreEquivalent(
                        StateVariables.Concat(OtherAllowedVariables).ToArray(),
                        command.Environment.Keys.ToArray()
                    );
                    Assert.AreEqual(
                        string.Join(
                            Path.PathSeparator,
                            Path.GetDirectoryName(fixture.Tools.Ruby),
                            "/usr/bin",
                            "/bin"
                        ),
                        command.Environment["PATH"]
                    );
                    foreach (string name in WindowsVariables)
                        Assert.AreEqual(
                            Environment.GetEnvironmentVariable(name),
                            command.Environment[name]
                        );
                    foreach (string name in StateVariables)
                    {
                        string state = command.Environment[name]!;
                        Assert.AreEqual(
                            Path.Combine(fixture.Tools.Scratch, name.ToLowerInvariant()),
                            state
                        );
                        Assert.IsTrue(Directory.Exists(state), name);
                        Assert.IsEmpty(Directory.EnumerateFileSystemEntries(state), name);
                    }
                    Assert.HasCount(
                        StateVariables.Length,
                        StateVariables.Select(name => command.Environment[name]).Distinct()
                    );
                    foreach (string name in ExcludedVariables)
                        Assert.DoesNotContain(name, command.Environment.Keys);
                    Assert.DoesNotContain(AmbientKey, command.Environment.Values);
                    Assert.DoesNotContain(
                        channel == "buddy" ? TrustedKey : RepositoryKey,
                        command.Environment.Values
                    );
                    Assert.DoesNotContain(expectedKey, command.Arguments);
                    Assert.DoesNotContain("-e", command.Arguments);
                    Assert.DoesNotContain("-r", command.Arguments);
                    Assert.DoesNotContain("require", string.Join(" ", command.Arguments));
                    Assert.IsEmpty(
                        Directory.EnumerateFiles(
                            command.Directory,
                            "*",
                            SearchOption.AllDirectories
                        )
                    );
                    CollectionAssert.AreEqual(
                        new byte[] { 1, 2, 3, 4 },
                        File.ReadAllBytes(fixture.OriginalPath)
                    );
                    return Task.FromResult(Success());
                },
                context.CancellationToken
            );

            Assert.AreEqual(1, calls);
            Assert.AreEqual(preparation.Prepared.Subject, result.Subject);
            Assert.AreEqual("selected-gem", result.Upload.Output);
            Assert.AreEqual(NativeTermination.Exited, result.Upload.Command.Termination);
            Assert.AreEqual(0, result.Upload.Command.ExitCode);
            Assert.AreEqual(1.25, result.Upload.Command.DurationSeconds);
            Assert.IsTrue(result.Upload.Command.Succeeded);
            Assert.IsNull(result.Upload.Command.Error);
            Assert.IsEmpty(result.Upload.Command.Stdout);
            Assert.IsEmpty(result.Upload.Command.Stderr);
            Assert.AreEqual(
                preparation.Prepared.Files[0].Sha256,
                Digest(File.ReadAllBytes(fixture.OriginalPath))
            );
            AssertTransientStateAbsent(result, fixture);
        }
        finally
        {
            foreach ((string name, string? value) in previous)
                Environment.SetEnvironmentVariable(name, value);
        }
    }

    [TestMethod]
    [DataRow("no-files")]
    [DataRow("extra-file")]
    [DataRow("wrong-unit")]
    [DataRow("wrong-name")]
    [DataRow("wrong-kind")]
    public async Task QualifiedStateDefectsStopBeforeNativeInvocation(string defect)
    {
        using var fixture = new Fixture();
        RubyPreparationResult preparation = fixture.Preparation;
        PreparedRelease prepared = preparation.Prepared;
        ReleaseFile file = prepared.Files[0];
        switch (defect)
        {
            case "errors":
                preparation = preparation with { Errors = ["failed"] };
                break;
            case "no-commands":
                preparation = preparation with { Commands = [] };
                break;
            case "no-files":
                prepared = prepared with { Files = [] };
                break;
            case "extra-file":
                prepared = prepared with
                {
                    Files =
                    [
                        file,
                        file with
                        {
                            Output = "other",
                            Path = "other.gem",
                            Version = "2.0",
                        },
                    ],
                };
                break;
            case "wrong-unit":
                prepared = prepared with { Subject = prepared.Subject with { Unit = "other" } };
                break;
            case "wrong-name":
                file = file with { Name = "other" };
                preparation.Native["name"] = "other";
                break;
            case "wrong-kind":
                file = file with { Kind = "other" };
                break;
            case "empty-output":
                file = file with { Output = "" };
                break;
            case "empty-build":
                file = file with { Build = "" };
                break;
            case "invalid-revision":
                prepared = prepared with { Subject = prepared.Subject with { Revision = "a" } };
                break;
            case "invalid-declaration":
                prepared = prepared with
                {
                    Subject = prepared.Subject with { Declaration = "../unit.yml" },
                };
                break;
            case "no-required-checks":
                prepared = prepared with { RequiredChecks = [], Checks = [] };
                break;
            case "reordered-required-checks":
                prepared = prepared with { RequiredChecks = RequiredChecks.Reverse().ToArray() };
                break;
            case "subset-required-checks":
                prepared = prepared with
                {
                    RequiredChecks = ["native-input"],
                    Checks = [new("native-input", ReleaseCheckStatus.Passed)],
                };
                break;
            default:
                Assert.Fail("Unknown qualification scenario.");
                break;
        }
        if (file != fixture.Preparation.Prepared.Files[0])
            prepared = prepared with { Files = [file] };
        await AssertRejectedAsync(preparation with { Prepared = prepared }, fixture);
    }

    [TestMethod]
    [DataRow("buddy", "github-packages-ruby", "refs/tags/v1", "true")]
    [DataRow("buddy", "github-packages-ruby", "refs/heads/", "true")]
    [DataRow("buddy", "rubygems", "refs/heads/development", "true")]
    [DataRow("official", "github-packages-ruby", "refs/heads/main", "true")]
    [DataRow("unsupported", "rubygems", "refs/heads/main", "true")]
    [DataRow("official", "rubygems", "refs/heads/development", "true")]
    [DataRow("official", "rubygems", "refs/tags/v1", "true")]
    public async Task UnsupportedChannelsAndRefsStopBeforeNativeInvocation(
        string channel,
        string destination,
        string selectedRef,
        string publicRelease
    )
    {
        using var fixture = new Fixture();
        RubyPreparationResult preparation = fixture.ForChannel(channel, destination, selectedRef);
        if (publicRelease == "missing")
            preparation.Native.Remove("public-release");
        else
            preparation.Native["public-release"] = publicRelease;
        await AssertRejectedAsync(preparation, fixture);
    }

    [TestMethod]
    public async Task DryRunStopsBeforeNativeInvocation()
    {
        using var fixture = new Fixture();
        RubyPreparationResult preparation = fixture.Preparation;
        await AssertRejectedAsync(
            preparation with
            {
                Prepared = preparation.Prepared with
                {
                    Subject = preparation.Prepared.Subject with { DryRun = true },
                },
            },
            fixture
        );
    }

    [TestMethod]
    [DataRow("buddy", "missing")]
    [DataRow("buddy", "null")]
    [DataRow("buddy", "blank")]
    [DataRow("official", "missing")]
    [DataRow("official", "null")]
    [DataRow("official", "blank")]
    public async Task MissingSelectedKeyNeverFallsBack(string channel, string defect)
    {
        using var fixture = new Fixture();
        RubyPreparationResult preparation =
            channel == "buddy"
                ? fixture.Preparation
                : fixture.ForChannel("official", "rubygems", "refs/heads/main");
        string selected = channel == "buddy" ? "GITHUB_TOKEN" : "GEM_HOST_API_KEY";
        if (defect == "missing")
            fixture.Platform.Remove(selected);
        else
            fixture.Platform[selected] = defect == "null" ? null : " \t\r\n";
        // The other destination's key, account/API keys and OIDC inputs remain available.
        await AssertRejectedAsync(preparation, fixture);
    }

    [TestMethod]
    [DataRow("missing-original")]
    [DataRow("changed-same-length")]
    [DataRow("wrong-digest")]
    [DataRow("wrong-size")]
    [DataRow("parent-path")]
    [DataRow("relative-originals")]
    [DataRow("relative-ruby")]
    [DataRow("relative-gem")]
    [DataRow("relative-scratch")]
    [DataRow("missing-scratch")]
    [DataRow("nonempty-scratch")]
    [DataRow("wrong-owned-path")]
    public async Task OriginalTransferDefectsStopBeforeNativeInvocation(string defect)
    {
        using var fixture = new Fixture();
        RubyPreparationResult preparation = fixture.Preparation;
        ReleaseFile file = preparation.Prepared.Files[0];
        RubyPublisherTools tools = fixture.Tools;
        string originals = fixture.Originals;
        switch (defect)
        {
            case "missing-original":
                File.Delete(fixture.OriginalPath);
                break;
            case "changed-same-length":
                File.WriteAllBytes(fixture.OriginalPath, [4, 3, 2, 1]);
                Assert.AreEqual(file.Size, new FileInfo(fixture.OriginalPath).Length);
                Assert.AreNotEqual(file.Sha256, Digest(File.ReadAllBytes(fixture.OriginalPath)));
                break;
            case "wrong-digest":
                file = file with { Sha256 = new string('b', 64) };
                break;
            case "wrong-size":
                file = file with { Size = file.Size + 1 };
                break;
            case "parent-path":
                file = file with { Path = "../original.gem" };
                break;
            case "absolute-path":
                file = file with { Path = fixture.OriginalPath };
                break;
            case "relative-originals":
                originals = "originals";
                break;
            case "relative-ruby":
                tools = tools with { Ruby = "ruby" };
                break;
            case "relative-gem":
                tools = tools with { Gem = "gem" };
                break;
            case "relative-scratch":
                tools = tools with { Scratch = "scratch" };
                break;
            case "missing-scratch":
                Directory.Delete(tools.Scratch);
                break;
            case "nonempty-scratch":
                File.WriteAllText(Path.Combine(tools.Scratch, "ambient-state"), AmbientKey);
                break;
            case "wrong-owned-path":
                file = file with { Path = "package/other.gem" };
                break;
            default:
                Assert.Fail("Unknown transfer scenario.");
                break;
        }
        preparation = preparation with { Prepared = preparation.Prepared with { Files = [file] } };
        await AssertRejectedAsync(preparation, fixture, originals, tools);
    }

    [TestMethod]
    public async Task OriginalChangedDuringKeySelectionStopsBeforeNativeInvocation()
    {
        using var fixture = new Fixture();
        int lookups = 0,
            calls = 0;
        var platform = new KeySelection(
            fixture.Platform,
            () =>
            {
                lookups++;
                File.WriteAllBytes(fixture.OriginalPath, [4, 3, 2, 1]);
            }
        );
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            RubyNativePublisher.PublishAsync(
                fixture.Preparation,
                fixture.Originals,
                fixture.Tools,
                platform,
                (_, _) =>
                {
                    calls++;
                    throw new AssertFailedException("Unexpected native push.");
                },
                context.CancellationToken
            )
        );
        Assert.AreEqual(1, lookups);
        Assert.AreEqual(0, calls);
        Assert.AreEqual(
            fixture.Preparation.Prepared.Files[0].Size,
            new FileInfo(fixture.OriginalPath).Length
        );
        Assert.AreNotEqual(
            fixture.Preparation.Prepared.Files[0].Sha256,
            Digest(File.ReadAllBytes(fixture.OriginalPath))
        );
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CancellationBeforeInvocationDoesNotCallExecutor(bool duringKeySelection)
    {
        using var fixture = new Fixture();
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(
            context.CancellationToken
        );
        if (!duringKeySelection)
            await cancelled.CancelAsync();
        var platform = new KeySelection(fixture.Platform, cancelled.Cancel);
        int calls = 0;
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            RubyNativePublisher.PublishAsync(
                fixture.Preparation,
                fixture.Originals,
                fixture.Tools,
                platform,
                (_, _) =>
                {
                    calls++;
                    throw new AssertFailedException("Unexpected native push.");
                },
                cancelled.Token
            )
        );
        Assert.AreEqual(0, calls);
    }

    [TestMethod]
    [DataRow(nameof(NativeTermination.Exited), 0, false, true)]
    [DataRow(nameof(NativeTermination.Exited), 17, false, false)]
    [DataRow(nameof(NativeTermination.Exited), 17, true, false)]
    [DataRow(nameof(NativeTermination.TimedOut), -1, true, false)]
    [DataRow(nameof(NativeTermination.Cancelled), -1, true, false)]
    [DataRow(nameof(NativeTermination.Cancelled), 0, true, false)]
    [DataRow(nameof(NativeTermination.InvalidOutput), 0, true, false)]
    [DataRow(nameof(NativeTermination.CleanupFailed), 0, true, false)]
    [DataRow(nameof(NativeTermination.StartFailed), -1, true, false)]
    [DataRow(nameof(NativeTermination.InputFailed), -1, true, false)]
    [DataRow(nameof(NativeTermination.InputFailed), 0, true, false)]
    [DataRow(nameof(NativeTermination.Exited), 0, true, true)]
    public async Task NativeOutcomesRetainFactsAndOmitDiagnostics(
        string terminationName,
        int exit,
        bool error,
        bool nativeSucceeded
    )
    {
        using var fixture = new Fixture();
        NativeTermination termination = Enum.Parse<NativeTermination>(terminationName);
        string unsafeText =
            RepositoryKey
            + " "
            + TrustedKey
            + " "
            + AmbientKey
            + " https://user:password@controlled.invalid arbitrary-native-diagnostic";
        var native = new NativeCommandResult(
            termination,
            exit < 0 ? null : exit,
            "pushed-before-response-loss " + unsafeText,
            "::error::" + unsafeText,
            4.75,
            error ? unsafeText : null
        );
        int calls = 0;
        RubyPublishResult result = await RubyNativePublisher.PublishAsync(
            fixture.Preparation,
            fixture.Originals,
            fixture.Tools,
            fixture.Platform,
            (_, _) =>
            {
                calls++;
                return Task.FromResult(native);
            },
            context.CancellationToken
        );

        Assert.AreEqual(1, calls);
        Assert.AreEqual(fixture.Preparation.Prepared.Subject, result.Subject);
        Assert.AreEqual("selected-gem", result.Upload.Output);
        Assert.AreEqual(termination, result.Upload.Command.Termination);
        Assert.AreEqual(exit < 0 ? (int?)null : exit, result.Upload.Command.ExitCode);
        Assert.AreEqual(4.75, result.Upload.Command.DurationSeconds);
        Assert.AreEqual(nativeSucceeded, result.Upload.Command.Succeeded);
        Assert.IsEmpty(result.Upload.Command.Stdout);
        Assert.IsEmpty(result.Upload.Command.Stderr);
        Assert.AreEqual(error ? Uncertainty : null, result.Upload.Command.Error);
        string retained = JsonSerializer.Serialize(result);
        Assert.DoesNotContain("arbitrary-native-diagnostic", retained);
        Assert.DoesNotContain("pushed-before-response-loss", retained);
        Assert.DoesNotContain("user:password", retained);
        Assert.DoesNotContain("::error::", retained);
        AssertTransientStateAbsent(result, fixture);
        using JsonDocument record = JsonDocument.Parse(retained);
        CollectionAssert.AreEquivalent(
            ResultFields,
            record.RootElement.EnumerateObject().Select(property => property.Name).ToArray()
        );
    }

    private async Task AssertRejectedAsync(
        RubyPreparationResult preparation,
        Fixture fixture,
        string? originals = null,
        RubyPublisherTools? tools = null
    )
    {
        int calls = 0;
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            RubyNativePublisher.PublishAsync(
                preparation,
                originals ?? fixture.Originals,
                tools ?? fixture.Tools,
                fixture.Platform,
                (_, _) =>
                {
                    calls++;
                    throw new AssertFailedException("Unexpected native push.");
                },
                context.CancellationToken
            )
        );
        Assert.AreEqual(0, calls);
    }

    private static void AssertTransientStateAbsent(RubyPublishResult result, Fixture fixture)
    {
        string retained = JsonSerializer.Serialize(result);
        foreach (string secret in new[] { RepositoryKey, TrustedKey, AmbientKey })
            Assert.DoesNotContain(secret, retained);
        foreach (
            string member in new[]
            {
                "Executable",
                "Arguments",
                "Environment",
                "DeadlineSeconds",
                "InheritEnvironment",
                "CloseStandardInput",
                "State",
            }
        )
            Assert.DoesNotContain("\"" + member + "\"", retained);
        Assert.DoesNotContain(fixture.Root, retained);
        Assert.DoesNotContain(fixture.Tools.Gem, retained);
        Assert.DoesNotContain(fixture.Tools.Ruby, retained);
    }

    private static string Digest(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static NativeCommandResult Success() =>
        new(
            NativeTermination.Exited,
            0,
            "arbitrary-success-output",
            "arbitrary-success-diagnostic",
            1.25,
            null
        );

    private sealed class KeySelection(IReadOnlyDictionary<string, string?> values, Action select)
        : IReadOnlyDictionary<string, string?>
    {
        public string? this[string key] => values[key];
        public IEnumerable<string> Keys => values.Keys;
        public IEnumerable<string?> Values => values.Values;
        public int Count => values.Count;

        public bool ContainsKey(string key) => values.ContainsKey(key);

        public bool TryGetValue(string key, out string? value)
        {
            select();
            return values.TryGetValue(key, out value);
        }

        public IEnumerator<KeyValuePair<string, string?>> GetEnumerator() => values.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } = Directory.CreateTempSubdirectory("ruby publish ").FullName;
        internal string Originals { get; }
        internal string OriginalPath { get; }
        internal RubyPublisherTools Tools { get; }
        internal RubyPreparationResult Preparation { get; }
        internal Dictionary<string, string?> Platform { get; } = new(StringComparer.Ordinal);

        internal Fixture()
        {
            Originals = Directory.CreateDirectory(Path.Combine(Root, "originals")).FullName;
            OriginalPath = Path.Combine(
                Directory.CreateDirectory(Path.Combine(Originals, "package")).FullName,
                "original.gem"
            );
            byte[] bytes = [1, 2, 3, 4];
            File.WriteAllBytes(OriginalPath, bytes);
            Tools = new(
                Path.Combine(Root, "prepared tools", "ruby"),
                Path.Combine(Root, "prepared tools", "gem"),
                Directory.CreateDirectory(Path.Combine(Root, "scratch")).FullName
            );
            PreparedRelease prepared = new(
                new(
                    new string('a', 40),
                    "refs/heads/development",
                    "hcoona-release-smoke-ruby",
                    "product/unit.yml",
                    "buddy",
                    "github-packages-ruby",
                    false
                ),
                [
                    new(
                        "gem-build",
                        "selected-gem",
                        "ruby-gem",
                        "hcoona-release-smoke-ruby",
                        "1.2.3",
                        "package/original.gem",
                        bytes.Length,
                        Digest(bytes)
                    ),
                ],
                RequiredChecks.ToArray(),
                RequiredChecks
                    .Select(id => new ReleaseCheck(id, ReleaseCheckStatus.Passed))
                    .ToArray()
            );
            Preparation = new(
                prepared,
                new(StringComparer.Ordinal)
                {
                    ["name"] = "hcoona-release-smoke-ruby",
                    ["version"] = "1.2.3",
                    ["platform"] = "ruby",
                    ["commit"] = prepared.Subject.Revision,
                    ["public-release"] = "true",
                },
                [
                    new("build", new(Tools.Ruby, Originals, [Tools.Gem, "build"], 180), Success()),
                    new("consumer", new(Tools.Ruby, Originals, ["consumer"], 180), Success()),
                ],
                []
            );
            foreach (string name in ExcludedVariables)
                Platform[name] = AmbientKey;
            Platform["GITHUB_TOKEN"] = RepositoryKey;
            Platform["GEM_HOST_API_KEY"] = TrustedKey;
            Platform["PATH"] = "controlled-ambient-path";
            foreach (string name in StateVariables)
                Platform[name] = Root;
        }

        internal RubyPreparationResult ForChannel(
            string channel,
            string destination,
            string selectedRef
        ) =>
            Preparation with
            {
                Prepared = Preparation.Prepared with
                {
                    Subject = Preparation.Prepared.Subject with
                    {
                        Channel = channel,
                        Destination = destination,
                        SelectedRef = selectedRef,
                    },
                },
            };

        public void Dispose() => Directory.Delete(Root, true);
    }
}
