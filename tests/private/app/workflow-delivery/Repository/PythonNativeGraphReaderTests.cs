using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class PythonNativeGraphReaderTests(TestContext context)
{
    private static readonly PythonBuildRequirement[] BuildRequirements =
        [new("producer", ["feature"]), new("hatchling", [])];
    private static readonly string[] ProducerPaths = ["src/plugin"];
    private static readonly string[] Feature = ["feature"];
    private static readonly string[] BaseDirectories = [".", "src/consumer", "src/plugin"];
    private static readonly string[] InputOnlyDirectories =
        [".", "src/consumer", "src/plugin", "src/input-only"];

    [TestMethod]
    public async Task CollectsNativeGraphAndRetainsPassiveInputs()
    {
        using var fixture = new Fixture();
        PythonNativeGraph graph = await fixture.ReadAsync(context.CancellationToken);

        Assert.AreEqual(fixture.Root, graph.Metadata.Root);
        Assert.AreEqual(fixture.Request.Interpreter, graph.Interpreter);
        AssertOperations(fixture.Operations, graph.Operations);
        Assert.HasCount(3, graph.Passive);
        Assert.IsNull(graph.Passive.Single(input => input.Project.Directory ==
            fixture.Root).Project.BuildRequirements);
        PythonPassiveInputs consumer = graph.Passive.Single(input =>
            input.Project.Directory == fixture.Consumer);
        Assert.AreEqual(BuildRequirements.Length, consumer.Project.BuildRequirements!.Length);
        Assert.AreEqual("workspace", consumer.Project.Sources!["producer"]);
        Assert.AreEqual(new PythonNbgvCoordinates(fixture.Root,
            Path.Combine(fixture.Consumer, "generated", "version.py")), consumer.Nbgv);
        Assert.HasCount(3, graph.Projects);
        PythonProjectDependencies project = graph.Projects.Single(item =>
            item.Directory == "src/consumer");
        CollectionAssert.AreEqual(ProducerPaths, project.Ordinary);
        CollectionAssert.AreEqual(ProducerPaths, project.Build);
        Assert.IsEmpty(graph.Projects.Single(item => item.Directory == ".").Build);
        CollectionAssert.AreEquivalent(new[] { fixture.Parent, fixture.Root, fixture.Consumer,
            fixture.Producer }.Select(dir => Path.Combine(dir, "pyproject.toml")).ToArray(),
            graph.ConfigurationInputs);
        foreach (NativeCommand command in fixture.Commands)
        {
            Assert.IsNotNull(command.Environment);
            CollectionAssert.AreEquivalent(fixture.Environment.ToArray(),
                command.Environment.ToArray());
        }
        CollectionAssert.AreEqual(new[] { "passive-script", "--configuration-only",
            fixture.Parent, fixture.Root, fixture.Consumer, fixture.Producer },
            fixture.Commands[1].Arguments);
        Assert.HasCount(5, fixture.Commands);
        Assert.AreEqual(fixture.Root, fixture.Commands[0].Directory);
        CollectionAssert.AreEqual(new[] { "workspace", "metadata", "--frozen", "--python",
            fixture.Request.Interpreter }, fixture.Commands[0].Arguments);
    }

    [TestMethod]
    public async Task NamedRootMemberIsReadOnce()
    {
        using var fixture = new Fixture { NamedRoot = true };
        PythonNativeGraph graph = await fixture.ReadAsync(context.CancellationToken);

        Assert.HasCount(3, graph.Metadata.Members);
        Assert.HasCount(3, graph.Passive);
        Assert.ContainsSingle(fixture.Commands.Where(command =>
            command.Executable == "helper-python" && command.Arguments.Length == 2 &&
            command.Arguments[1] == fixture.Root));
        Assert.AreEqual("root-package", graph.Projects.Single(item =>
            item.Directory == ".").Name);
    }

    [TestMethod]
    public async Task UnsupportedBuildSourceFailsTheGraph()
    {
        using var fixture = new Fixture { UnsupportedSource = true };
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.ReadAsync(context.CancellationToken));
        Assert.HasCount(5, fixture.Commands);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ProjectUvTomlFailsBeforePassiveExtraction(bool ancestor)
    {
        using var fixture = new Fixture();
        File.WriteAllText(Path.Combine(ancestor ? fixture.Parent : fixture.Root, "uv.toml"), "");
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.ReadAsync(context.CancellationToken));
        Assert.HasCount(1, fixture.Commands);
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(5)]
    public async Task FailedStageStopsCollection(int stage)
    {
        using var fixture = new Fixture { FailAt = stage };
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.ReadAsync(context.CancellationToken));
        Assert.HasCount(stage, fixture.Commands);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(5)]
    public async Task CancellationStopsBeforeFollowingStage(int stage)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            context.CancellationToken);
        using var fixture = new Fixture { CancelAt = stage, Cancellation = cancellation };
        if (stage == 0) cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            fixture.ReadAsync(cancellation.Token));
        Assert.HasCount(stage, fixture.Commands);
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(5)]
    public async Task CancelledNativeResultStopsBeforeFollowingStage(int stage)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            context.CancellationToken);
        using var fixture = new Fixture
        {
            CancelAt = stage,
            Cancellation = cancellation,
            ReturnCancelledResult = true
        };
        OperationCanceledException failure = await Assert.ThrowsAsync<OperationCanceledException>(
            () => fixture.ReadAsync(cancellation.Token));
        Assert.AreEqual(cancellation.Token, failure.CancellationToken);
        Assert.HasCount(stage, fixture.Commands);
        Assert.AreEqual(stage == 1 ? 0 : 1, fixture.FactoryCalls);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ChangedOperationContextsFailGroupSelection(bool outside)
    {
        using var fixture = new Fixture();
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.ReadAsync(context.CancellationToken, createOperations: metadata =>
            {
                PythonGroupOperation[] operations = CreateOperations(metadata);
                return outside ? [.. operations, new("outside", fixture.Parent, [], false)] :
                    operations[1..];
            }));
        Assert.HasCount(5, fixture.Commands);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task OperationFactoryUsesSameMetadataAndNativeContexts(bool namedRoot)
    {
        using var fixture = new Fixture { NamedRoot = namedRoot };
        PythonMetadata? seen = null;
        PythonGroupOperation[] expected = [];
        PythonNativeGraph graph = await fixture.ReadAsync(context.CancellationToken,
            createOperations: metadata =>
            {
                seen = metadata;
                expected = [.. CreateOperations(metadata),
                    new("selected-consumer", metadata.Root, ["consumer"], true)];
                return expected;
            });

        Assert.AreSame(seen, graph.Metadata);
        Assert.AreEqual(1, fixture.FactoryCalls);
        Assert.ContainsSingle(fixture.Commands.Where(command => command.Executable == "uv"));
        AssertOperations(expected, graph.Operations);
        CollectionAssert.AreEquivalent(new[] { fixture.Root, fixture.Consumer,
            fixture.Producer }, graph.Operations.Select(operation => operation.Directory)
                .Distinct(StringComparer.Ordinal).ToArray());
        Assert.ContainsSingle(graph.Operations.Where(operation => operation.Id == "."));
        Assert.AreEqual(namedRoot ? 3 : 2, graph.Metadata.Members.Length);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ChangedInputOnlyMembershipChangesFactoryContexts(bool inputOnlyMember)
    {
        using var fixture = new Fixture { InputOnlyMember = inputOnlyMember };
        PythonNativeGraph graph = await fixture.ReadAsync(context.CancellationToken,
            createOperations: CreateOperations);
        string[] expected = inputOnlyMember ? [fixture.Root, fixture.Consumer,
            fixture.Producer, fixture.InputOnly] : [fixture.Root, fixture.Consumer,
            fixture.Producer];
        CollectionAssert.AreEquivalent(expected,
            graph.Operations.Select(operation => operation.Directory).ToArray());
        CollectionAssert.AreEquivalent(inputOnlyMember ? InputOnlyDirectories : BaseDirectories,
            graph.Projects.Select(project => project.Directory).ToArray());
        Assert.AreEqual(1, fixture.FactoryCalls);
        Assert.ContainsSingle(fixture.Commands.Where(command => command.Executable == "uv"));
        if (inputOnlyMember)
        {
            Assert.IsNull(graph.Passive.Single(input => input.Project.Directory ==
                fixture.InputOnly).Project.BuildRequirements);
            Assert.IsEmpty(graph.Projects.Single(project =>
                project.Directory == "src/input-only").Build);
        }
    }

    [TestMethod]
    public async Task FailedOperationFactoryStopsBeforePassiveExtraction()
    {
        using var fixture = new Fixture();
        var failure = new InvalidOperationException("Caller operation construction failed.");
        InvalidOperationException actual =
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.ReadAsync(
                context.CancellationToken, createOperations: _ => throw failure));
        Assert.AreSame(failure, actual);
        Assert.AreEqual(1, fixture.FactoryCalls);
        Assert.HasCount(1, fixture.Commands);
    }

    [TestMethod]
    public async Task CancelledOperationFactoryStopsBeforePassiveExtraction()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            context.CancellationToken);
        using var fixture = new Fixture();
        await Assert.ThrowsAsync<OperationCanceledException>(() => fixture.ReadAsync(
            cancellation.Token, createOperations: metadata =>
            {
                cancellation.Cancel();
                return CreateOperations(metadata);
            }));
        Assert.AreEqual(1, fixture.FactoryCalls);
        Assert.HasCount(1, fixture.Commands);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    public async Task CancellationBeforeOperationFactoryDoesNotInvokeIt(int stage)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            context.CancellationToken);
        using var fixture = new Fixture { CancelAt = stage, Cancellation = cancellation };
        if (stage == 0) cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => fixture.ReadAsync(
            cancellation.Token));
        Assert.AreEqual(0, fixture.FactoryCalls);
        Assert.HasCount(stage, fixture.Commands);
    }

    private static PythonGroupOperation[] CreateOperations(PythonMetadata metadata) =>
        metadata.Members.Select(member => member.Directory).Append(".")
            .Distinct(StringComparer.Ordinal).Select(directory => new PythonGroupOperation(
                directory, Path.GetFullPath(Path.Combine(metadata.Root, directory)), [], false))
            .ToArray();

    private static void AssertOperations(PythonGroupOperation[] expected,
        PythonGroupOperation[] actual)
    {
        Assert.HasCount(expected.Length, actual);
        for (int index = 0; index < expected.Length; index++)
        {
            Assert.AreEqual(expected[index].Id, actual[index].Id);
            Assert.AreEqual(expected[index].Directory, actual[index].Directory);
            Assert.AreEqual(expected[index].NoDev, actual[index].NoDev);
            CollectionAssert.AreEqual(expected[index].Packages, actual[index].Packages);
        }
    }

    private sealed class Fixture : IDisposable
    {
        internal string Parent { get; } = Path.Combine(Path.GetTempPath(),
            "workflow-python-graph-" + Guid.NewGuid().ToString("N"));
        internal string Root => Path.Combine(Parent, "checkout");
        internal string Consumer => Path.Combine(Root, "src", "consumer");
        internal string Producer => Path.Combine(Root, "src", "plugin");
        internal string InputOnly => Path.Combine(Root, "src", "input-only");
        internal List<NativeCommand> Commands { get; } = [];
        internal Dictionary<string, string?> Environment { get; } = new(StringComparer.Ordinal)
        {
            ["UV_PYTHON_DOWNLOADS"] = "never",
            ["PIP_INDEX_URL"] = null
        };
        internal bool NamedRoot { get; init; }
        internal bool InputOnlyMember { get; init; }
        internal bool UnsupportedSource { get; init; }
        internal int FailAt { get; init; }
        internal int CancelAt { get; init; }
        internal bool ReturnCancelledResult { get; init; }
        internal CancellationTokenSource? Cancellation { get; init; }
        private PythonGraphRequest? request;
        internal PythonGraphRequest Request => request ??= new(Root, "uv",
            Path.Combine(Parent, "python"), "helper-python", "passive-script");
        internal PythonGroupOperation[] Operations { get; private set; } = [];
        internal PythonMetadata? Metadata { get; private set; }
        internal int FactoryCalls { get; private set; }

        internal Fixture()
        {
            foreach (string directory in new[] { Parent, Root, Consumer, Producer, InputOnly })
                Directory.CreateDirectory(directory);
            foreach (string directory in new[] { Parent, Root, Consumer, Producer, InputOnly })
                File.WriteAllText(Path.Combine(directory, "pyproject.toml"), "");
        }

        internal Task<PythonNativeGraph> ReadAsync(CancellationToken token,
            PythonGraphRequest? request = null,
            Func<PythonMetadata, PythonGroupOperation[]>? createOperations = null) =>
            new PythonNativeGraphReader(ExecuteAsync).ReadAsync(request ?? Request, metadata =>
            {
                Metadata = metadata;
                FactoryCalls++;
                Operations = createOperations is not null ? createOperations(metadata) :
                    metadata.Members.Select(member => member.Directory).Append(".")
                        .Distinct(StringComparer.Ordinal).Select(directory =>
                            new PythonGroupOperation(directory,
                                Path.GetFullPath(Path.Combine(metadata.Root, directory)), [],
                                directory == "src/plugin")).ToArray();
                return Operations;
            }, Environment, token);

        private Task<NativeCommandResult> ExecuteAsync(NativeCommand command,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Commands.Add(command);
            NativeCommandResult result = new(NativeTermination.Exited, 0, Output(command),
                "", 0, null);
            if (Commands.Count == FailAt) result = result with { ExitCode = 1 };
            if (Commands.Count == CancelAt)
            {
                Cancellation!.Cancel();
                if (ReturnCancelledResult)
                    result = new(NativeTermination.Cancelled, null, "", "", 0, null);
            }
            return Task.FromResult(result);
        }

        private string Output(NativeCommand command)
        {
            if (command.Executable == "uv")
            {
                JsonNode metadata = PythonMetadataReaderTests.Fixture(Root);
                metadata["resolution"]!["native extra id"]!["dependencies"]![0]!["marker"] =
                    "extra == 'feature'";
                if (NamedRoot)
                {
                    metadata["members"]!.AsArray().Add(JsonSerializer.SerializeToNode(new
                    { name = "root-package", path = Root, id = "root-package-id" }));
                    metadata["resolution"]!["root-package-id"] = JsonSerializer.SerializeToNode(new
                    {
                        kind = "package",
                        name = "root-package",
                        source = new { @virtual = Root },
                        dependencies = Array.Empty<object>()
                    });
                }
                if (InputOnlyMember)
                {
                    metadata["members"]!.AsArray().Add(JsonSerializer.SerializeToNode(new
                    { name = "input-only", path = InputOnly, id = "input-only-id" }));
                    metadata["resolution"]!["input-only-id"] = JsonSerializer.SerializeToNode(new
                    {
                        kind = "package",
                        name = "input-only",
                        source = new { @virtual = InputOnly },
                        dependencies = Array.Empty<object>()
                    });
                }
                return metadata.ToJsonString();
            }
            if (command.Executable == "helper-python")
            {
                if (command.Arguments.Contains("--configuration-only"))
                    return JsonSerializer.Serialize(new
                    {
                        configuration_inputs =
                        command.Arguments.Skip(2).Select(directory =>
                            Path.Combine(directory, "pyproject.toml"))
                    });
                string directory = command.Arguments[1];
                return JsonSerializer.Serialize(new
                {
                    directory,
                    build_requirements = directory == Consumer ? BuildRequirements.Select(
                        requirement => new { name = requirement.Name,
                            extras = requirement.Extras }).ToArray() : null,
                    sources = directory == Consumer ? new Dictionary<string, string>
                    {
                        ["producer"] = UnsupportedSource ? "unsupported" : "workspace"
                    } : [],
                    nbgv = directory == Consumer ? new
                    {
                        working_directory = Root,
                        generated_file = Path.Combine(Consumer, "generated", "version.py")
                    } : null
                });
            }
            throw new AssertFailedException("Unexpected native command.");
        }

        public void Dispose() => Directory.Delete(Parent, recursive: true);
    }
}
