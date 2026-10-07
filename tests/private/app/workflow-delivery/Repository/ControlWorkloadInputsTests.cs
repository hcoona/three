using Microsoft.Build.Framework;
using NuGet.Frameworks;
using NuGet.ProjectModel;
using WorkflowDelivery.Repository;
using GitFixture = WorkflowDelivery.Tests.Repository.GitReaderTests.GitFixture;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class ControlWorkloadInputsTests(TestContext context)
{
    private const string WorkloadDependency = "selected SDK workload autoimport";
    private static readonly ControlBuildEvaluation Evaluation = new(1, 1, 10);

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public async Task ConnectedWorkloadImportsProduceExactDependenciesAndConsumption(
        bool reverseImports, bool multiplePacks)
    {
        using Fixture fixture = await CreateAsync();
        string[] packs = multiplePacks
            ? [fixture.ToolPath("packs/First/1.0"), fixture.ToolPath("packs/Second/2.0")]
            : [fixture.ToolPath("packs/First/1.0")];
        var imports = new List<ControlBuildImport>();
        var expected = new Dictionary<string, string[]>();
        foreach (string pack in packs)
        {
            ControlBuildImport seed = fixture.Seed(pack);
            string child = Fixture.FileAt(Path.Combine(pack, "targets", "First.props"));
            string transitive = Fixture.FileAt(Path.Combine(pack, "targets", "nested",
                "Observed.custom"));
            Fixture.FileAt(Path.Combine(pack, "targets", "Unobserved.props"));
            imports.AddRange([seed, fixture.Import(child, seed.Path),
                fixture.Import(transitive, child)]);
            expected.Add(pack, [seed.Path, child, transitive]);
        }
        if (reverseImports) imports.Reverse();
        ControlBuildLog log = fixture.Log([.. imports]);

        ControlBuildDependencies dependencies = ControlBuildDependencyReader.Read(
            fixture.Operation, log);
        ControlBuildConsumption consumption = fixture.Project(log, dependencies);

        Assert.HasCount(packs.Length, dependencies.Directories);
        foreach ((string pack, string[] files) in expected)
        {
            ControlDependencyDirectory dependency = Assert.ContainsSingle(
                dependencies.Directories.Where(value => value.Directory == pack));
            Assert.AreEqual(WorkloadDependency, dependency.Identity);
            CollectionAssert.AreEquivalent(files, dependency.Files);
            Assert.DoesNotContain(Path.Combine(pack, "targets", "Unobserved.props"),
                dependency.Files);
        }
        ControlExternalInput[] external = expected.Values.SelectMany(files => files)
            .Select(path => new ControlExternalInput(path, WorkloadDependency))
            .Concat(fixture.SelectedSdkInputs).ToArray();
        CollectionAssert.AreEquivalent(external, consumption.External);
        Assert.Contains(new ControlSourceInput("control/Source.cs", "compiler Sources", true),
            consumption.Sources);
        Assert.AreSame(fixture.Operation, consumption.Operation);
    }

    [TestMethod]
    [DataRow("other-sdk")]
    [DataRow("other-sdk-importer")]
    [DataRow("nonliteral")]
    [DataRow("relative-literal")]
    [DataRow("missing-unexpanded")]
    [DataRow("wrong-folder")]
    [DataRow("wrong-basename")]
    [DataRow("missing-evaluation")]
    [DataRow("missing-importer")]
    [DataRow("missing-lineage")]
    public async Task UnqualifiedAutoImportSeedsCannotGrantDependencies(string defect)
    {
        using Fixture fixture = await CreateAsync();
        string pack = fixture.ToolPath("packs/Fixture/1.0");
        string path = Fixture.FileAt(Path.Combine(pack,
            defect == "wrong-folder" ? "Sdk-extra" : "Sdk",
            defect == "wrong-basename" ? "Other.props" : "AutoImport.props"));
        ControlBuildImport seed = fixture.Import(path, fixture.WorkloadImporter,
            "AutoImport.props");
        seed = defect switch
        {
            "other-sdk" => seed with
            {
                Importer = fixture.ToolPath("sdk/other/Sdks/Microsoft.NET.Sdk/targets/"
                    + "Microsoft.NET.Sdk.ImportWorkloads.props"),
            },
            "other-sdk-importer" => seed with
            {
                Importer = Path.Combine(fixture.Operation.SdkDirectory, "Sdks",
                    "Microsoft.NET.Sdk", "targets", "Other.props"),
            },
            "nonliteral" => seed with { UnexpandedProject = "$(WorkloadImport)" },
            "relative-literal" => seed with { UnexpandedProject = "./AutoImport.props" },
            "missing-unexpanded" => seed with { UnexpandedProject = null },
            "missing-evaluation" => seed with { Evaluation = null },
            "missing-importer" => seed with { Importer = null },
            "missing-lineage" => new(fixture.Operation.Project, path),
            _ => seed,
        };
        ControlBuildLog log = fixture.Log([seed]);

        ControlBuildDependencies dependencies = ControlBuildDependencyReader.Read(
            fixture.Operation, log);

        Assert.IsEmpty(dependencies.Directories);
        InvalidDataException error = Assert.ThrowsExactly<InvalidDataException>(() =>
            fixture.Project(log, dependencies));
        Assert.Contains(path, error.Message);
    }

    [TestMethod]
    [DataRow("disconnected")]
    [DataRow("escape")]
    [DataRow("pack-prefix")]
    [DataRow("submission")]
    [DataRow("node")]
    [DataRow("evaluation")]
    [DataRow("owner")]
    [DataRow("missing-evaluation")]
    [DataRow("missing-importer")]
    [DataRow("unobserved")]
    public async Task UnconnectedWorkloadImportsRemainUnqualified(string defect)
    {
        using Fixture fixture = await CreateAsync();
        string pack = fixture.ToolPath("packs/Fixture/1.0");
        ControlBuildImport seed = fixture.Seed(pack);
        string path = defect switch
        {
            "escape" => Path.GetFullPath(Path.Combine(pack, "..", "hooks", "Escaped.props")),
            "pack-prefix" => Path.Combine(pack + "-extra", "targets", "Prefix.props"),
            _ => Path.Combine(pack, "targets", "Child.props"),
        };
        Fixture.FileAt(path);
        ControlBuildImport child = fixture.Import(path, seed.Path);
        child = defect switch
        {
            "disconnected" => child with
            {
                Importer = Fixture.FileAt(Path.Combine(pack, "targets", "UnobservedParent.props")),
            },
            "submission" => child with { Evaluation = new(2, 1, 10) },
            "node" => child with { Evaluation = new(1, 2, 10) },
            "evaluation" => child with { Evaluation = new(1, 1, 20) },
            "owner" => child with { Project = fixture.PathOf("other/Control.csproj") },
            "missing-evaluation" => child with { Evaluation = null },
            "missing-importer" => child with { Importer = null },
            _ => child,
        };
        ControlBuildTask compiler = fixture.Compiler;
        if (defect == "unobserved")
            compiler = compiler with
            {
                Parameters = [.. compiler.Parameters, Input("AdditionalFiles", path)],
            };
        ControlBuildLog log = fixture.Log(defect == "unobserved" ? [seed] : [seed, child],
            compiler);

        ControlBuildDependencies dependencies = ControlBuildDependencyReader.Read(
            fixture.Operation, log);

        ControlDependencyDirectory dependency = Assert.ContainsSingle(dependencies.Directories);
        Assert.AreEqual(pack, dependency.Directory);
        Assert.AreEqual(WorkloadDependency, dependency.Identity);
        CollectionAssert.AreEquivalent(new[] { seed.Path }, dependency.Files);
        Assert.DoesNotContain(path, dependency.Files);
        InvalidDataException error = Assert.ThrowsExactly<InvalidDataException>(() =>
            fixture.Project(log, dependencies));
        Assert.Contains(path, error.Message);
    }

    [TestMethod]
    [DataRow("missing-seed")]
    [DataRow("relative-seed")]
    [DataRow("missing-child")]
    [DataRow("relative-child")]
    public async Task MissingOrRelativeWorkloadFilesCannotGrantMembership(string defect)
    {
        using Fixture fixture = await CreateAsync();
        string pack = fixture.ToolPath("packs/Fixture/1.0");
        ControlBuildImport seed = fixture.Seed(pack);
        var imports = new List<ControlBuildImport> { seed };
        if (defect == "missing-seed") File.Delete(seed.Path);
        if (defect == "relative-seed")
            imports[0] = seed with { Path = "unqualified-workload/Sdk/AutoImport.props" };
        if (defect.EndsWith("child", StringComparison.Ordinal))
        {
            string path = defect == "relative-child" ? "unqualified-workload/targets/Child.props"
                : Path.Combine(pack, "targets", "Missing.props");
            imports.Add(fixture.Import(path, seed.Path));
        }
        ControlBuildLog log = fixture.Log([.. imports]);

        ControlBuildDependencies dependencies = ControlBuildDependencyReader.Read(
            fixture.Operation, log);

        if (defect.EndsWith("seed", StringComparison.Ordinal))
            Assert.IsEmpty(dependencies.Directories);
        else
        {
            ControlDependencyDirectory dependency = Assert.ContainsSingle(dependencies.Directories);
            Assert.AreEqual(pack, dependency.Directory);
            Assert.AreEqual(WorkloadDependency, dependency.Identity);
            CollectionAssert.AreEquivalent(new[] { seed.Path }, dependency.Files);
            Assert.DoesNotContain(imports[1].Path, dependency.Files);
        }
        Assert.ThrowsExactly<InvalidDataException>(() => fixture.Project(log, dependencies));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task CommittedWorkloadImportsRemainRepositorySources(bool observeChild)
    {
        using Fixture fixture = await CreateAsync(committedPack: true);
        string pack = fixture.PathOf("control/CommittedPack");
        ControlBuildImport seed = fixture.Seed(pack);
        string child = fixture.PathOf("control/CommittedPack/targets/Child.props");
        ControlBuildTask compiler = fixture.Compiler;
        if (!observeChild)
            compiler = compiler with
            {
                Parameters = [.. compiler.Parameters, Input("AdditionalFiles", child)],
            };
        ControlBuildLog log = fixture.Log(observeChild
            ? [seed, fixture.Import(child, seed.Path)] : [seed], compiler);

        ControlBuildDependencies dependencies = ControlBuildDependencyReader.Read(
            fixture.Operation, log);
        ControlBuildConsumption consumption = fixture.Project(log, dependencies);

        ControlDependencyDirectory dependency = Assert.ContainsSingle(dependencies.Directories);
        Assert.AreEqual(pack, dependency.Directory);
        Assert.AreEqual(WorkloadDependency, dependency.Identity);
        CollectionAssert.AreEquivalent(observeChild ? new[] { seed.Path, child } : [seed.Path],
            dependency.Files);
        Assert.Contains(new ControlSourceInput("control/CommittedPack/Sdk/AutoImport.props",
            "import", true), consumption.Sources);
        Assert.Contains(new ControlSourceInput("control/CommittedPack/targets/Child.props",
            observeChild ? "import" : "compiler AdditionalFiles", true), consumption.Sources);
        Assert.DoesNotContain(seed.Path,
            consumption.External.Select(value => value.Path).ToArray());
        Assert.DoesNotContain(child, consumption.External.Select(value => value.Path).ToArray());
        CollectionAssert.AreEquivalent(fixture.SelectedSdkInputs, consumption.External);
    }

    [TestMethod]
    [DataRow("task")]
    [DataRow("implementation")]
    [DataRow("role")]
    public async Task WorkloadMembershipDoesNotAuthorizeUnsupportedTasksOrRoles(string defect)
    {
        using Fixture fixture = await CreateAsync();
        string pack = fixture.ToolPath("packs/Fixture/1.0");
        ControlBuildImport seed = fixture.Seed(pack);
        ControlBuildLog valid = fixture.Log([seed]);
        ControlBuildDependencies dependencies = ControlBuildDependencyReader.Read(
            fixture.Operation, valid);
        Assert.AreEqual(seed.Path, Assert.ContainsSingle(
            Assert.ContainsSingle(dependencies.Directories).Files));
        ControlBuildTask compiler = fixture.Compiler;
        compiler = defect switch
        {
            "task" => compiler with { Name = "CustomWorkloadTask" },
            "implementation" => compiler with { Implementation = seed.Path },
            _ => compiler with
            {
                Parameters = [.. compiler.Parameters, Input("ResponseFiles", seed.Path)],
            },
        };
        ControlBuildLog log = fixture.Log([seed], compiler);

        InvalidDataException error = Assert.ThrowsExactly<InvalidDataException>(() =>
            fixture.Project(log, dependencies));

        Assert.StartsWith(defect switch
        {
            "task" => "Unsupported control task:",
            "implementation" => "Unsupported native task implementation:",
            _ => "Unsupported indirect compiler input:",
        }, error.Message);
    }

    private async Task<Fixture> CreateAsync(bool committedPack = false)
    {
        GitFixture repo = await GitFixture.CreateAsync(context.CancellationToken);
        try
        {
            await repo.SetAsync("control/Control.csproj", "<Project />");
            await repo.SetAsync("control/Source.cs", "internal sealed class Source { }");
            if (committedPack)
                foreach (string path in new[] { "control/CommittedPack/Sdk/AutoImport.props",
                    "control/CommittedPack/targets/Child.props" })
                    await repo.SetAsync(path, "<Project />");
            await repo.CommitAsync();
            await repo.GitAsync("reset", "--hard", "HEAD");
            GitRevision revision = await new GitReader(repo.Directory).ReadAsync("HEAD",
                context.CancellationToken);
            GitMaterialization checkout = await GitMaterialization.BindAsync(repo.Directory,
                revision, context.CancellationToken);
            return new(repo, checkout);
        }
        catch { repo.Dispose(); throw; }
    }

    private static ControlBuildParameter Input(string name, string path) => new(
        TaskParameterMessageKind.TaskInput, name, name, null,
        [new(path, new Dictionary<string, string>())]);

    // Prepared public NuGet model and controlled build records; no native workload qualification.
    private sealed class Fixture : IDisposable
    {
        private readonly GitFixture repo;
        private readonly GitMaterialization checkout;
        private readonly string tools;
        internal ControlBuildContext Operation { get; }
        internal string WorkloadImporter { get; }
        internal ControlBuildTask Compiler { get; }
        internal ControlExternalInput[] SelectedSdkInputs =>
            [new(Compiler.Definition, "selected SDK"),
                new(Compiler.Implementation, "selected SDK")];

        internal Fixture(GitFixture repo, GitMaterialization checkout)
        {
            this.repo = repo;
            this.checkout = checkout;
            tools = repo.Directory + ".control-workload-tools";
            var operation = new ControlBuildContext(checkout.Revision.Commit,
                PathOf("control/Control.csproj"), new("Debug", "net10.0"), "fixture-sdk",
                ToolPath("sdk/selected"), tools, ToolPath("packages"), PathOf("control/bin"),
                PathOf("control/obj/Debug/net10.0"), PathOf("control/obj"),
                PathOf("control/obj/project.assets.json"), PathOf("control/bin/Control.dll"),
                [], [], new Dictionary<string, string>());
            Operation = operation with
            {
                Properties = new Dictionary<string, string>
                {
                    ["MSBuildProjectFullPath"] = operation.Project,
                    ["Configuration"] = "Debug",
                    ["TargetFramework"] = "net10.0",
                    ["TargetFrameworks"] = "",
                    ["RuntimeIdentifier"] = "",
                    ["RuntimeIdentifiers"] = "",
                    ["NETCoreSdkVersion"] = operation.SdkVersion,
                    ["MSBuildToolsPath"] = operation.SdkDirectory,
                    ["NetCoreRoot"] = operation.DotNetDirectory,
                    ["NuGetPackageRoot"] = operation.PackageDirectory,
                    ["OutputPath"] = operation.OutputDirectory,
                    ["IntermediateOutputPath"] = operation.IntermediateDirectory,
                    ["BaseIntermediateOutputPath"] = operation.BaseIntermediateDirectory,
                    ["ProjectAssetsFile"] = operation.AssetsFile,
                    ["TargetPath"] = operation.TargetFile,
                    ["ContinuousIntegrationBuild"] = "true",
                    ["RestoreLockedMode"] = "true",
                    ["MSBuildStartupDirectory"] = checkout.Root,
                    ["MSBuildLogVerboseTaskParameters"] = "true",
                    ["NuGetInteractive"] = "false",
                },
            };
            WorkloadImporter = FileAt(Path.Combine(Operation.SdkDirectory, "Sdks",
                "Microsoft.NET.Sdk", "targets", "Microsoft.NET.Sdk.ImportWorkloads.props"));
            Compiler = new(Operation.Project, "Csc", WorkloadImporter,
                FileAt(Path.Combine(Operation.SdkDirectory, "Roslyn",
                    "Microsoft.Build.Tasks.CodeAnalysis.dll")), [Input("Sources", "Source.cs")]);
            var restore = new ProjectRestoreMetadata
            {
                ProjectPath = Operation.Project,
                OutputPath = Operation.BaseIntermediateDirectory,
                RestoreLockProperties = new("true", "", true),
            };
            restore.OriginalTargetFrameworks.Add(Operation.Dimension.TargetFramework);
            var assets = new LockFile
            {
                Version = 3,
                PackageSpec = new() { RestoreMetadata = restore },
            };
            assets.Targets.Add(new()
            {
                TargetFramework = NuGetFramework.ParseFolder(Operation.Dimension.TargetFramework),
            });
            Directory.CreateDirectory(Operation.BaseIntermediateDirectory);
            new LockFileFormat().Write(Operation.AssetsFile, assets);
        }

        internal string PathOf(string relative) => Path.GetFullPath(Path.Combine(repo.Directory,
            relative.Replace('/', Path.DirectorySeparatorChar)));
        internal string ToolPath(string relative) => Path.GetFullPath(Path.Combine(tools,
            relative.Replace('/', Path.DirectorySeparatorChar)));
        internal static string FileAt(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "<Project />");
            return path;
        }

        internal ControlBuildImport Import(string path, string importer,
            string? unexpanded = null) => new(Operation.Project, path)
            {
                Evaluation = ControlWorkloadInputsTests.Evaluation,
                Importer = importer,
                UnexpandedProject = unexpanded
            };
        internal ControlBuildImport Seed(string pack) => Import(
            FileAt(Path.Combine(pack, "Sdk", "AutoImport.props")), WorkloadImporter,
            "AutoImport.props");
        internal ControlBuildLog Log(ControlBuildImport[] imports,
            ControlBuildTask? compiler = null) => new(Operation.Project, [compiler ?? Compiler],
                imports, new Dictionary<string, string>(Operation.Properties),
                ControlBuildContextReader.GlobalPropertyNames.ToDictionary(name => name,
                    name => Operation.Properties[name], StringComparer.Ordinal));
        internal ControlBuildConsumption Project(ControlBuildLog log,
            ControlBuildDependencies dependencies) => ControlBuildInputProjection.Project(
                checkout, Operation, log, dependencies, []);

        public void Dispose()
        {
            repo.Dispose();
            if (Directory.Exists(tools)) Directory.Delete(tools, recursive: true);
        }
    }
}
