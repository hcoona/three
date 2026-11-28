using System.Collections;
using System.Security.Cryptography;
using System.Text.Json;
using WorkflowDelivery.Destinations;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Release;

namespace WorkflowDelivery.Tests.Release;

[TestClass]
public sealed class RubyNativePublisherTests(TestContext context)
{
    private const string RepositoryKey = "controlled-repository-write-key";
    private const string TrustedKey = "controlled-action-exported-trusted-key";
    private const string AmbientKey = "controlled-unselected-ambient-key";
    private static readonly string[] Secrets = [RepositoryKey, TrustedKey, AmbientKey];
    private static readonly string[] State = ["HOME", "USERPROFILE", "XDG_CONFIG_HOME",
        "XDG_CACHE_HOME", "XDG_DATA_HOME", "APPDATA", "LOCALAPPDATA", "GEM_HOME", "GEM_PATH",
        "GEM_SPEC_CACHE", "TMPDIR", "TMP", "TEMP"];
    private static readonly string[] PlatformVariables = ["SystemRoot", "WINDIR", "PATHEXT"];
    private static readonly string[] EnvironmentKeys = [.. State, .. PlatformVariables,
        "PATH", "LANG", "LC_ALL", "GEM_HOST_API_KEY"];
    private static readonly string[] AmbientVariables = ["GITHUB_TOKEN", "GEM_HOST_API_KEY",
        "GH_TOKEN", "HTTP_PROXY", "HTTPS_PROXY", "RUBYOPT", "RUBYLIB",
        "BUNDLE_GEMS__RUBYGEMS__ORG", "ACTIONS_ID_TOKEN_REQUEST_TOKEN",
        "ACTIONS_ID_TOKEN_REQUEST_URL"];
    private static readonly string[] ResultFields = ["Subject", "Upload"];
    private static readonly byte[] OriginalBytes = [1, 2, 3, 4];

    [TestMethod]
    [DoNotParallelize]
    [DataRow("buddy", "github-packages-ruby", "refs/heads/development", "hcoona-release-smoke-ruby",
        "https://rubygems.pkg.github.com/hcoona", "Bearer " + RepositoryKey)]
    [DataRow("official", "rubygems", "refs/heads/main", "hcoona-release-smoke-ruby",
        "https://rubygems.org", TrustedKey)]
    [DataRow("buddy", "github-packages-ruby", "refs/heads/development",
        "asciidoctor-latexmath", "https://rubygems.pkg.github.com/hcoona",
        "Bearer " + RepositoryKey)]
    [DataRow("official", "rubygems", "refs/heads/main", "asciidoctor-latexmath",
        "https://rubygems.org", TrustedKey)]
    public async Task PushUsesSelectedKeyAndIsolatedState(string channel, string destination,
        string reference, string unit, string host, string key)
    {
        using var fixture = new Fixture(channel, destination, reference, unit);
        var previous = AmbientVariables.ToDictionary(name => name,
            Environment.GetEnvironmentVariable, StringComparer.Ordinal);
        try
        {
            foreach (string name in previous.Keys)
                Environment.SetEnvironmentVariable(name, AmbientKey);
            var commands = new List<NativeCommand>();
            RubyPublishResult result = await fixture.RunAsync((command, token) =>
            {
                commands.Add(command);
                Assert.AreEqual(context.CancellationToken, token);
                return Task.FromResult(new NativeCommandResult(NativeTermination.Exited, 0,
                    "native output", "native diagnostic", 1.25, null));
            }, context.CancellationToken);

            NativeCommand push = Assert.ContainsSingle(commands);
            Assert.AreEqual(fixture.Tools.Ruby, push.Executable);
            Assert.AreEqual(fixture.Tools.Scratch, push.Directory);
            Assert.AreEqual(180, push.DeadlineSeconds);
            Assert.IsFalse(push.InheritEnvironment);
            Assert.IsTrue(push.CloseStandardInput);
            string[] arguments = [fixture.Tools.Gem, "push", "--norc", fixture.Archive,
                "--host", host];
            CollectionAssert.AreEqual(arguments, push.Arguments);
            Assert.IsNotNull(push.Environment);
            CollectionAssert.AreEquivalent(EnvironmentKeys, push.Environment.Keys.ToArray());
            Assert.AreEqual(key, push.Environment["GEM_HOST_API_KEY"]);
            Assert.DoesNotContain(AmbientKey, push.Environment.Values);
            Assert.DoesNotContain(key, push.Arguments);
            Assert.AreEqual("C.UTF-8", push.Environment["LANG"]);
            Assert.AreEqual("C.UTF-8", push.Environment["LC_ALL"]);
            Assert.AreEqual(string.Join(Path.PathSeparator,
                Path.GetDirectoryName(fixture.Tools.Ruby), "/usr/bin", "/bin"),
                push.Environment["PATH"]);
            foreach (string name in PlatformVariables)
                Assert.AreEqual(Environment.GetEnvironmentVariable(name), push.Environment[name]);
            foreach (string name in State)
            {
                string home = Path.Combine(push.Directory, name.ToLowerInvariant());
                Assert.AreEqual(home, push.Environment[name]);
                Assert.IsEmpty(Directory.EnumerateFileSystemEntries(home));
            }
            CollectionAssert.AreEqual(OriginalBytes, File.ReadAllBytes(fixture.Archive));
            Assert.AreEqual(fixture.Preparation.Prepared.Subject, result.Subject);
            Assert.AreEqual("selected-gem", result.Upload.Output);
            Assert.IsTrue(result.Upload.Command.Succeeded);
            Assert.AreEqual(1.25, result.Upload.Command.DurationSeconds);
            AssertRedacted(result, fixture);
        }
        finally
        {
            foreach ((string name, string? value) in previous)
                Environment.SetEnvironmentVariable(name, value);
        }
    }

