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
        "https://upload.pypi.org/legacy/")]
    [DataRow("buddy", "testpypi", "refs/heads/development", "sdist",
        "https://test.pypi.org/legacy/", PythonReleaseUnits.Nbgv)]
    [DataRow("official", "pypi", "refs/heads/main", "wheel",
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
    [DataRow(nameof(NativeTermination.Cancelled), 0)]
    [DataRow(nameof(NativeTermination.TimedOut), -1)]
    [DataRow(nameof(NativeTermination.InvalidOutput), 0)]
    [DataRow(nameof(NativeTermination.InputFailed), -1)]
    [DataRow(nameof(NativeTermination.InputFailed), 0)]
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
    [DataRow("dry-run")]
    [DataRow("official-ref")]
    [DataRow("buddy-ref")]
    [DataRow("destination")]
    [DataRow("wrong-name")]
    [DataRow("wrong-unit")]
    [DataRow("missing-pair")]
    [DataRow("wrong-kind")]
    [DataRow("unknown-output")]
    [DataRow("missing-context")]
    [DataRow("missing-url")]
    [DataRow("wrong-platform")]
    [DataRow("nonempty-scratch")]
    [DataRow("overlapping-scratch")]
    [DataRow("relative-uv")]
    [DataRow("relative-scratch")]
    [DataRow("missing-original")]
    public async Task UnsupportedOrUnqualifiedRequestsCannotInvokePublisher(string defect)
    {
        using var fixture = new Fixture();
        PreparedRelease prepared = fixture.Prepared;
        PythonPublisherTools tools = fixture.Tools;
        string output = "wheel";
        switch (defect)
        {
            case "dry-run":
                prepared = prepared with
                {
                    Subject = prepared.Subject with
                    { DryRun = true }
                }; break;
            case "official-ref":
                prepared = prepared with
                {
                    Subject = prepared.Subject with
                    { Channel = "official", Destination = "pypi" }
                }; break;
            case "buddy-ref":
                prepared = prepared with
                {
                    Subject = prepared.Subject with
                    { SelectedRef = "refs/tags/unsupported" }
                }; break;
            case "destination":
                prepared = prepared with
                {
                    Subject = prepared.Subject with
                    { Destination = "pypi" }
                }; break;
            case "failed-check":
                prepared = prepared with
                {
                    Checks =
                [new("qualified", ReleaseCheckStatus.Failed)]
                }; break;
            case "missing-check": prepared = prepared with { Checks = [] }; break;
            case "wrong-name":
                prepared = prepared with
                {
                    Files = [prepared.Files[0] with
                { Name = "foreign" }, prepared.Files[1]]
                }; break;
            case "wrong-unit":
                prepared = prepared with
                {
                    Subject = prepared.Subject with
                    { Unit = "foreign" }
                }; break;
            case "wrong-version":
                prepared = prepared with
                {
                    Files = [prepared.Files[0] with
                { Version = "0.2" }, prepared.Files[1]]
                }; break;
            case "missing-pair": prepared = prepared with { Files = [prepared.Files[0]] }; break;
            case "wrong-kind":
                prepared = prepared with
                {
                    Files = [prepared.Files[0] with
                { Kind = "other" }, prepared.Files[1]]
                }; break;
            case "unknown-output": output = "foreign"; break;
            case "missing-context":
                fixture.Platform.Remove("ACTIONS_ID_TOKEN_REQUEST_TOKEN");
                break;
            case "missing-url": fixture.Platform.Remove("ACTIONS_ID_TOKEN_REQUEST_URL"); break;
            case "wrong-platform": fixture.Platform["GITHUB_ACTIONS"] = "false"; break;
            case "nonempty-scratch":
                File.WriteAllText(Path.Combine(tools.Scratch, "state"), "x");
                break;
            case "overlapping-scratch": tools = tools with { Scratch = fixture.Originals }; break;
            case "relative-uv": tools = tools with { Uv = "uv" }; break;
            case "relative-scratch": tools = tools with { Scratch = "scratch" }; break;
            case "missing-original":
                File.Delete(Path.Combine(fixture.Originals,
                prepared.Files[0].Path)); break;
        }
        using var masks = new StringWriter();
        bool invoked = false;
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            PythonNativePublisher.PublishAsync(prepared, fixture.Originals, output, tools,
                fixture.Platform, masks, (_, _) =>
                { invoked = true; throw new AssertFailedException("Unexpected native writer."); },
                context.CancellationToken));

        Assert.IsFalse(invoked);
        Assert.IsEmpty(masks.ToString());
    }

    [TestMethod]
    public async Task ChangedOriginalCannotInvokePublisher()
    {
        using var fixture = new Fixture();
        File.WriteAllBytes(Path.Combine(fixture.Originals, fixture.Prepared.Files[1].Path),
            [9, 9, 9]);
        using var masks = new StringWriter();
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            PythonNativePublisher.PublishAsync(fixture.Prepared, fixture.Originals, "wheel",
                fixture.Tools, fixture.Platform, masks, (_, _) =>
                    throw new AssertFailedException("Unexpected native writer."),
                context.CancellationToken));
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
