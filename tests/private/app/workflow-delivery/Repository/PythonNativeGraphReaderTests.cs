using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class PythonNativeGraphReaderTests(TestContext context)
{
    private static readonly string[] BuildStrings =
        ["producer[feature]; python_version >= '3.13'"];
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
        CollectionAssert.AreEqual(BuildStrings,
            consumer.Project.BuildRequirements);
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
        Assert.HasCount(2, fixture.Requests);
        PythonSupplementRequest bootstrap = fixture.Requests[0];
        Assert.IsEmpty(bootstrap.Markers);
        Assert.IsEmpty(bootstrap.GroupOperations);
        Assert.IsEmpty(bootstrap.PythonConstraints);
        PythonSupplementRequest activity = fixture.Requests[1];
        Assert.AreEqual(fixture.Request.Interpreter, activity.Interpreter);
        Assert.AreEqual(fixture.Request.Cache, activity.Cache);
        Assert.HasCount(fixture.Operations.Length, activity.GroupOperations);
        for (int index = 0; index < activity.GroupOperations.Length; index++)
        {
            PythonGroupOperation expected = fixture.Operations[index];
            PythonGroupOperation actual = activity.GroupOperations[index];
            Assert.AreEqual(expected.Id, actual.Id);
            Assert.AreEqual(expected.Directory, actual.Directory);
            Assert.AreEqual(expected.NoDev, actual.NoDev);
            CollectionAssert.AreEqual(expected.Packages, actual.Packages);
        }
        CollectionAssert.AreEquivalent(new[] { fixture.Root, fixture.Consumer,
            fixture.Producer }, activity.Projects.Select(item => item.Directory).ToArray());
        Assert.ContainsSingle(activity.Markers.Where(item => item.Extras.Length == 1 &&
            item.Extras[0] == "feature"));
        Assert.ContainsSingle(activity.Markers.Where(item => item.Expression ==
            "python_version >= '3.13'" && item.Extras.Length == 0));
        foreach (NativeCommand command in fixture.Commands)
        {
            Assert.IsNotNull(command.Environment);
            CollectionAssert.AreEquivalent(fixture.Environment.ToArray(),
                command.Environment.ToArray());
        }
        CollectionAssert.AreEqual(new[]
        {
            Path.Combine(fixture.Bootstrap, "python-supplement-request.json"),
            Path.Combine(fixture.Activity, "python-supplement-request.json")
        }, fixture.Commands.Where(command => command.Executable == "native-supplement")
            .Select(command => command.Arguments.Single()).ToArray());
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
    [DataRow(false, true, true)]
    [DataRow(true, false, true)]
    [DataRow(true, true, false)]
    public async Task NativeFalseAnswersExcludeOnlyTheirDependencyLeg(bool ordinary,
        bool build, bool supportsPython)
    {
        using var fixture = new Fixture
        {
            OrdinaryActive = ordinary,
            BuildActive = build,
            SupportsPython = supportsPython
        };
        if (!supportsPython)
        {
            await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
                fixture.ReadAsync(context.CancellationToken));
            Assert.HasCount(2, fixture.Requests);
            return;
        }
        PythonNativeGraph graph = await fixture.ReadAsync(context.CancellationToken);
        PythonProjectDependencies consumer = graph.Projects.Single(item =>
            item.Directory == "src/consumer");
        CollectionAssert.AreEqual(ordinary ? ProducerPaths : [], consumer.Ordinary);
        CollectionAssert.AreEqual(build ? ProducerPaths : [], consumer.Build);
        Assert.HasCount(3, graph.Projects);
    }

    [TestMethod]
    public async Task ScratchCollisionFailsBeforeNativeWork()
    {
        using var fixture = new Fixture();
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => fixture.ReadAsync(
            context.CancellationToken,
            fixture.Request with { ActivityScratch = fixture.Bootstrap }));
        Assert.IsEmpty(fixture.Commands);
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(5)]
    [DataRow(6)]
    [DataRow(7)]
    [DataRow(8)]
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
    [DataRow(6)]
    [DataRow(7)]
    [DataRow(8)]
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
    [DataRow(6)]
    [DataRow(7)]
    [DataRow(8)]
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
        Assert.HasCount(stage < 6 ? 0 : stage < 8 ? 1 : 2, fixture.Requests);
        Assert.AreEqual(stage == 1 ? 0 : 1, fixture.FactoryCalls);
    }

    [TestMethod]
    [DataRow("bootstrap-members")]
    [DataRow("bootstrap-builds")]
    [DataRow("activity-markers")]
    [DataRow("activity-groups")]
    [DataRow("activity-root")]
    [DataRow("activity-interpreter")]
    public async Task ChangedNativeAnswersCannotReturnGraph(string fault)
    {
        using var fixture = new Fixture { Fault = fault };
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.ReadAsync(context.CancellationToken));
        Assert.HasCount(fault.StartsWith("bootstrap", StringComparison.Ordinal) ? 1 : 2,
            fixture.Requests);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ChangedOperationContextsFailBeforeActivityQuery(bool outside)
    {
        using var fixture = new Fixture();
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            fixture.ReadAsync(context.CancellationToken, createOperations: metadata =>
            {
                PythonGroupOperation[] operations = CreateOperations(metadata);
                return outside ? [.. operations, new("outside", fixture.Parent, [], false)] :
                    operations[1..];
            }));
        Assert.HasCount(1, fixture.Requests);
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
        AssertOperations(expected, fixture.Requests[1].GroupOperations);
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
        Assert.IsEmpty(fixture.Requests);
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
        Assert.IsEmpty(fixture.Requests);
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
        Assert.IsEmpty(fixture.Requests);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ScratchAliasesFailBeforeNativeWork(bool trailingSeparator)
    {
        using var fixture = new Fixture();
        string alias = trailingSeparator ? fixture.Bootstrap + Path.DirectorySeparatorChar :
            Path.Combine(fixture.Bootstrap, ".");
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => fixture.ReadAsync(
            context.CancellationToken, fixture.Request with { ActivityScratch = alias }));
        Assert.AreEqual(0, fixture.FactoryCalls);
        Assert.IsEmpty(fixture.Commands);
    }

    [TestMethod]
    public async Task CaseOnlyScratchAliasUsesPlatformSemantics()
    {
        using var fixture = new Fixture();
        string alias = Path.Combine(fixture.Parent, "BOOTSTRAP");
        Directory.CreateDirectory(alias);
        PythonGraphRequest request = fixture.Request with { ActivityScratch = alias };
        if (OperatingSystem.IsWindows())
        {
            await Assert.ThrowsExactlyAsync<InvalidDataException>(() => fixture.ReadAsync(
                context.CancellationToken, request));
            Assert.AreEqual(0, fixture.FactoryCalls);
            Assert.IsEmpty(fixture.Commands);
        }
        else
        {
            PythonNativeGraph graph = await fixture.ReadAsync(context.CancellationToken, request);
            Assert.HasCount(3, graph.Projects);
            Assert.HasCount(2, fixture.Requests);
        }
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
        internal string Bootstrap => Path.Combine(Parent, "bootstrap");
        internal string Activity => Path.Combine(Parent, "activity");
        internal List<NativeCommand> Commands { get; } = [];
        internal List<PythonSupplementRequest> Requests { get; } = [];
        internal Dictionary<string, string?> Environment { get; } = new(StringComparer.Ordinal)
        {
            ["UV_PYTHON_DOWNLOADS"] = "never",
            ["PIP_INDEX_URL"] = null
        };
        internal bool NamedRoot { get; init; }
        internal bool InputOnlyMember { get; init; }
        internal bool OrdinaryActive { get; init; } = true;
        internal bool BuildActive { get; init; } = true;
        internal bool SupportsPython { get; init; } = true;
        internal int FailAt { get; init; }
        internal int CancelAt { get; init; }
        internal bool ReturnCancelledResult { get; init; }
        internal CancellationTokenSource? Cancellation { get; init; }
        internal string? Fault { get; init; }
        private PythonGraphRequest? request;
        internal PythonGraphRequest Request => request ??= new(Root, "uv",
            Path.Combine(Parent, "python"),
            "helper-python", "passive-script", "native-supplement", Path.Combine(Parent, "cache"),
            Bootstrap, Activity);
        internal PythonGroupOperation[] Operations { get; private set; } = [];
        internal PythonMetadata? Metadata { get; private set; }
        internal int FactoryCalls { get; private set; }

        internal Fixture()
        {
            foreach (string directory in new[] { Parent, Root, Consumer, Producer,
                Bootstrap, Activity, InputOnly }) Directory.CreateDirectory(directory);
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
                    build_requirements = directory == Consumer
                        ? BuildStrings : null,
                    nbgv = directory == Consumer ? new
                    {
                        working_directory = Root,
                        generated_file = Path.Combine(Consumer, "generated", "version.py")
                    } : null
                });
            }
            PythonSupplementRequest request = JsonSerializer.Deserialize(
                File.ReadAllText(command.Arguments.Single()),
                PythonSupplementJson.Default.PythonSupplementRequest)!;
            Requests.Add(request);
            bool bootstrap = Requests.Count == 1;
            JsonNode response = JsonSerializer.SerializeToNode(new
            {
                workspace_root = Root,
                interpreter = Request.Interpreter,
                members = new[]
                {
                    new { directory = Consumer, name = "consumer" },
                    new { directory = Producer, name = "producer" }
                }.Concat(NamedRoot ? [new { directory = Root, name = "root-package" }] : [])
                    .Concat(InputOnlyMember ? [new { directory = InputOnly,
                        name = "input-only" }] : []),
                build_requirements = new[]
                {
                    new { directory = Consumer, requirements = new[]
                    {
                        new { name = "producer", editable = Producer, extras = Feature,
                            marker = "python_version >= '3.13'" }
                    } }
                },
                group_operations = request.GroupOperations.Select(operation => new
                { id = operation.Id, groups = Array.Empty<string>() }),
                markers = request.Markers.Select(marker => new
                {
                    id = marker.Id,
                    active = marker.Expression == "python_version >= '3.13'"
                        ? BuildActive : OrdinaryActive
                }),
                python_constraints = request.PythonConstraints.Select(constraint => new
                { id = constraint.Id, active = SupportsPython })
            })!;
            if (bootstrap && Fault == "bootstrap-members") response["members"] = new JsonArray();
            if (bootstrap && Fault == "bootstrap-builds")
                response["build_requirements"] = new JsonArray();
            if (!bootstrap)
            {
                if (Fault == "activity-markers") response["markers"] = new JsonArray();
                if (Fault == "activity-groups") response["group_operations"] = new JsonArray();
                if (Fault == "activity-root") response["workspace_root"] = Parent;
                if (Fault == "activity-interpreter") response["interpreter"] = "foreign-python";
            }
            return response.ToJsonString();
        }

        public void Dispose() => Directory.Delete(Parent, recursive: true);
    }
}
