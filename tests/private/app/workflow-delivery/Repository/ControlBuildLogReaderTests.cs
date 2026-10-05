using System.Collections;
using Microsoft.Build.Framework;
using TaskItem = Microsoft.Build.Utilities.TaskItem;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;
using GitFixture = WorkflowDelivery.Tests.Repository.GitReaderTests.GitFixture;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class ControlBuildLogReaderTests(TestContext context)
{
    private static readonly string Project = Path.GetFullPath("fixture/control.csproj");
    private static readonly string ToolProject = Path.GetFullPath("fixture/tool.proj");
    private static readonly string Definition = Path.GetFullPath("sdk/tasks.targets");
    private static readonly string Implementation = Path.GetFullPath("sdk/tasks.dll");
    private static readonly BuildEventContext ProjectContext = Context(1, task: -1, target: -1);
    private static readonly BuildEventContext CompilerContext = Context(1);

    [TestMethod]
    public void EvaluationPropertiesFollowTheActualCompilerContext()
    {
        var events = SuccessfulEvents();
        BuildEventContext rootEvaluation = new(1, 1, 10, -1, -2, -1, -1);
        BuildEventContext otherEvaluation = new(1, 1, 20, -1, -2, -1, -1);
        events.InsertRange(1,
        [
            new ProjectEvaluationStartedEventArgs("", "")
                { ProjectFile = ToolProject, BuildEventContext = otherEvaluation },
            new ProjectEvaluationFinishedEventArgs("", "")
            {
                ProjectFile = ToolProject, BuildEventContext = otherEvaluation,
                Properties = new Dictionary<string, string> { ["Configuration"] = "Release" },
            },
            new ProjectEvaluationStartedEventArgs("", "")
                { ProjectFile = Project, BuildEventContext = rootEvaluation },
            new ProjectEvaluationFinishedEventArgs("", "")
            {
                ProjectFile = Project, BuildEventContext = rootEvaluation,
                Properties = new Dictionary<string, string>
                {
                    ["MSBuildProjectFullPath"] = Project, ["Configuration"] = "Debug",
                    ["TargetFramework"] = "net10.0",
                },
            },
        ]);

        ControlBuildLog result = Read(events);

        Assert.AreEqual(Project, result.Properties["MSBuildProjectFullPath"]);
        Assert.AreEqual("Debug", result.Properties["Configuration"]);
        Assert.AreEqual("net10.0", result.Properties["TargetFramework"]);
    }

    [TestMethod]
    [DataRow("duplicate-property")]
    [DataRow("unsupported-property-item")]
    [DataRow("conflicting-project-property")]
    [DataRow("different-evaluation-project")]
    public void ConflictingEvaluationCannotSupplyCompilerContext(string defect)
    {
        var events = SuccessfulEvents();
        BuildEventContext evaluation = new(1, 1, 10, -1, -2, -1, -1);
        IEnumerable properties = defect switch
        {
            "duplicate-property" => new DictionaryEntry[]
            {
                new("Configuration", "Debug"), new("configuration", "Release"),
            },
            "unsupported-property-item" => new object[] { "Configuration=Debug" },
            _ => new Dictionary<string, string> { ["Configuration"] = "Debug" },
        };
        if (defect == "conflicting-project-property")
            events[1] = new ProjectStartedEventArgs("", "", Project, "Build",
                new Dictionary<string, string> { ["Configuration"] = "Release" },
                Array.Empty<object>())
            { BuildEventContext = ProjectContext };
        events.InsertRange(1,
        [
            new ProjectEvaluationStartedEventArgs("", "")
                { ProjectFile = Project, BuildEventContext = evaluation },
            new ProjectEvaluationFinishedEventArgs("", "")
            {
                ProjectFile = defect == "different-evaluation-project" ? ToolProject : Project,
                BuildEventContext = evaluation, Properties = properties,
            },
        ]);

        Assert.ThrowsExactly<InvalidDataException>(() => Read(events));
    }

    [TestMethod]
    public void TaskContextsKeepInputAndOutputRolesSeparate()
    {
        BuildEventContext toolProject = Context(2, task: -1, target: -1);
        BuildEventContext producer = Context(2);
        BuildEventArgs[] events =
        [
            new BuildStartedEventArgs("", ""), ProjectStart(Project, ProjectContext),
            ProjectStart(ToolProject, toolProject), TaskStart("Csc", Project, CompilerContext),
            TaskStart("WriteCodeFragment", ToolProject, producer),
            Parameter(CompilerContext, "Sources", "Sources", "root.cs"),
            Parameter(producer, "OutputFile", "Compile", "obj/generated.cs",
                TaskParameterMessageKind.TaskOutput),
            Parameter(producer, "OutputFile", "FileWrites", "obj/generated.cs",
                TaskParameterMessageKind.TaskOutput),
            TaskFinish("WriteCodeFragment", ToolProject, producer),
            TaskFinish("Csc", Project, CompilerContext), ProjectFinish(ToolProject, toolProject),
            ProjectFinish(Project, ProjectContext), new BuildFinishedEventArgs("", "", true),
        ];

        ControlBuildLog result = Read(events);

        ControlBuildTask compiler = Assert.ContainsSingle(result.Tasks.Where(t => t.Name == "Csc"));
        Assert.AreEqual(Project, compiler.Project);
        Assert.AreEqual(Definition, compiler.Definition);
        Assert.AreEqual(Implementation, compiler.Implementation);
        ControlBuildParameter input = Assert.ContainsSingle(compiler.Parameters);
        Assert.AreEqual(TaskParameterMessageKind.TaskInput, input.Kind);
        Assert.AreEqual("Sources", input.Name);
        Assert.AreEqual("Sources", input.ItemType);
        Assert.IsNull(input.PropertyName);
        Assert.AreEqual("root.cs", Assert.ContainsSingle(input.Values).Identity);
        Assert.AreEqual("fixture-metadata", input.Values[0].Metadata["LogicalName"]);
        ControlBuildTask generator = Assert.ContainsSingle(result.Tasks.Where(t =>
            t.Name == "WriteCodeFragment"));
        Assert.AreEqual(ToolProject, generator.Project);
        Assert.HasCount(2, generator.Parameters);
        Assert.AreEqual("OutputFile", generator.Parameters[0].Name);
        Assert.AreEqual("Compile", generator.Parameters[0].ItemType);
        Assert.AreEqual("FileWrites", generator.Parameters[1].ItemType);
        Assert.AreEqual(TaskParameterMessageKind.TaskOutput, generator.Parameters[0].Kind);
        Assert.AreEqual("obj/generated.cs", generator.Parameters[0].Values[0].Identity);
    }

    [TestMethod]
    public void ImportsRetainTheirNativeEvaluationProject()
    {
        BuildEventContext evaluation = new(1, 1, 10, -1, -2, -1, -1);
        string imported = Path.GetFullPath("shared/Directory.Build.props");
        var events = SuccessfulEvents();
        events.InsertRange(1,
        [
            new ProjectEvaluationStartedEventArgs("", "")
                { ProjectFile = Project, BuildEventContext = evaluation },
            new ProjectImportedEventArgs
            {
                ProjectFile = Path.GetFullPath("shared/other.targets"),
                ImportedProjectFile = imported, BuildEventContext = evaluation,
            },
            new ProjectImportedEventArgs
            {
                ProjectFile = Definition, UnexpandedProject = "$(OptionalImport)",
                BuildEventContext = evaluation,
            },
            new ProjectEvaluationFinishedEventArgs("", "")
                { ProjectFile = Project, BuildEventContext = evaluation },
        ]);

        ControlBuildLog result = Read(events);

        ControlBuildImport import = Assert.ContainsSingle(result.Imports);
        Assert.AreEqual(Project, import.Project);
        Assert.AreEqual(imported, import.Path);
    }

    [TestMethod]
    [DataRow("missing-build-start")]
    [DataRow("missing-build-finish")]
    [DataRow("failed-build")]
    [DataRow("missing-project-finish")]
    [DataRow("failed-project")]
    [DataRow("missing-task-finish")]
    [DataRow("failed-task")]
    [DataRow("missing-task-start")]
    [DataRow("conflicting-task-start")]
    [DataRow("wrong-task-finish")]
    [DataRow("wrong-project-finish")]
    [DataRow("wrong-parameter-context")]
    [DataRow("duplicate-input")]
    [DataRow("missing-parameter-role")]
    [DataRow("missing-source-values")]
    [DataRow("missing-compiler")]
    [DataRow("missing-task-implementation")]
    [DataRow("unbound-import")]
    [DataRow("unfinished-evaluation")]
    public void IncompleteOrConflictingOperationCannotProduceFacts(string defect)
    {
        var events = SuccessfulEvents();
        switch (defect)
        {
            case "missing-build-start": events.RemoveAt(0); break;
            case "missing-build-finish": events.RemoveAt(events.Count - 1); break;
            case "failed-build": events[^1] = new BuildFinishedEventArgs("", "", false); break;
            case "missing-project-finish": events.RemoveAt(5); break;
            case "failed-project": events[5] = ProjectFinish(Project, ProjectContext, false); break;
            case "missing-task-finish": events.RemoveAt(4); break;
            case "failed-task":
                events[4] = TaskFinish("Csc", Project, CompilerContext, false); break;
            case "missing-task-start": events.RemoveAt(2); break;
            case "conflicting-task-start": events.Insert(3, events[2]); break;
            case "wrong-task-finish":
                events[4] = TaskFinish("Other", Project, CompilerContext); break;
            case "wrong-project-finish":
                events[5] = ProjectFinish(ToolProject, ProjectContext); break;
            case "wrong-parameter-context":
                events[3] = Parameter(Context(2), "Sources", "Sources", "root.cs"); break;
            case "duplicate-input": events.Insert(4, events[3]); break;
            case "missing-parameter-role":
                events[3] = Parameter(CompilerContext, "", "Sources", "root.cs"); break;
            case "missing-source-values":
                events[3] = new TaskParameterEventArgs(TaskParameterMessageKind.TaskInput,
                    "Sources", null, "Sources", Array.Empty<ITaskItem>(), true, DateTime.UtcNow)
                { BuildEventContext = CompilerContext }; break;
            case "missing-compiler":
                events.RemoveRange(2, 3); break;
            case "missing-task-implementation":
                events[2] = new TaskStartedEventArgs("", "", Project, Definition, "Csc")
                { BuildEventContext = CompilerContext }; break;
            case "unbound-import":
                events.Insert(1, new ProjectImportedEventArgs
                {
                    ImportedProjectFile = Definition,
                    BuildEventContext = ProjectContext,
                }); break;
            case "unfinished-evaluation":
                events.Insert(1, new ProjectEvaluationStartedEventArgs("", "")
                {
                    ProjectFile = Project,
                    BuildEventContext = new(1, 1, 10, -1, -2, -1, -1),
                }); break;
            default: Assert.Fail("Unknown fixture defect."); break;
        }

        Assert.ThrowsExactly<InvalidDataException>(() => Read(events));
    }

    [TestMethod]
    public async Task CancellationStopsReplayBeforeFacts()
    {
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(
            context.CancellationToken);
        await cancelled.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => Task.Run(() =>
            ControlBuildLogReader.ReadEvents(SuccessfulEvents(), Project, cancelled.Token),
            context.CancellationToken));
    }

    [TestMethod]
    public async Task NativeReplayPreservesCompilerAndGeneratorRoles()
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        await repo.SetAsync("Control.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <OutputType>Exe</OutputType>
                <ImplicitUsings>enable</ImplicitUsings>
              </PropertyGroup>
            </Project>
            """);
        await repo.SetAsync("Program.cs", "System.Console.WriteLine(\"fixture\");\n");
        await repo.CommitAsync();
        await repo.GitAsync("reset", "--hard", "HEAD");
        string results = context.TestResultsDirectory!;
        Directory.CreateDirectory(results);
        string binlog = Path.Combine(results, "native-control-build-" + Guid.NewGuid().ToString("N")
            + ".binlog");
        await NativeProcess.RunAsync("dotnet", repo.Directory,
            ["build", "Control.csproj", "-nologo", "-property:MSBuildLogVerboseTaskParameters=true",
                "-property:ContinuousIntegrationBuild=true", "-property:RestoreLockedMode=true",
                "-bl:" + binlog], context.CancellationToken);

        ControlBuildLog result = ControlBuildLogReader.Read(binlog,
            Path.Combine(repo.Directory, "Control.csproj"), context.CancellationToken);
        GitRevision revision = await new GitReader(repo.Directory).ReadAsync("HEAD",
            context.CancellationToken);
        GitMaterialization checkout = await GitMaterialization.BindAsync(repo.Directory, revision,
            context.CancellationToken);
        ControlBuildContext operation = await new ControlBuildContextReader(checkout).ReadAsync(
            "Control.csproj", context.CancellationToken);

        Assert.AreEqual(Path.Combine(repo.Directory, "Control.csproj"), result.Project);
        Assert.AreEqual(result.Project, operation.Project);
        Assert.AreEqual(new("Debug", "net10.0"), operation.Dimension);
        Assert.AreEqual(revision.Commit, operation.Revision);
        Assert.IsNotEmpty(operation.SdkVersion);
        Assert.IsNotEmpty(operation.EditorConfigCandidates);
        ControlBuildTask compiler = Assert.ContainsSingle(result.Tasks.Where(t => t.Name == "Csc"));
        ControlBuildParameter sources = Assert.ContainsSingle(compiler.Parameters.Where(p =>
            p.Kind == TaskParameterMessageKind.TaskInput && p.Name == "Sources"));
        Assert.Contains("Program.cs", sources.Values.Select(value => value.Identity).ToArray());
        Assert.IsNotEmpty(compiler.Parameters.Where(p => p.Name == "References").ToArray());
        ControlBuildTask generator = Assert.ContainsSingle(result.Tasks.Where(t =>
            t.Name == "WriteCodeFragment"));
        Assert.IsNotEmpty(generator.Parameters.Where(p =>
            p.Kind == TaskParameterMessageKind.TaskOutput && p.Name == "OutputFile" &&
            p.ItemType == "Compile").ToArray());
        Assert.IsNotEmpty(result.Imports);
        Assert.IsNotEmpty(compiler.Implementation);
        Assert.IsTrue(File.Exists(binlog));
        operation.RequireSameOperation(result);
        Assert.AreEqual(operation.SdkVersion, result.Properties["NETCoreSdkVersion"]);
        Assert.AreEqual(repo.Directory, result.Properties["MSBuildStartupDirectory"]);
    }

    private ControlBuildLog Read(IEnumerable<BuildEventArgs> events) =>
        ControlBuildLogReader.ReadEvents(events, Project, context.CancellationToken);

    private static BuildEventContext Context(int node, int task = 1, int target = 1) =>
        new(1, node, 10, 1, 1, target, task);

    private static ProjectStartedEventArgs ProjectStart(string project, BuildEventContext id) =>
        new("", "", project, "Build", Array.Empty<object>(), Array.Empty<object>())
        { BuildEventContext = id };

    private static ProjectFinishedEventArgs ProjectFinish(string project, BuildEventContext id,
        bool succeeded = true) => new("", "", project, succeeded) { BuildEventContext = id };

    private static TaskStartedEventArgs TaskStart(string name, string project,
        BuildEventContext id) =>
        new("", "", project, Definition, name, Implementation) { BuildEventContext = id };

    private static TaskFinishedEventArgs TaskFinish(string name, string project,
        BuildEventContext id,
        bool succeeded = true) => new("", "", project, Definition, name, succeeded)
        { BuildEventContext = id };

    private static TaskParameterEventArgs Parameter(BuildEventContext id, string name,
        string itemType,
        string identity, TaskParameterMessageKind kind = TaskParameterMessageKind.TaskInput) =>
        new(kind, name, null, itemType, new ITaskItem[]
        {
            new TaskItem(identity,
                new Dictionary<string, string> { ["LogicalName"] = "fixture-metadata" }),
        }, true, DateTime.UtcNow)
        { BuildEventContext = id };

    private static List<BuildEventArgs> SuccessfulEvents() =>
    [
        new BuildStartedEventArgs("", ""), ProjectStart(Project, ProjectContext),
        TaskStart("Csc", Project, CompilerContext),
        Parameter(CompilerContext, "Sources", "Sources", "root.cs"),
        TaskFinish("Csc", Project, CompilerContext), ProjectFinish(Project, ProjectContext),
        new BuildFinishedEventArgs("", "", true),
    ];
}
