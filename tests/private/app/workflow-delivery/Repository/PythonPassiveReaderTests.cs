using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class PythonPassiveReaderTests
{
    private static readonly string Directory = Path.GetFullPath("python passive endpoint");
    private static readonly string Generated = Path.Combine(Directory, "absent-generated.py");
    private static readonly string[] BuildExtras = ["build"];
    private static readonly string[] BuildNames = ["hatchling", "nbgv-python"];
    private static readonly IReadOnlyDictionary<string, string?> Environment =
        new Dictionary<string, string?> { ["PYTHONPATH"] = null, ["UV_NO_MANAGED_PYTHON"] = "1" };

    private static JsonObject Response() => new()
    {
        ["directory"] = Directory,
        ["build_requirements"] = new JsonArray(
            new JsonObject { ["name"] = "hatchling", ["extras"] = new JsonArray() },
            new JsonObject { ["name"] = "nbgv-python", ["extras"] = new JsonArray("build") }),
        ["sources"] = new JsonObject { ["nbgv-python"] = "workspace" },
        ["nbgv"] = new JsonObject
        {
            ["working_directory"] = Directory,
            ["generated_file"] = Generated
        }
    };

    private static Task<PythonPassiveInputs> Read(JsonObject response) =>
        new PythonPassiveReader((_, _) => Task.FromResult(new NativeCommandResult(
            NativeTermination.Exited, 0, response.ToJsonString(), "", 0, null)))
            .ReadAsync(Directory, "python-helper", "passive-script", Environment,
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
        PythonPassiveInputs inputs = await reader.ReadAsync(Directory, "python-helper",
            "passive-script", Environment, CancellationToken.None);

        PythonBuildRequirement[] requirements = inputs.Project.BuildRequirements!;
        CollectionAssert.AreEqual(BuildNames,
            requirements.Select(requirement => requirement.Name).ToArray());
        Assert.IsEmpty(requirements[0].Extras);
        CollectionAssert.AreEqual(BuildExtras, requirements[1].Extras);
        Assert.AreEqual("workspace", inputs.Project.Sources!["nbgv-python"]);
        Assert.AreEqual(Directory, inputs.Project.Directory);
        Assert.IsNotNull(inputs.Nbgv);
        Assert.AreEqual(Directory, inputs.Nbgv.WorkingDirectory);
        Assert.AreEqual(Generated, inputs.Nbgv.GeneratedFile);
        Assert.IsNotNull(command);
        Assert.AreEqual(Directory, command.Directory);
        Assert.AreEqual("python-helper", command.Executable);
        CollectionAssert.AreEqual(new[] { "passive-script", Directory }, command.Arguments);
        Assert.AreEqual(30, command.DeadlineSeconds);
        Assert.AreSame(Environment, command.Environment);
    }

    [TestMethod]
    public async Task NativeDotSegmentCoordinatesRemainUnchanged()
    {
        string working = Path.Combine(Directory, "..", "shared");
        string generated = Path.Combine(Directory, "generated", "..", "absent-version.py");
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
            Path.Combine(Directory, "..", "shared"), "python-helper", "passive-script",
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
    [DataRow("subject")]
    [DataRow("missing-build")]
    [DataRow("build-type")]
    [DataRow("empty-requirement")]
    [DataRow("missing-extras")]
    [DataRow("missing-sources")]
    [DataRow("source-class")]
    [DataRow("missing-plugin")]
    [DataRow("plugin-type")]
    [DataRow("relative-working")]
    [DataRow("relative-generated")]
    public async Task ChangedOrMalformedPassiveResponseFails(string fault)
    {
        JsonObject response = Response();
        switch (fault)
        {
            case "subject": response["directory"] = Path.GetDirectoryName(Directory); break;
            case "missing-build": response.Remove("build_requirements"); break;
            case "build-type": response["build_requirements"] = "not-an-array"; break;
            case "empty-requirement":
                response["build_requirements"] = new JsonArray(new JsonObject
                { ["name"] = "", ["extras"] = new JsonArray() }); break;
            case "missing-extras":
                response["build_requirements"] = new JsonArray(new JsonObject
                { ["name"] = "hatchling" }); break;
            case "missing-sources": response.Remove("sources"); break;
            case "source-class": response["sources"]!["nbgv-python"] = "path"; break;
            case "missing-plugin": response.Remove("nbgv"); break;
            case "plugin-type": response["nbgv"] = 42; break;
            case "relative-working": response["nbgv"]!["working_directory"] = "."; break;
            case "relative-generated": response["nbgv"]!["generated_file"] = "version.py"; break;
        }
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => Read(response));
    }

    [TestMethod]
    public async Task NativeFailureAndCancellationRemainTerminal()
    {
        var reader = new PythonPassiveReader((_, _) => Task.FromResult(new NativeCommandResult(
            NativeTermination.Exited, 1, "partial", "private-diagnostic", 0, "private-error")));
        InvalidDataException failure = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            reader.ReadAsync(Directory, "python-helper", "passive-script", Environment,
                CancellationToken.None));
        Assert.AreEqual("Python passive extraction failed.", failure.Message);
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => reader.ReadAsync(
            Directory, "python-helper", "passive-script", Environment, new(canceled: true)));
    }
}
