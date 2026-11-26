using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class PythonPytestConfigurationReaderTests
{
    private static readonly string PytestRoot = Path.GetFullPath("pytest endpoint with spaces");
    private static readonly string[] Targets = ["tests/eng", "src/member/tests"];
    private static readonly string[] ImportMode = ["--import-mode=importlib"];
    private static readonly string[] Arguments = ["query.py", PytestRoot];

    private static JsonObject Facts() => JsonSerializer.SerializeToNode(new
    {
        directory = PytestRoot,
        root = PytestRoot,
        configuration_file = Path.Combine(PytestRoot, "pyproject.toml"),
        testpaths = Targets,
        addopts = ImportMode,
        pythonpath = Array.Empty<string>(),
        required_plugins = Array.Empty<string>()
    })!.AsObject();

    private static PythonPytestConfigurationReader Reader(string output,
        Action<NativeCommand>? observe = null, NativeTermination termination =
            NativeTermination.Exited, string? error = null) => new((command, _token) =>
    {
        observe?.Invoke(command);
        return Task.FromResult(new NativeCommandResult(termination,
            termination == NativeTermination.Exited ? 0 : null,
            output, "private-native-diagnostic", 0, error));
    });

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task NativeConfigurationAndCommandScopeRemainExplicit(bool emptyOptions)
    {
        JsonObject facts = Facts();
        if (emptyOptions) facts["addopts"] = new JsonArray();
        var environment = new Dictionary<string, string?>
        {
            ["HOME"] = "owned-home",
            ["PYTEST_ADDOPTS"] = "foreign-options",
            ["PYTEST_PLUGINS"] = "foreign-plugin"
        };
        PythonPytestConfiguration configuration = await Reader(facts.ToJsonString(), command =>
        {
            Assert.AreEqual("owned-python", command.Executable);
            Assert.AreEqual(PytestRoot, command.Directory);
            CollectionAssert.AreEqual(Arguments, command.Arguments);
            Assert.AreEqual(30, command.DeadlineSeconds);
            Assert.IsFalse(command.InheritEnvironment);
            Assert.IsNotNull(command.Environment);
            Assert.AreEqual("owned-home", command.Environment["HOME"]);
            Assert.IsNull(command.Environment["PYTEST_ADDOPTS"]);
            Assert.IsNull(command.Environment["PYTEST_PLUGINS"]);
            Assert.AreEqual("1", command.Environment["PYTEST_DISABLE_PLUGIN_AUTOLOAD"]);
        }).ReadAsync(PytestRoot, "owned-python", "query.py", environment, CancellationToken.None);

        Assert.AreEqual("pyproject.toml", configuration.ConfigurationFile);
        CollectionAssert.AreEqual(Targets, configuration.TestPaths);
        CollectionAssert.AreEqual(emptyOptions ? [] : ImportMode, configuration.Options);
        Assert.AreEqual("foreign-options", environment["PYTEST_ADDOPTS"]);
        Assert.AreEqual("foreign-plugin", environment["PYTEST_PLUGINS"]);
    }

    [TestMethod]
    public async Task NoncanonicalEndpointFailsBeforeNativeRequest()
    {
        bool executed = false;
        PythonPytestConfigurationReader reader = Reader(Facts().ToJsonString(),
            _command => executed = true);
        string endpoint = PytestRoot + Path.DirectorySeparatorChar + ".";
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => reader.ReadAsync(endpoint,
            "python", "query.py", new Dictionary<string, string?>(), CancellationToken.None));
        Assert.IsFalse(executed);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task NativeFailureAndCancellationRemainTerminal(bool cancelled)
    {
        bool executed = false;
        using var cancellation = new CancellationTokenSource();
        if (cancelled) cancellation.Cancel();
        PythonPytestConfigurationReader reader = Reader(Facts().ToJsonString(),
            _command => executed = true, NativeTermination.StartFailed,
            "private-native-error");
        Task<PythonPytestConfiguration> Call() => reader.ReadAsync(PytestRoot, "python", "query.py",
            new Dictionary<string, string?>(), cancellation.Token);
        if (cancelled)
        {
            await Assert.ThrowsExactlyAsync<OperationCanceledException>(Call);
            Assert.IsFalse(executed);
        }
        else
        {
            InvalidDataException error = await Assert.ThrowsExactlyAsync<InvalidDataException>(
                Call);
            Assert.AreEqual("Native pytest configuration extraction failed: StartFailed, " +
                "exit unknown. private-native-error stderr: private-native-diagnostic",
                error.Message);
            Assert.IsTrue(executed);
        }
    }
}
