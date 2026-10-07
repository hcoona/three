using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class PythonNativeSupplementReaderTests
{
    [TestMethod]
    public async Task ReadPreflightsInputsBeforeNativeSupplement()
    {
        using var fixture = new Fixture();
        PythonSupplementResult result = await fixture.ReadAsync();
        NativeCommand preflight = fixture.Commands[0];
        CollectionAssert.AreEquivalent(new[] { fixture.Parent, fixture.Root, fixture.Member },
            preflight.Arguments.Skip(2).ToArray());
        Assert.AreEqual("--configuration-only", preflight.Arguments[1]);
        Assert.AreEqual("passive-script", preflight.Arguments[0]);
        Assert.IsNotNull(preflight.Environment);
        CollectionAssert.AreEquivalent(fixture.Environment.ToArray(),
            preflight.Environment.ToArray());
        Assert.AreEqual(fixture.Root, preflight.Directory);
        Assert.HasCount(2, fixture.Commands);
        NativeCommand native = fixture.Commands[1];
        Assert.AreEqual("rust-supplement", native.Executable);
        Assert.IsNotNull(native.Environment);
        CollectionAssert.AreEquivalent(fixture.Environment.ToArray(), native.Environment.ToArray());
        using JsonDocument request = JsonDocument.Parse(
            await File.ReadAllTextAsync(Assert.ContainsSingle(native.Arguments)));
        Assert.AreEqual(fixture.Root,
            request.RootElement.GetProperty("workspace_root").GetString());
        Assert.AreEqual(fixture.Member, request.RootElement.GetProperty("projects")[1]
            .GetProperty("directory").GetString());
        Assert.AreEqual("nbgv-python[build]", request.RootElement.GetProperty("projects")[1]
            .GetProperty("build_requirements")[1].GetString());
        Assert.AreEqual(JsonValueKind.Null, request.RootElement.GetProperty("projects")[0]
            .GetProperty("build_requirements").ValueKind);
        JsonElement operation = request.RootElement.GetProperty("group_operations")[0];
        Assert.AreEqual("quality", operation.GetProperty("id").GetString());
        Assert.AreEqual(fixture.Root, operation.GetProperty("directory").GetString());
        Assert.AreEqual("example", operation.GetProperty("packages")[0].GetString());
        Assert.IsFalse(operation.GetProperty("no_dev").GetBoolean());
        JsonElement marker = request.RootElement.GetProperty("markers")[0];
        Assert.AreEqual("opaque-marker", marker.GetProperty("id").GetString());
        Assert.AreEqual("python_version >= '3.14'", marker.GetProperty("expression").GetString());
        Assert.AreEqual("build", marker.GetProperty("extras")[0].GetString());
        JsonElement constraint = request.RootElement.GetProperty("python_constraints")[0];
        Assert.AreEqual("opaque-constraint", constraint.GetProperty("id").GetString());
        Assert.AreEqual(">=3.14", constraint.GetProperty("specifier").GetString());
        Assert.AreEqual("opaque-native-id", result.Facts.GetProperty("opaque").GetString());
        CollectionAssert.AreEquivalent(new[] { fixture.Parent, fixture.Root, fixture.Member }
            .Select(directory => Path.Combine(directory, "pyproject.toml")).ToArray(),
            result.ConfigurationInputs);
    }

    [TestMethod]
    [DataRow((int)NativeTermination.Exited, 1)]
    [DataRow((int)NativeTermination.Cancelled, null)]
    [DataRow((int)NativeTermination.TimedOut, null)]
    public async Task ReadStopsOnUnsuccessfulConfigurationPreflight(
        int termination, int? exitCode)
    {
        using var fixture = new Fixture
        {
            PreflightFailure =
            new((NativeTermination)termination, exitCode, "private-partial", "private-token", 0,
                "private-error")
        };
        InvalidDataException error = await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => fixture.ReadAsync());
        Assert.AreEqual("Python configuration preflight failed.", error.Message);
        Assert.HasCount(1, fixture.Commands);
        Assert.IsFalse(File.Exists(fixture.RequestPath));
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("duplicate")]
    [DataRow("wrong")]
    [DataRow("malformed")]
    public async Task ReadRejectsIncompleteConfigurationInputs(string mode)
    {
        using var fixture = new Fixture { PreflightMode = mode };
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => fixture.ReadAsync());
        Assert.HasCount(1, fixture.Commands);
        Assert.IsFalse(File.Exists(fixture.RequestPath));
    }

    [TestMethod]
    public async Task ReadStopsOnUnsuccessfulNativeSupplement()
    {
        using var fixture = new Fixture
        {
            NativeFailure =
            new(NativeTermination.Exited, 1, "private-partial", "private-token", 0, "private-error")
        };
        InvalidDataException error = await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => fixture.ReadAsync());
        Assert.AreEqual("Python native supplementation failed.", error.Message);
        Assert.HasCount(2, fixture.Commands);
    }

    private sealed class Fixture : IDisposable
    {
        internal string Parent { get; } = Path.Combine(Path.GetTempPath(), "python-supplement-" +
            Guid.NewGuid().ToString("N"));
        internal string Root => Path.Combine(Parent, "endpoint");
        internal string Member => Path.Combine(Root, "member");
        private string Scratch => Path.Combine(Parent, "scratch");
        internal string RequestPath => Path.Combine(Scratch, "python-supplement-request.json");
        internal List<NativeCommand> Commands { get; } = [];
        internal Dictionary<string, string?> Environment { get; } = new(StringComparer.Ordinal)
        {
            ["UV_PROJECT_ENVIRONMENT"] = "owned-absent-environment",
            ["PIP_INDEX_URL"] = null,
        };
        internal NativeCommandResult? PreflightFailure { get; init; }
        internal NativeCommandResult? NativeFailure { get; init; }
        internal string? PreflightMode { get; init; }

        internal Fixture()
        {
            Directory.CreateDirectory(Member);
            Directory.CreateDirectory(Scratch);
            foreach (string directory in new[] { Parent, Root, Member })
                File.WriteAllText(Path.Combine(directory, "pyproject.toml"), "");
        }

        internal Task<PythonSupplementResult> ReadAsync() =>
            new PythonNativeSupplementReader(ExecuteAsync).ReadAsync(new(Root,
                Path.Combine(Parent, "python"), Path.Combine(Scratch, "cache"),
                [new(Root, null), new(Member, ["hatchling", "nbgv-python[build]"])],
                [new("quality", Root, ["example"], false)],
                [new("opaque-marker", "python_version >= '3.14'", ["build"])],
                [new("opaque-constraint", ">=3.14")]),
                "helper-python", "passive-script", "rust-supplement", Scratch, Environment,
                CancellationToken.None);

        private Task<NativeCommandResult> ExecuteAsync(NativeCommand command,
            CancellationToken token)
        {
            Commands.Add(command);
            if (command.Executable == "helper-python")
            {
                if (PreflightFailure is not null) return Task.FromResult(PreflightFailure);
                string[] inputs = command.Arguments.Skip(2)
                    .Select(directory => Path.Combine(directory, "pyproject.toml")).ToArray();
                inputs = PreflightMode switch
                {
                    "missing" => inputs[..^1],
                    "duplicate" => [.. inputs[..^1], inputs[0]],
                    "wrong" => [.. inputs[..^1], Path.Combine(Parent, "wrong")],
                    _ => inputs,
                };
                return Success(PreflightMode == "malformed" ? "private-malformed" :
                    JsonSerializer.Serialize(new { configuration_inputs = inputs }));
            }
            if (NativeFailure is not null) return Task.FromResult(NativeFailure);
            return Success(JsonSerializer.Serialize(new
            {
                workspace_root = Root,
                interpreter = Path.Combine(Parent, "python"),
                opaque = "opaque-native-id"
            }));
        }

        private static Task<NativeCommandResult> Success(string stdout) => Task.FromResult(
            new NativeCommandResult(NativeTermination.Exited, 0, stdout, "", 0, null));

        public void Dispose() => Directory.Delete(Parent, recursive: true);
    }
}
