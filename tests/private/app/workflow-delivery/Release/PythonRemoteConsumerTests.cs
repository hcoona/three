using System.Text.Json;
using WorkflowDelivery.Ecosystems;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Release;

namespace WorkflowDelivery.Tests.Release;

[TestClass]
public sealed class PythonRemoteConsumerTests(TestContext context)
{
    [TestMethod]
    [DataRow("buddy", "testpypi", "python-wheel")]
    [DataRow("buddy", "testpypi", "python-sdist")]
    [DataRow("official", "pypi", "python-wheel")]
    [DataRow("official", "pypi", "python-sdist")]
    public async Task RemoteOriginalConsumptionUsesNativeSelectedIndexAndQualifiedHash(
        string channel, string destination, string kind)
    {
        using var fixture = new Fixture();
        PreparedRelease prepared = fixture.Prepared with { Subject = fixture.Prepared.Subject
            with { Channel = channel, Destination = destination } };
        PythonRemoteConsumerResult result = await fixture.RunAsync(prepared, kind,
            context.CancellationToken);

        Assert.AreEqual(ReleaseCheckStatus.Passed, result.Check.Status);
        Assert.IsEmpty(result.Errors);
        Assert.AreEqual(prepared.Subject, result.Subject);
        ReleaseFile original = prepared.Files.Single(file => file.Kind == kind);
        NativeCommand install = Assert.ContainsSingle(fixture.Commands.Where(command =>
            command.Arguments.Contains("install")));
        string selected = destination == "testpypi" ? "https://test.pypi.org/simple/"
            : "https://pypi.org/simple/";
        Assert.AreEqual(selected, result.Index);
        Assert.AreEqual(selected, Value(install, "--index-url"));
        Assert.AreEqual($"hcoona-release-smoke-python==1.0.0 --hash=sha256:{original.Sha256}\n",
            fixture.Requirement);
        Assert.Contains("--require-hashes", install.Arguments);
        Assert.Contains("--no-cache", install.Arguments);
        Assert.Contains("--no-deps", install.Arguments);
        Assert.Contains("--no-config", install.Arguments);
        Assert.AreEqual("disabled", Value(install, "--keyring-provider"));
        Assert.IsFalse(install.Arguments.Any(arg => arg.EndsWith(".whl",
            StringComparison.Ordinal) || arg.EndsWith(".tar.gz", StringComparison.Ordinal)));
        Assert.DoesNotContain("--find-links", install.Arguments);
        Assert.DoesNotContain("--extra-index-url", install.Arguments);
        Assert.AreEqual(":all:", Value(install, kind == "python-wheel"
            ? "--only-binary" : "--no-binary"));
        foreach (NativeCommand command in fixture.Commands)
        {
            Assert.IsFalse(command.InheritEnvironment);
            var environment = command.Environment;
            Assert.IsNotNull(environment);
            Assert.IsFalse(environment.Where(pair => pair.Value is not null)
                .Select(pair => pair.Key).Any(key => key.StartsWith("GIT_",
                StringComparison.Ordinal) || key.StartsWith("ACTIONS_", StringComparison.Ordinal)
                || key is "GH_TOKEN" or "GITHUB_TOKEN" or "PYTHONPATH" or "UV_EXTRA_INDEX_URL"));
            Assert.StartsWith(fixture.Scratch, environment["HOME"]!);
        }
        NativeCommand absent = fixture.Commands.Single(command =>
            command.Arguments.Contains("consumer-absent"));
        NativeCommand consume = fixture.Commands.Single(command =>
            command.Arguments.Contains("consume"));
        Assert.AreEqual(Value(install, "--python"), consume.Executable);
        Assert.Contains("-I", consume.Arguments);
        Assert.Contains("-B", consume.Arguments);
        Assert.AreEqual(fixture.Helper, consume.Arguments[2]);
        Assert.AreEqual("1.0.0", consume.Arguments[5]);
        Assert.AreEqual(consume.Executable, absent.Executable);
        Assert.IsLessThan(fixture.Commands.IndexOf(install), fixture.Commands.IndexOf(absent));
        Assert.IsLessThan(fixture.Commands.IndexOf(consume), fixture.Commands.IndexOf(install));
        Assert.IsFalse(consume.Directory.StartsWith(fixture.Checkout, StringComparison.Ordinal));
        Assert.AreEqual(original, result.Original);
        Assert.IsTrue(result.Commands.All(command => command.Result.Succeeded));
        Assert.IsTrue(result.Commands.All(command => command.Result.Stdout.Length == 0 &&
            command.Result.Stderr.Length == 0));
        if (kind == "python-sdist")
        {
            NativeCommand conversion = Assert.ContainsSingle(fixture.Commands.Where(command =>
                command.Arguments.Contains("--no-editable")));
            Assert.AreEqual(fixture.Checkout, conversion.Directory);
            Assert.Contains("--frozen", conversion.Arguments);
            Assert.Contains("--no-build-isolation", conversion.Arguments);
            Assert.Contains("workflow-delivery-python", conversion.Arguments);
            Assert.Contains("dev", conversion.Arguments);
            Assert.Contains("--no-build-isolation", install.Arguments);
            Assert.Contains("build-dependency-check", install.Arguments);
            Assert.IsLessThan(fixture.Commands.IndexOf(absent),
                fixture.Commands.IndexOf(conversion));
        }
        else
        {
            Assert.Contains("venv", fixture.Commands[0].Arguments);
            Assert.DoesNotContain("--no-build-isolation", install.Arguments);
            Assert.IsEmpty(fixture.Commands.Where(command => command.Arguments.Contains("sync")));
        }
    }

