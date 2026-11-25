using System.Security.Cryptography;
using System.Text.Json;
using WorkflowDelivery.Destinations;
using WorkflowDelivery.Ecosystems;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Release;

namespace WorkflowDelivery.Tests.Release;

[TestClass]
public sealed class PythonNativePublisherTests(TestContext context)
{
    private const string Secret = "controlled-publisher-secret";
    private const string Oidc = "controlled-oidc-request-secret";
    private static readonly string[] StateVariables = ["HOME", "USERPROFILE", "XDG_CONFIG_HOME",
        "XDG_CACHE_HOME", "UV_CACHE_DIR", "TMPDIR", "TMP", "TEMP"];

    [TestMethod]
    [DataRow("buddy", "testpypi", "refs/heads/development", "wheel",
        "https://test.pypi.org/legacy/")]
    [DataRow("official", "pypi", "refs/heads/main", "sdist",
        "https://upload.pypi.org/legacy/", PythonReleaseUnits.Nbgv)]
    public async Task NativePublisherReceivesOnlyQualifiedOriginalAndSelectedContext(
        string channel, string destination, string selectedRef, string output, string endpoint,
        string unit = PythonReleaseUnits.Smoke)
    {
        using var fixture = new Fixture(unit);
        PreparedRelease prepared = fixture.Prepared with
        {
            Subject = fixture.Prepared.Subject
            with
            { Channel = channel, Destination = destination, SelectedRef = selectedRef }
        };
        using var masks = new StringWriter();
        int calls = 0;
        PythonPublishResult result = await PythonNativePublisher.PublishAsync(prepared,
            fixture.Originals, output, fixture.Tools, fixture.Platform, masks, (command, _) =>
            {
                calls++;
                Assert.AreEqual(fixture.Tools.Uv, command.Executable);
                Assert.AreEqual(fixture.Tools.Scratch, command.Directory);
                Assert.AreEqual(180, command.DeadlineSeconds);
                Assert.IsFalse(command.InheritEnvironment);
                Assert.IsTrue(command.CloseStandardInput);
                CollectionAssert.AreEqual(new[] { "publish", "--no-config", "--publish-url",
                    endpoint, "--trusted-publishing", "always", "--keyring-provider", "disabled",
                    "--no-attestations", "--", Path.Combine(fixture.Originals,
                        prepared.Files.Single(file => file.Output == output).Path) },
                    command.Arguments);
                Assert.IsNotNull(command.Environment);
                Assert.AreEqual("true", command.Environment["GITHUB_ACTIONS"]);
                Assert.AreEqual(Oidc, command.Environment["ACTIONS_ID_TOKEN_REQUEST_TOKEN"]);
                Assert.AreEqual(fixture.Platform["ACTIONS_ID_TOKEN_REQUEST_URL"],
                    command.Environment["ACTIONS_ID_TOKEN_REQUEST_URL"]);
                Assert.DoesNotContain("UV_PUBLISH_TOKEN", command.Environment.Keys);
                Assert.DoesNotContain("HTTP_PROXY", command.Environment.Keys);
                Assert.DoesNotContain("PATH", command.Environment.Keys);
                foreach (string name in StateVariables)
                {
                    string path = command.Environment[name]!;
                    Assert.StartsWith(fixture.Tools.Scratch + Path.DirectorySeparatorChar, path);
                    Assert.IsTrue(Directory.Exists(path));
                    Assert.IsEmpty(Directory.EnumerateFileSystemEntries(path));
                }
                return Task.FromResult(new NativeCommandResult(NativeTermination.Exited,
                    0, "", "", 1.25, null));
            }, context.CancellationToken);

        Assert.AreEqual(1, calls);
        Assert.AreEqual(output, result.Upload.Output);
        Assert.IsTrue(result.Upload.Command.Succeeded);
        Assert.AreEqual(1.25, result.Upload.Command.DurationSeconds);
        Assert.IsFalse(result.MaskForwardingFailed);
        Assert.Contains("omitted", result.DiagnosticLimitation);
        Assert.DoesNotContain(Oidc, JsonSerializer.Serialize(result));
        foreach (ReleaseFile file in prepared.Files)
            Assert.AreEqual(file.Sha256, Convert.ToHexStringLower(SHA256.HashData(
                File.ReadAllBytes(Path.Combine(fixture.Originals, file.Path)))));
    }

