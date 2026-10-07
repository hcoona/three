using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class PythonPytestConfigurationReaderTests
{
    private static readonly string Root = Path.GetFullPath("pytest endpoint with spaces");
    private static readonly string[] Targets = ["tests/eng", "src/member/tests"];
    private static readonly string[] ImportMode = ["--import-mode=importlib"];
    private static readonly string[] Arguments = ["query.py", Root];

    private static JsonObject Facts() => JsonSerializer.SerializeToNode(new
    {
        directory = Root,
        root = Root,
        configuration_file = Path.Combine(Root, "pyproject.toml"),
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
            Assert.AreEqual(Root, command.Directory);
            CollectionAssert.AreEqual(Arguments, command.Arguments);
            Assert.AreEqual(30, command.DeadlineSeconds);
            Assert.IsNotNull(command.Environment);
            Assert.AreEqual("owned-home", command.Environment["HOME"]);
            Assert.IsNull(command.Environment["PYTEST_ADDOPTS"]);
            Assert.IsNull(command.Environment["PYTEST_PLUGINS"]);
            Assert.AreEqual("1", command.Environment["PYTEST_DISABLE_PLUGIN_AUTOLOAD"]);
        }).ReadAsync(Root, "owned-python", "query.py", environment, CancellationToken.None);

        Assert.AreEqual("pyproject.toml", configuration.ConfigurationFile);
        CollectionAssert.AreEqual(Targets, configuration.TestPaths);
        CollectionAssert.AreEqual(emptyOptions ? [] : ImportMode, configuration.Options);
        Assert.AreEqual("foreign-options", environment["PYTEST_ADDOPTS"]);
        Assert.AreEqual("foreign-plugin", environment["PYTEST_PLUGINS"]);
    }

    [TestMethod]
    [DataRow("endpoint")]
    [DataRow("root")]
    [DataRow("missing-field")]
    [DataRow("malformed-json")]
    [DataRow("no-config")]
    [DataRow("relative-config")]
    [DataRow("outside-config")]
    [DataRow("empty-targets")]
    [DataRow("duplicate-targets")]
    [DataRow("glob")]
    [DataRow("escape")]
    [DataRow("absolute-target")]
    [DataRow("wrong-type")]
    [DataRow("plugin-option")]
    [DataRow("pythonpath")]
    [DataRow("required-plugin")]
    public async Task InvalidNativeConfigurationCannotCertifyTargets(string fault)
    {
        JsonObject facts = Facts();
        switch (fault)
        {
            case "endpoint": facts["directory"] = Path.GetDirectoryName(Root); break;
            case "root": facts["root"] = Path.GetDirectoryName(Root); break;
            case "missing-field": facts.Remove("addopts"); break;
            case "no-config": facts["configuration_file"] = null; break;
            case "relative-config": facts["configuration_file"] = "pyproject.toml"; break;
            case "outside-config":
                facts["configuration_file"] = Path.Combine(Path.GetDirectoryName(Root)!,
                    "pytest.ini");
                break;
            case "empty-targets": facts["testpaths"] = new JsonArray(); break;
            case "duplicate-targets": facts["testpaths"] = new JsonArray("tests", "tests"); break;
            case "glob": facts["testpaths"] = new JsonArray("tests/*"); break;
            case "escape": facts["testpaths"] = new JsonArray("../tests"); break;
            case "absolute-target": facts["testpaths"] = new JsonArray(Root); break;
            case "wrong-type": facts["testpaths"] = "tests"; break;
            case "plugin-option": facts["addopts"] = new JsonArray("-p", "custom-plugin"); break;
            case "pythonpath": facts["pythonpath"] = new JsonArray("src/external"); break;
            case "required-plugin":
                facts["required_plugins"] = new JsonArray("custom-plugin");
                break;
        }
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            Reader(fault == "malformed-json" ? "{" : facts.ToJsonString()).ReadAsync(Root,
                "python", "query.py",
                new Dictionary<string, string?>(), CancellationToken.None));
    }

    [TestMethod]
    public async Task NoncanonicalEndpointFailsBeforeNativeRequest()
    {
        bool executed = false;
        PythonPytestConfigurationReader reader = Reader(Facts().ToJsonString(),
            _command => executed = true);
        string endpoint = Root + Path.DirectorySeparatorChar + ".";
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
        Task<PythonPytestConfiguration> Call() => reader.ReadAsync(Root, "python", "query.py",
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
            Assert.AreEqual("Native pytest configuration extraction failed.", error.Message);
            Assert.IsTrue(executed);
        }
    }
}