    [TestMethod]
    [DataRow("destination")]
    [DataRow("pair")]
    [DataRow("quality")]
    [DataRow("version")]
    [DataRow("kind")]
    [DataRow("populated")]
    [DataRow("checkout-scratch")]
    public async Task UnsupportedOrUnqualifiedRemoteInputsCannotExecute(string defect)
    {
        using var fixture = new Fixture();
        PreparedRelease prepared = fixture.Prepared;
        string kind = "python-wheel";
        PythonPackageTools tools = fixture.Tools;
        switch (defect)
        {
            case "destination":
                prepared = prepared with
                    { Subject = prepared.Subject with { Destination = "other" } };
                break;
            case "pair": prepared = prepared with { Files = [prepared.Files[0]] }; break;
            case "quality":
                prepared = prepared with { Checks = [new("build", ReleaseCheckStatus.Failed)] };
                break;
            case "version":
                prepared = prepared with { Files = prepared.Files.Select(file => file with
                    { Version = "1.0.0\nother @ https://example.test/package.whl" }).ToArray() };
                break;
            case "kind": kind = "other"; break;
            case "populated": await File.WriteAllTextAsync(Path.Combine(fixture.Scratch,
                "existing"), "product", context.CancellationToken); break;
            case "checkout-scratch": tools = tools with { Scratch = fixture.Checkout }; break;
            default: Assert.Fail("Unknown scenario."); break;
        }
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => PythonRemoteConsumer.RunAsync(
            prepared, kind, tools, fixture.ExecuteAsync, context.CancellationToken));
        Assert.IsEmpty(fixture.Commands);
    }

    [TestMethod]
    [DataRow("Exited", 1, "Failed")]
    [DataRow("TimedOut", null, "TimedOut")]
    [DataRow("Cancelled", null, "Cancelled")]
    [DataRow("InvalidOutput", 0, "Failed")]
    [DataRow("StartFailed", null, "Failed")]
    public async Task UnsuccessfulNativeConsumptionStopsAndRetainsOutcome(string termination,
        int? exit, string status)
    {
        using var fixture = new Fixture();
        fixture.FailureStage = "install";
        fixture.Failure = new(Enum.Parse<NativeTermination>(termination), exit,
            "arbitrary public URL", "raw diagnostics", 7.5, "raw exception");
        PythonRemoteConsumerResult result = await fixture.RunAsync(fixture.Prepared,
            "python-wheel", context.CancellationToken);

        Assert.AreEqual(Enum.Parse<ReleaseCheckStatus>(status), result.Check.Status);
        PythonRemoteCommand reached = result.Commands[^1];
        Assert.AreEqual("install", reached.Stage);
        Assert.AreEqual(fixture.Failure.Termination, reached.Result.Termination);
        Assert.AreEqual(exit, reached.Result.ExitCode);
        Assert.AreEqual(7.5, reached.Result.DurationSeconds);
        Assert.AreEqual("Native details omitted.", reached.Result.Error);
        Assert.HasCount(1, fixture.Commands.Where(command =>
            command.Arguments.Contains("install")));
        Assert.IsEmpty(fixture.Commands.Where(command => command.Arguments.Contains("consume")));
        Assert.IsNotEmpty(result.Errors);
        Assert.DoesNotContain("raw", JsonSerializer.Serialize(result));
    }

    [TestMethod]
    [DataRow("present", "python-wheel")]
    [DataRow("malformed", "python-wheel")]
    [DataRow("wrong-name", "python-wheel")]
    [DataRow("present", "python-sdist")]
    [DataRow("malformed", "python-sdist")]
    [DataRow("wrong-name", "python-sdist")]
    public async Task ExistingProductCannotSupplyRemoteAcknowledgement(string defect, string kind)
    {
        using var fixture = new Fixture();
        fixture.Absent = defect switch
        {
            "present" => "{\"name\":\"hcoona-release-smoke-python\",\"absent\":false}",
            "malformed" => "{",
            _ => "{\"name\":\"other\",\"absent\":true}",
        };
        PythonRemoteConsumerResult result = await fixture.RunAsync(fixture.Prepared,
            kind, context.CancellationToken);
        Assert.AreEqual(ReleaseCheckStatus.Failed, result.Check.Status);
        Assert.IsNotEmpty(result.Errors);
        Assert.IsEmpty(fixture.Commands.Where(command => command.Arguments.Contains("install")));
        Assert.AreEqual("absent", result.Commands[^1].Stage);
        Assert.IsTrue(result.Commands[^1].Result.Succeeded);
    }

    [TestMethod]
    [DataRow("sync", "tools")]
    [DataRow("--no-editable", "noneditable-tools")]
    public async Task SourceToolFailureCannotReachRemoteInstall(string argument, string stage)
    {
        using var fixture = new Fixture();
        fixture.FailureStage = argument;
        fixture.Failure = new(NativeTermination.TimedOut, null, "", "", 180, null);
        PythonRemoteConsumerResult result = await fixture.RunAsync(fixture.Prepared,
            "python-sdist", context.CancellationToken);
        Assert.AreEqual(ReleaseCheckStatus.TimedOut, result.Check.Status);
        Assert.AreEqual(stage, result.Commands[^1].Stage);
        Assert.AreEqual(NativeTermination.TimedOut, result.Commands[^1].Result.Termination);
        Assert.AreEqual(180, result.Commands[^1].Result.DurationSeconds);
        Assert.IsEmpty(fixture.Commands.Where(command => command.Arguments.Contains("install") ||
            command.Arguments.Contains("consumer-absent") ||
            command.Arguments.Contains("consume")));
    }

    [TestMethod]
    [DataRow("version")]
    [DataRow("origin")]
    [DataRow("malformed")]
    public async Task MissingInstalledNativeFactsCannotQualify(string defect)
    {
        using var fixture = new Fixture();
        fixture.Installed = defect switch
        {
            "version" => "{\"name\":\"hcoona-release-smoke-python\",\"version\":\"2\"," +
                "\"origin\":\"x\"}",
            "origin" => "{\"name\":\"hcoona-release-smoke-python\",\"version\":\"1.0.0\"," +
                "\"origin\":\"\"}",
            _ => "{",
        };
        PythonRemoteConsumerResult result = await fixture.RunAsync(fixture.Prepared,
            "python-wheel", context.CancellationToken);
        Assert.AreEqual(ReleaseCheckStatus.Failed, result.Check.Status);
        Assert.AreEqual("consume", result.Commands[^1].Stage);
        Assert.IsTrue(result.Commands[^1].Result.Succeeded);
        Assert.IsNotEmpty(result.Errors);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CancellationBeforeOrAfterNativeCommandCannotQualify(bool after)
    {
        using var fixture = new Fixture();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(
            context.CancellationToken);
        if (after) fixture.Cancel = cancel;
        else cancel.Cancel();
        PythonRemoteConsumerResult result = await fixture.RunAsync(fixture.Prepared,
            "python-wheel", cancel.Token);
        Assert.AreEqual(ReleaseCheckStatus.Cancelled, result.Check.Status);
        Assert.HasCount(after ? 1 : 0, result.Commands);
        Assert.IsEmpty(fixture.Commands.Where(command => command.Arguments.Contains("install")));
        if (after) Assert.IsTrue(result.Commands[0].Result.Succeeded);
    }

    private static string Value(NativeCommand command, string flag) =>
        command.Arguments[Array.IndexOf(command.Arguments, flag) + 1];

    private sealed class Fixture : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "python-remote-" +
            Guid.NewGuid().ToString("N"));
        internal string Scratch { get; }
        internal string Checkout { get; }
        internal string Helper => Path.Combine(Checkout, "packages.py");
        internal List<NativeCommand> Commands { get; } = [];
        internal string? FailureStage { get; set; }
        internal NativeCommandResult? Failure { get; set; }
        internal string? Requirement { get; private set; }
        internal CancellationTokenSource? Cancel { get; set; }
        internal string Absent { get; set; } =
            "{\"name\":\"hcoona-release-smoke-python\",\"absent\":true}";
        internal string Installed { get; set; } =
            "{\"name\":\"hcoona-release-smoke-python\",\"version\":\"1.0.0\"," +
            "\"origin\":\"installed\"}";
        internal PreparedRelease Prepared { get; } = new(new(new string('a', 40),
            "refs/heads/development", "hcoona-release-smoke-python", "release.yml", "buddy",
            "testpypi", false),
            [new("distributions", "wheel", "python-wheel", "hcoona-release-smoke-python",
                "1.0.0", "archives/product.whl", 10, new string('b', 64)),
                new("distributions", "sdist", "python-sdist", "hcoona-release-smoke-python",
                    "1.0.0", "archives/product.tar.gz", 20, new string('c', 64))],
            ["build"], [new("build", ReleaseCheckStatus.Passed)]);
        internal PythonPackageTools Tools => new(Checkout, Scratch, Path.Combine(root, "uv"),
            Path.Combine(root, "python"), Helper, 180, new Dictionary<string, string?>
            {
                ["PATH"] = "prepared-native-tools", ["GITHUB_TOKEN"] = "must-not-forward",
                ["ACTIONS_ID_TOKEN_REQUEST_TOKEN"] = "must-not-forward",
                ["GIT_DIR"] = Checkout, ["UV_EXTRA_INDEX_URL"] = "https://other.test/simple/",
                ["PYTHONPATH"] = Checkout,
            });

        internal Fixture()
        {
            Checkout = Directory.CreateDirectory(Path.Combine(root, "checkout")).FullName;
            Scratch = Directory.CreateDirectory(Path.Combine(root, "scratch")).FullName;
        }

        internal Task<PythonRemoteConsumerResult> RunAsync(PreparedRelease prepared, string kind,
            CancellationToken token) => PythonRemoteConsumer.RunAsync(prepared, kind, Tools,
                ExecuteAsync, token);

        internal async Task<NativeCommandResult> ExecuteAsync(NativeCommand command,
            CancellationToken token)
        {
            Commands.Add(command);
            if (command.Arguments.Contains("install"))
                Requirement = await File.ReadAllTextAsync(Value(command, "--requirements"), token);
            if (FailureStage is not null && command.Arguments.Contains(FailureStage))
                return Failure!;
            Cancel?.Cancel();
            string stdout = command.Arguments.Contains("consumer-absent") ? Absent :
                command.Arguments.Contains("consume") ? Installed : "";
            return new(NativeTermination.Exited, 0, stdout, "", 0.5, null);
        }

        public void Dispose() => Directory.Delete(root, true);
    }
}