    [TestMethod]
    [DataRow(nameof(NativeTermination.Exited), 0)]
    [DataRow(nameof(NativeTermination.Exited), 7)]
    [DataRow(nameof(NativeTermination.Cancelled), -1)]
    [DataRow(nameof(NativeTermination.TimedOut), -1)]
    [DataRow(nameof(NativeTermination.InvalidOutput), 0)]
    [DataRow(nameof(NativeTermination.InputFailed), -1)]
    [DataRow(nameof(NativeTermination.CleanupFailed), -1)]
    [DataRow(nameof(NativeTermination.StartFailed), -1)]
    public async Task NativeOutcomesCannotPersistCredentialBearingDiagnostics(
        string terminationName, int exit)
    {
        NativeTermination termination = Enum.Parse<NativeTermination>(terminationName);
        using var fixture = new Fixture();
        using var masks = new StringWriter();
        var native = new NativeCommandResult(termination, exit < 0 ? null : exit,
            "::add-mask::" + Secret + "\r\n::error::" + Secret + "\n" +
            "prefix ::add-mask::unsafe\n::add-mask::bad\rpayload\n" +
            "::add-mask::invalid\r\r\n::add-mask::incomplete-" + Secret,
            "token " + Oidc + "\n::add-mask::second-secret\n", 4.25,
            "unsafe " + Oidc);
        int calls = 0;
        PythonPublishResult result = await PythonNativePublisher.PublishAsync(fixture.Prepared,
            fixture.Originals, "wheel", fixture.Tools, fixture.Platform, masks, (_, _) =>
                { calls++; return Task.FromResult(native); }, context.CancellationToken);

        Assert.AreEqual(1, calls);
        Assert.AreEqual(termination, result.Upload.Command.Termination);
        Assert.AreEqual(native.ExitCode, result.Upload.Command.ExitCode);
        Assert.AreEqual(native.Succeeded, result.Upload.Command.Succeeded);
        Assert.AreEqual(4.25, result.Upload.Command.DurationSeconds);
        Assert.IsEmpty(result.Upload.Command.Stdout);
        Assert.IsEmpty(result.Upload.Command.Stderr);
        Assert.AreEqual("Native failure details omitted.", result.Upload.Command.Error);
        Assert.AreEqual("::add-mask::" + Secret + Environment.NewLine +
            "::add-mask::second-secret" + Environment.NewLine, masks.ToString());
        Assert.IsFalse(result.MaskForwardingFailed);
        string retained = JsonSerializer.Serialize(result);
        Assert.DoesNotContain(Secret, retained);
        Assert.DoesNotContain(Oidc, retained);
        Assert.DoesNotContain("incomplete", masks.ToString());
        Assert.DoesNotContain("::error::", masks.ToString());
    }

    [TestMethod]
    [DataRow(true, "testpypi", "hcoona-release-smoke-python", "python-wheel", "wheel")]
    [DataRow(false, "pypi", "hcoona-release-smoke-python", "python-wheel", "wheel")]
    [DataRow(false, "testpypi", "foreign", "python-wheel", "wheel")]
    [DataRow(false, "testpypi", "hcoona-release-smoke-python", "other", "wheel")]
    [DataRow(false, "testpypi", "hcoona-release-smoke-python", "python-wheel", "foreign")]
    public async Task IneligibleSelectionCannotInvokePublisher(bool dryRun, string destination,
        string name, string kind, string output)
    {
        using var fixture = new Fixture();
        PreparedRelease prepared = fixture.Prepared with
        {
            Subject = fixture.Prepared.Subject with { DryRun = dryRun, Destination = destination },
            Files = [fixture.Prepared.Files[0] with { Name = name, Kind = kind },
                fixture.Prepared.Files[1]],
        };
        await AssertNoPublishAsync(fixture, prepared, output: output);
    }

    [TestMethod]
    public async Task MissingPairCannotInvokePublisher()
    {
        using var fixture = new Fixture();
        await AssertNoPublishAsync(fixture, fixture.Prepared with
        { Files = [fixture.Prepared.Files[0]] });
    }

