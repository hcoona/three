using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class PythonPassiveReaderTests
{
    private static readonly string PassiveDirectory = Path.GetFullPath("python passive endpoint");
    private static readonly string Generated =
        Path.Combine(PassiveDirectory, "absent-generated.py");
    private static readonly string[] BuildExtras = ["build"];
    private static readonly string[] BuildNames = ["hatchling", "nbgv-python"];
    private static readonly IReadOnlyDictionary<string, string?> Environment =
        new Dictionary<string, string?> { ["PYTHONPATH"] = null, ["UV_NO_MANAGED_PYTHON"] = "1" };

    private static JsonObject Response() => new()
    {
        ["directory"] = PassiveDirectory,
        ["build_requirements"] = new JsonArray(
            new JsonObject { ["name"] = "hatchling", ["extras"] = new JsonArray() },
            new JsonObject { ["name"] = "nbgv-python", ["extras"] = new JsonArray("build") }),
        ["sources"] = new JsonObject { ["nbgv-python"] = "workspace" },
        ["nbgv"] = new JsonObject
        {
            ["working_directory"] = PassiveDirectory,
            ["generated_file"] = Generated
        }
    };

    private static Task<PythonPassiveInputs> Read(JsonObject response) =>
        new PythonPassiveReader((_, _) => Task.FromResult(new NativeCommandResult(
            NativeTermination.Exited, 0, response.ToJsonString(), "", 0, null)))
            .ReadAsync(PassiveDirectory, "python-helper", "passive-script", Environment,
                CancellationToken.None);

    [TestMethod]
    public async Task ParsedRequirementsSourcesAndPluginCoordinatesAreTransferred()
    {
        NativeCommand? command = null;
        var reader = new PythonPassiveReader((request, _) =>
        {
            command = request;
            return Task.FromResult(new NativeCommandResult(NativeTermination.Exited, 0,
                Response().ToJsonString(), "", 0, null));
        });
        PythonPassiveInputs inputs = await reader.ReadAsync(PassiveDirectory, "python-helper",
            "passive-script", Environment, CancellationToken.None);

        PythonBuildRequirement[] requirements = inputs.Project.BuildRequirements!;
        CollectionAssert.AreEqual(BuildNames,
            requirements.Select(requirement => requirement.Name).ToArray());
        Assert.IsEmpty(requirements[0].Extras);
        CollectionAssert.AreEqual(BuildExtras, requirements[1].Extras);
        Assert.AreEqual("workspace", inputs.Project.Sources!["nbgv-python"]);
        Assert.AreEqual(PassiveDirectory, inputs.Project.Directory);
        Assert.IsNotNull(inputs.Nbgv);
        Assert.AreEqual(PassiveDirectory, inputs.Nbgv.WorkingDirectory);
        Assert.AreEqual(Generated, inputs.Nbgv.GeneratedFile);
        Assert.IsNotNull(command);
        Assert.AreEqual(PassiveDirectory, command.Directory);
        Assert.AreEqual("python-helper", command.Executable);
        CollectionAssert.AreEqual(new[] { "passive-script", PassiveDirectory }, command.Arguments);
        Assert.AreEqual(30, command.DeadlineSeconds);
        Assert.AreSame(Environment, command.Environment);
        Assert.IsFalse(command.InheritEnvironment);
    }

    [TestMethod]
    public async Task NativeDotSegmentCoordinatesRemainUnchanged()
    {
        string working = Path.Combine(PassiveDirectory, "..", "shared");
        string generated = Path.Combine(PassiveDirectory, "generated", "..", "absent-version.py");
        JsonObject response = Response();
        response["nbgv"]!["working_directory"] = working;
        response["nbgv"]!["generated_file"] = generated;

        PythonPassiveInputs inputs = await Read(response);

        Assert.IsNotNull(inputs.Nbgv);
        Assert.AreEqual(working, inputs.Nbgv.WorkingDirectory);
        Assert.AreEqual(generated, inputs.Nbgv.GeneratedFile);
    }

    [TestMethod]
    public async Task NoncanonicalCallerEndpointFailsBeforeExtraction()
    {
        bool executed = false;
        var reader = new PythonPassiveReader((_, _) =>
        {
            executed = true;
            return Task.FromResult(new NativeCommandResult(NativeTermination.Exited, 0,
                Response().ToJsonString(), "", 0, null));
        });
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => reader.ReadAsync(
            Path.Combine(PassiveDirectory, "..", "shared"), "python-helper", "passive-script",
            Environment, CancellationToken.None));
        Assert.IsFalse(executed);
    }

    [TestMethod]
    public async Task TablelessAndEmptyBuildContextsRemainDistinct()
    {
        JsonObject response = Response();
        response["build_requirements"] = null;
        response["nbgv"] = null;
        PythonPassiveInputs tableless = await Read(response);
        Assert.IsNull(tableless.Project.BuildRequirements);
        Assert.IsNull(tableless.Nbgv);
        response["build_requirements"] = new JsonArray();
        Assert.IsEmpty((await Read(response)).Project.BuildRequirements!);
        response["nbgv"] = Response()["nbgv"]!.DeepClone();
        response["nbgv"]!["generated_file"] = null;
        Assert.IsNull((await Read(response)).Nbgv!.GeneratedFile);
    }

    [TestMethod]
    [DataRow("build_requirements", "\"not-an-array\"")]
    [DataRow("build_requirements", """[{"name":"","extras":[]}]""")]
    [DataRow("build_requirements", """[{"name":"hatchling"}]""")]
    [DataRow("sources", null)]
    [DataRow("nbgv", "42")]
    [DataRow("sources/nbgv-python", "\"path\"")]
    [DataRow("nbgv/working_directory", "\".\"")]
    [DataRow("nbgv/generated_file", "\"version.py\"")]
    public async Task InvalidPassiveResponseCannotSupplyFacts(string path, string? json)
    {
        JsonObject response = Response();
        string[] fields = path.Split('/');
        JsonObject target = fields.Length == 1 ? response : response[fields[0]]!.AsObject();
        if (json is null) target.Remove(fields[^1]);
        else target[fields[^1]] = JsonNode.Parse(json);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => Read(response));
    }

    [TestMethod]
    public async Task ForeignPassiveEndpointCannotSupplyFacts()
    {
        JsonObject response = Response();
        response["directory"] = Path.GetDirectoryName(PassiveDirectory);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => Read(response));
    }

    [TestMethod]
    public async Task PassiveFailureAndCancellationRemainTerminal()
    {
        var reader = new PythonPassiveReader((_, _) => Task.FromResult(new NativeCommandResult(
            NativeTermination.Exited, 1, "partial", "private-diagnostic", 0, "private-error")));
        InvalidDataException failure = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            reader.ReadAsync(PassiveDirectory, "python-helper", "passive-script", Environment,
                CancellationToken.None));
        Assert.AreEqual("Python passive extraction failed: Exited, exit 1. private-error " +
            "stderr: private-diagnostic", failure.Message);
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => reader.ReadAsync(
            PassiveDirectory, "python-helper", "passive-script", Environment, new(canceled: true)));
    }
}