    [TestMethod]
    [DataRow("buddy", "rubygems", "refs/heads/development", false)]
    [DataRow("official", "github-packages-ruby", "refs/heads/main", false)]
    [DataRow("official", "rubygems", "refs/heads/development", false)]
    [DataRow("buddy", "github-packages-ruby", "refs/heads/development", true)]
    public async Task IneligibleSubjectCannotPush(string channel, string destination,
        string reference, bool dryRun)
    {
        using var fixture = new Fixture(channel, destination, reference, dryRun: dryRun);
        await AssertRejectedAsync(fixture);
    }

    [TestMethod]
    [DataRow("buddy", "github-packages-ruby", "refs/heads/development", "GITHUB_TOKEN")]
    [DataRow("official", "rubygems", "refs/heads/main", "GEM_HOST_API_KEY")]
    public async Task MissingSelectedKeyDoesNotFallBack(string channel, string destination,
        string reference, string selectedKey)
    {
        using var fixture = new Fixture(channel, destination, reference);
        fixture.Platform.Remove(selectedKey);
        await AssertRejectedAsync(fixture);
    }

    [TestMethod]
    [DataRow("another-unit", "ruby-gem", "another-unit")]
    [DataRow("hcoona-release-smoke-ruby", "other-kind", "hcoona-release-smoke-ruby")]
    [DataRow("hcoona-release-smoke-ruby", "ruby-gem", "unrelated-gem")]
    public async Task UnsupportedCoordinateCannotPush(string unit, string kind, string name)
    {
        using var fixture = new Fixture(unit: unit);
        fixture.SetFiles(fixture.Original with { Kind = kind, Name = name });
        await AssertRejectedAsync(fixture);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PushRequiresExactlyOneOriginal(bool extra)
    {
        using var fixture = new Fixture();
        fixture.SetFiles(extra ? [fixture.Original,
            fixture.Original with { Output = "second", Path = "other.gem" }] : []);
        await AssertRejectedAsync(fixture);
    }

    [TestMethod]
    [DataRow("../original.gem")]
    [DataRow("package/other.gem")]
    public async Task UnexpectedOriginalPathCannotPush(string path)
    {
        using var fixture = new Fixture();
        fixture.SetFiles(fixture.Original with { Path = path });
        await AssertRejectedAsync(fixture);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task OriginalBytesMustStillMatchAtLaunch(bool duringKeySelection)
    {
        using var fixture = new Fixture();
        void Substitute() => File.WriteAllBytes(fixture.Archive, [4, 3, 2, 1]);
        if (duringKeySelection) fixture.KeyLookup = Substitute;
        else Substitute();
        await AssertRejectedAsync(fixture);
        Assert.AreEqual(fixture.Original.Size, new FileInfo(fixture.Archive).Length);
    }

    [TestMethod]
    public async Task OriginalDeclaredSizeMustMatchAtLaunch()
    {
        using var fixture = new Fixture();
        fixture.SetFiles(fixture.Original with { Size = fixture.Original.Size + 1 });
        await AssertRejectedAsync(fixture);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task FreshScratchIsRequired(bool missing)
    {
        using var fixture = new Fixture();
        if (missing) Directory.Delete(fixture.Tools.Scratch);
        else File.WriteAllText(Path.Combine(fixture.Tools.Scratch, "ambient-state"), AmbientKey);
        await AssertRejectedAsync(fixture);
    }

    [TestMethod]
    [DataRow("originals", null, null, null)]
    [DataRow(null, "ruby", null, null)]
    [DataRow(null, null, "gem", null)]
    [DataRow(null, null, null, "scratch")]
    public async Task RelativePathsCannotPush(string? originals, string? ruby,
        string? gem, string? scratch)
    {
        using var fixture = new Fixture();
        fixture.Originals = originals ?? fixture.Originals;
        fixture.Tools = new(ruby ?? fixture.Tools.Ruby, gem ?? fixture.Tools.Gem,
            scratch ?? fixture.Tools.Scratch);
        await AssertRejectedAsync(fixture);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CancellationBeforeInvocationDoesNotCallExecutor(bool duringKeySelection)
    {
        using var fixture = new Fixture();
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(
            context.CancellationToken);
        if (duringKeySelection) fixture.KeyLookup = cancelled.Cancel;
        else await cancelled.CancelAsync();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => fixture.RunAsync(
            UnexpectedPush, cancelled.Token));
    }

    [TestMethod]
    [DataRow(nameof(NativeTermination.Exited), 0, false, true)]
    [DataRow(nameof(NativeTermination.Exited), 17, true, false)]
    [DataRow(nameof(NativeTermination.TimedOut), -1, true, false)]
    [DataRow(nameof(NativeTermination.Cancelled), -1, true, false)]
    [DataRow(nameof(NativeTermination.InvalidOutput), 0, true, false)]
    [DataRow(nameof(NativeTermination.CleanupFailed), 0, true, false)]
    [DataRow(nameof(NativeTermination.StartFailed), -1, true, false)]
    [DataRow(nameof(NativeTermination.InputFailed), -1, true, false)]
    [DataRow(nameof(NativeTermination.Exited), 0, true, true)]
    public async Task NativeOutcomesRetainFactsAndOmitDiagnostics(string termination, int exit,
        bool error, bool succeeded)
    {
        using var fixture = new Fixture();
        string unsafeText = string.Join(' ', Secrets) + " https://user:password@controlled.invalid";
        var native = new NativeCommandResult(Enum.Parse<NativeTermination>(termination),
            exit < 0 ? null : exit, "partial " + unsafeText, "::error::" + unsafeText,
            4.75, error ? unsafeText : null);
        int pushes = 0;
        RubyPublishResult result = await fixture.RunAsync((_, _) =>
        {
            pushes++;
            return Task.FromResult(native);
        }, context.CancellationToken);

        Assert.AreEqual(1, pushes);
        Assert.AreEqual(native.Termination, result.Upload.Command.Termination);
        Assert.AreEqual(native.ExitCode, result.Upload.Command.ExitCode);
        Assert.AreEqual(native.DurationSeconds, result.Upload.Command.DurationSeconds);
        Assert.AreEqual(succeeded, result.Upload.Command.Succeeded);
        Assert.AreEqual(error ? "Native Ruby publisher process failed; effects remain uncertain."
            : null, result.Upload.Command.Error);
        AssertRedacted(result, fixture);
    }

    private async Task AssertRejectedAsync(Fixture fixture) =>
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => fixture.RunAsync(
            UnexpectedPush, context.CancellationToken));

    private static Task<NativeCommandResult> UnexpectedPush(NativeCommand command,
        CancellationToken token) => throw new AssertFailedException("Unexpected native push.");

    private static void AssertRedacted(RubyPublishResult result, Fixture fixture)
    {
        Assert.IsEmpty(result.Upload.Command.Stdout);
        Assert.IsEmpty(result.Upload.Command.Stderr);
        string serialized = JsonSerializer.Serialize(result);
        foreach (string secret in Secrets) Assert.DoesNotContain(secret, serialized);
        Assert.DoesNotContain("user:password", serialized);
        Assert.DoesNotContain("::error::", serialized);
        Assert.DoesNotContain(fixture.Root, serialized);
        using JsonDocument document = JsonDocument.Parse(serialized);
        CollectionAssert.AreEquivalent(ResultFields, document.RootElement.EnumerateObject()
            .Select(property => property.Name).ToArray());
    }

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
        internal string Originals { get; set; }
        internal string Archive { get; }
        internal RubyPublisherTools Tools { get; set; }
        internal ReleasePreparationResult Preparation { get; private set; }
        internal ReleaseFile Original => Preparation.Prepared.Files[0];
        internal Action? KeyLookup { get; set; }
        internal Dictionary<string, string?> Platform { get; } = new(StringComparer.Ordinal)
        {
            ["GITHUB_TOKEN"] = RepositoryKey,
            ["GEM_HOST_API_KEY"] = TrustedKey,
            ["GH_TOKEN"] = AmbientKey,
            ["RUBYGEMS_API_KEY"] = AmbientKey,
        };

        internal Fixture(string channel = "buddy", string destination = "github-packages-ruby",
            string reference = "refs/heads/development", string unit = "hcoona-release-smoke-ruby",
            bool dryRun = false)
        {
            Originals = Directory.CreateDirectory(Path.Combine(Root, "originals")).FullName;
            string package = Directory.CreateDirectory(Path.Combine(Originals, "package")).FullName;
            Archive = Path.Combine(package, "original.gem");
            File.WriteAllBytes(Archive, OriginalBytes);
            Tools = new(Path.Combine(Root, "prepared tools", "ruby"),
                Path.Combine(Root, "prepared tools", "gem"),
                Directory.CreateDirectory(Path.Combine(Root, "scratch")).FullName);
            Preparation = new(new(new(new string('a', 40), reference, unit, "product/unit.yml",
                channel, destination, dryRun), [new("gem-build", "selected-gem", "ruby-gem",
                    unit, "1.2.3", "package/original.gem", OriginalBytes.Length,
                    Convert.ToHexStringLower(SHA256.HashData(OriginalBytes)))], [], []),
                new(StringComparer.Ordinal), [], []);
        }

        internal void SetFiles(params ReleaseFile[] files) => Preparation = Preparation with
        { Prepared = Preparation.Prepared with { Files = files } };

        internal Task<RubyPublishResult> RunAsync(
            Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
            CancellationToken token) => RubyNativePublisher.PublishAsync(
                DestinationBindings.Ruby(Preparation.Prepared), Originals,
                Tools, KeyLookup is null ? Platform : new KeySelection(Platform, KeyLookup),
                execute, token);

        public void Dispose() => PreparationCheckout.Delete(Root);
    }
}