    [TestMethod]
    [DataRow("ACTIONS_ID_TOKEN_REQUEST_TOKEN", null)]
    [DataRow("ACTIONS_ID_TOKEN_REQUEST_URL", null)]
    [DataRow("GITHUB_ACTIONS", "false")]
    public async Task PublicationRequiresCompleteActionsContext(string name, string? value)
    {
        using var fixture = new Fixture();
        if (value is null) fixture.Platform.Remove(name);
        else fixture.Platform[name] = value;
        await AssertNoPublishAsync(fixture, fixture.Prepared);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PublisherRequiresFreshNonOverlappingScratch(bool overlapping)
    {
        using var fixture = new Fixture();
        PythonPublisherTools tools = fixture.Tools;
        if (overlapping) tools = tools with { Scratch = fixture.Originals };
        else File.WriteAllText(Path.Combine(tools.Scratch, "state"), "x");
        await AssertNoPublishAsync(fixture, fixture.Prepared, tools);
    }

    [TestMethod]
    [DataRow("uv", null)]
    [DataRow(null, "scratch")]
    public async Task PublisherRequiresAbsoluteToolAndScratch(string? uv, string? scratch)
    {
        using var fixture = new Fixture();
        await AssertNoPublishAsync(fixture, fixture.Prepared, fixture.Tools with
        { Uv = uv ?? fixture.Tools.Uv, Scratch = scratch ?? fixture.Tools.Scratch });
    }

    private async Task AssertNoPublishAsync(Fixture fixture, PreparedRelease prepared,
        PythonPublisherTools? tools = null, string output = "wheel")
    {
        using var masks = new StringWriter();
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            PythonNativePublisher.PublishAsync(prepared, fixture.Originals, output,
                tools ?? fixture.Tools, fixture.Platform, masks, (_, _) =>
                    throw new AssertFailedException("Unexpected native writer."),
                context.CancellationToken));
        Assert.IsEmpty(masks.ToString());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task MissingOrChangedOriginalCannotInvokePublisher(bool missing)
    {
        using var fixture = new Fixture();
        string path = Path.Combine(fixture.Originals, fixture.Prepared.Files[1].Path);
        if (missing) File.Delete(path);
        else File.WriteAllBytes(path, [9, 9, 9]);
        await AssertNoPublishAsync(fixture, fixture.Prepared);
    }

    [TestMethod]
    public async Task CancelledRequestCannotInvokePublisherOrForwardControls()
    {
        using var fixture = new Fixture();
        using var masks = new StringWriter();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            PythonNativePublisher.PublishAsync(fixture.Prepared, fixture.Originals, "wheel",
                fixture.Tools, fixture.Platform, masks, (_, _) =>
                    throw new AssertFailedException("Unexpected native writer."), cancelled.Token));

        Assert.IsEmpty(masks.ToString());
    }

    [TestMethod]
    public async Task MaskForwardingFailurePreservesActualNativeOutcome()
    {
        using var fixture = new Fixture();
        using var masks = new FailedWriter();
        PythonPublishResult result = await PythonNativePublisher.PublishAsync(fixture.Prepared,
            fixture.Originals, "wheel", fixture.Tools, fixture.Platform, masks, (_, _) =>
                Task.FromResult(new NativeCommandResult(NativeTermination.Exited, 0,
                    "::add-mask::" + Secret + "\n", "", 2.5, null)), context.CancellationToken);

        Assert.IsTrue(result.MaskForwardingFailed);
        Assert.IsTrue(result.Upload.Command.Succeeded);
        Assert.AreEqual(0, result.Upload.Command.ExitCode);
        Assert.AreEqual(2.5, result.Upload.Command.DurationSeconds);
        Assert.IsNull(result.Upload.Command.Error);
        Assert.DoesNotContain(Secret, JsonSerializer.Serialize(result));
    }

    private sealed class FailedWriter : StringWriter
    {
        public override Task WriteLineAsync(string? value) =>
            throw new IOException("controlled " + Secret);
    }

    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } = Directory.CreateTempSubdirectory("python-publish-")
            .FullName;
        internal string Originals { get; }
        internal PythonPublisherTools Tools { get; }
        internal PreparedRelease Prepared { get; }
        internal Dictionary<string, string?> Platform { get; } = new()
        {
            ["GITHUB_ACTIONS"] = "true",
            ["ACTIONS_ID_TOKEN_REQUEST_URL"] = "https://controlled.invalid/oidc",
            ["ACTIONS_ID_TOKEN_REQUEST_TOKEN"] = Oidc,
            ["UV_PUBLISH_TOKEN"] = "ambient-secret",
            ["HTTP_PROXY"] = "http://ambient.invalid",
            ["PATH"] = "ambient-path",
        };
        internal Fixture(string unit = PythonReleaseUnits.Smoke)
        {
            Originals = Directory.CreateDirectory(Path.Combine(Root, "originals")).FullName;
            Tools = new(Path.Combine(Root, "uv"), Directory.CreateDirectory(
                Path.Combine(Root, "scratch")).FullName);
            ReleaseFile[] files = new[] { ("wheel", "python-wheel", ".whl"),
                ("sdist", "python-sdist", ".tar.gz") }.Select(item =>
                {
                    string name = unit.Replace('-', '_') + "-0.1.0b1" + item.Item3;
                    byte[] bytes = [1, 2, 3];
                    File.WriteAllBytes(Path.Combine(Originals, name), bytes);
                    return new ReleaseFile("python-distributions", item.Item1, item.Item2,
                        unit, "0.1.0b1", name, bytes.Length,
                        Convert.ToHexStringLower(SHA256.HashData(bytes)));
                }).ToArray();
            Prepared = new(new(new string('a', 40), "refs/heads/development",
                unit, "unit.yml", "buddy", "testpypi", false), files, ["qualified"],
                [new("qualified", ReleaseCheckStatus.Passed)]);
        }
        public void Dispose() => Directory.Delete(Root, true);
    }
}
