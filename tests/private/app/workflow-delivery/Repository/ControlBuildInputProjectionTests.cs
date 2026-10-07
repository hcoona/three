using Microsoft.Build.Framework;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NuGet.Frameworks;
using NuGet.ProjectModel;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;
using GitFixture = WorkflowDelivery.Tests.Repository.GitReaderTests.GitFixture;

namespace WorkflowDelivery.Tests.Repository;

[TestClass]
public sealed class ControlBuildInputProjectionTests(TestContext context)
{
    [TestMethod]
    public async Task CompilerInputsRetainCommittedSpellingAndNativeRoles()
    {
        using Fixture fixture = await CreateAsync();
        ControlBuildTask compiler = fixture.Compiler("Source.cs") with
        {
            Parameters = [Input("Sources", "Source.cs"), Input("Resources", "Resource.txt"),
                Input("AnalyzerConfigFiles", "../.editorconfig")],
        };
        var log = fixture.Log([compiler],
            [new(fixture.Operation.Project, fixture.Path("Shared.props"))]);

        ControlBuildConsumption result = fixture.Project(log);

        Assert.Contains(new ControlSourceInput("control/Source.cs", "compiler Sources",
            true), result.Sources);
        Assert.Contains(new ControlSourceInput("control/Resource.txt", "compiler Resources",
            true), result.Sources);
        Assert.Contains(new ControlSourceInput(".editorconfig",
            "compiler AnalyzerConfigFiles", true), result.Sources);
        Assert.Contains(new ControlSourceInput("Shared.props", "import", true), result.Sources);
        Assert.Contains(new ControlSourceInput("control/Control.csproj", "project", true),
            result.Sources);
        Assert.IsEmpty(result.Generated);
    }

    [TestMethod]
    public async Task GeneratedInputsRequireTheirActualNativeProducer()
    {
        using Fixture fixture = await CreateAsync();
        string generated = System.IO.Path.Combine(fixture.Operation.IntermediateDirectory,
            "version.cs");
        string sourceLink = System.IO.Path.Combine(fixture.Operation.IntermediateDirectory,
            "source.json");
        string package = System.IO.Path.Combine(fixture.ToolDirectory, "sourcelink");
        string implementation = System.IO.Path.Combine(package,
            "tools", "net", "Microsoft.SourceLink.Common.dll");
        ControlBuildDependencies dependencies = fixture.Dependencies with
        {
            Directories = [new("Microsoft.SourceLink.Common/1.0.0", package, [implementation])],
        };
        ControlBuildTask compiler = fixture.Compiler(generated) with
        {
            Parameters = [Input("Sources", "Source.cs", generated), Input("SourceLink",
                sourceLink)],
        };
        ControlBuildTask producer = fixture.Task("WriteCodeFragment",
            "Microsoft.Build.Tasks.Core.dll", Input("OutputFile", generated));
        ControlBuildTask link = fixture.Task("Microsoft.SourceLink.Common.GenerateSourceLinkFile",
            implementation, Input("OutputFile", sourceLink));

        ControlBuildConsumption result = fixture.Project(fixture.Log(
            [compiler, producer, link], []), dependencies);

        Assert.Contains(new ControlGeneratedInput(generated, "WriteCodeFragment"),
            result.Generated);
        Assert.Contains(new ControlGeneratedInput(sourceLink,
            "Microsoft.SourceLink.Common.GenerateSourceLinkFile"),
            result.Generated);
        Assert.IsFalse(result.Sources.Any(s => s.Path.EndsWith("version.cs",
            StringComparison.Ordinal)));
        Assert.Contains(new ControlSourceInput("control/Source.cs", "compiler Sources",
            true), result.Sources);
        Assert.ThrowsExactly<InvalidDataException>(() => fixture.Project(
            fixture.Log([compiler, link]), dependencies));
    }

    [TestMethod]
    public async Task RuntimeCopiesRetainSourceInputsAndImplementationIdentity()
    {
        using Fixture fixture = await CreateAsync();
        string destination = System.IO.Path.Combine(fixture.Operation.OutputDirectory,
            "settings.json");
        ControlBuildTask copy = fixture.Task("Copy", "Microsoft.Build.Tasks.Core.dll",
            Input("SourceFiles", "settings.json"), Input("DestinationFiles", destination),
            Output("DestinationFiles", destination));

        ControlBuildConsumption result = fixture.Project(fixture.Log(
            [fixture.Compiler("Source.cs"), copy], []));

        Assert.Contains(new ControlSourceInput("control/settings.json",
            "runtime copy source", true), result.Sources);
        ControlBuildTask otherImplementation = copy with
        {
            Implementation = System.IO.Path.Combine(fixture.Operation.SdkDirectory,
                "OtherTasks.dll"),
        };
        Assert.ThrowsExactly<InvalidDataException>(() => fixture.Project(
            fixture.Log([fixture.Compiler("Source.cs"), otherImplementation])));
    }

    [TestMethod]
    public async Task OptionalCandidatesRetainAbsenceAndRejectUnknownExternalFiles()
    {
        using Fixture fixture = await CreateAsync();
        fixture.Operation = fixture.Operation with
        {
            EditorConfigCandidates = [fixture.Path("control/absent.editorconfig")],
        };

        ControlBuildConsumption result = fixture.Project(fixture.Log(
            [fixture.Compiler("Source.cs")], []));

        Assert.Contains(new ControlSourceInput("control/absent.editorconfig",
            "native configuration candidate", false),
            result.Sources);
        Assert.Contains(new ControlSourceInput("version.json",
            "native version configuration", false), result.Sources);
        string unknown = System.IO.Path.Combine(fixture.ToolDirectory, "unknown.dll");
        ControlBuildTask compiler = fixture.Compiler("Source.cs") with
        {
            Parameters = [Input("Sources", "Source.cs"), Input("References", unknown)],
        };
        Assert.ThrowsExactly<InvalidDataException>(() => fixture.Project(fixture.Log([compiler])));
        ControlBuildDependencies known = fixture.Dependencies with
        {
            Directories = [new("Fixture.Package/1.0.0", fixture.ToolDirectory, [unknown])],
        };
        result = fixture.Project(fixture.Log([compiler], []), known);
        Assert.Contains(new ControlExternalInput(unknown, "Fixture.Package/1.0.0"),
            result.External);
    }

    [TestMethod]
    [DataRow("unproduced-generated")]
    [DataRow("unknown-task")]
    [DataRow("unknown-parameter")]
    [DataRow("response-file")]
    [DataRow("library-search")]
    [DataRow("unproduced-sourcelink")]
    [DataRow("conflicting-copy")]
    [DataRow("outside-producer")]
    [DataRow("different-revision")]
    [DataRow("framework-cache")]
    [DataRow("wrong-core-implementation")]
    [DataRow("wrong-sdk-implementation")]
    public async Task UnsupportedNativeConsumptionCannotBecomeCompleteFacts(string defect)
    {
        using Fixture fixture = await CreateAsync();
        ControlBuildTask compiler = fixture.Compiler("Source.cs");
        var tasks = new List<ControlBuildTask> { compiler };
        switch (defect)
        {
            case "unproduced-generated":
                tasks[0] = fixture.Compiler("obj/unknown.cs"); break;
            case "unknown-task":
                tasks.Add(fixture.Task("CustomGenerator", "Microsoft.Build.Tasks.Core.dll")); break;
            case "unknown-parameter":
            case "response-file":
            case "library-search":
            case "unproduced-sourcelink":
                string role = defect switch
                {
                    "response-file" => "ResponseFiles",
                    "library-search" => "AdditionalLibPaths",
                    "unproduced-sourcelink" => "SourceLink",
                    _ => "HiddenInput",
                };
                tasks[0] = compiler with
                {
                    Parameters = [Input("Sources", "Source.cs"), Input(role, "indirect")],
                };
                break;
            case "conflicting-copy":
                tasks.Add(fixture.Task("Copy", "Microsoft.Build.Tasks.Core.dll",
                    Input("SourceFiles", "settings.json"),
                    Input("DestinationFiles", "bin/a.json"), Output("DestinationFiles",
                        "bin/b.json")));
                break;
            case "outside-producer":
                tasks.Add(fixture.Task("WriteCodeFragment", "Microsoft.Build.Tasks.Core.dll",
                    Input("OutputFile", "outside.cs"))); break;
            case "framework-cache":
                tasks[0] = compiler with
                {
                    Parameters = [Input("Sources", "Source.cs"), Input("References",
                        System.IO.Path.Combine(fixture.Operation.DotNetDirectory,
                            "packs/other/1.0/ref.dll"))],
                };
                break;
            case "wrong-core-implementation":
                tasks.Add(fixture.Task("WriteCodeFragment",
                    "Sdks/Microsoft.NET.Sdk/tools/net10.0/Microsoft.NET.Build.Tasks.dll"));
                break;
            case "wrong-sdk-implementation":
                tasks.Add(fixture.Task("GenerateGlobalUsings", "Microsoft.Build.Tasks.Core.dll"));
                break;
            case "different-revision":
                fixture.Operation = fixture.Operation with
                {
                    Revision = new string('f',
                    40)
                }; break;
            default: Assert.Fail("Unknown fixture defect."); break;
        }

        Assert.ThrowsExactly<InvalidDataException>(() => fixture.Project(
            fixture.Log(tasks.ToArray())));
    }

    [TestMethod]
    [DataRow("AppConfigFile")]
    [DataRow("AssemblyInformationCachePaths")]
    [DataRow("InstalledAssemblyTables")]
    [DataRow("InstalledAssemblySubsetTables")]
    [DataRow("FullFrameworkAssemblyTables")]
    public async Task UnsupportedResolverInputsCannotBecomeCompleteFacts(string role)
    {
        using Fixture fixture = await CreateAsync();
        ControlBuildTask resolver = fixture.Task("ResolveAssemblyReference",
            "Microsoft.Build.Tasks.Core.dll", Input(role, "native-input.txt"),
            Input("AutoUnify", "False"));

        Assert.ThrowsExactly<InvalidDataException>(() => fixture.Project(fixture.Log(
            [resolver, fixture.Compiler("Source.cs")])));
    }

    [TestMethod]
    [DataRow("absent")]
    [DataRow("empty")]
    [DataRow("whitespace")]
    public async Task UnusedOptionalResolverInputsPreserveOrdinaryCompilation(string shape)
    {
        using Fixture fixture = await CreateAsync();
        ControlBuildParameter[] parameters = shape switch
        {
            "absent" => [],
            "empty" => [Input("AppConfigFile"), Input("AssemblyInformationCachePaths"),
                Input("InstalledAssemblyTables"), Input("InstalledAssemblySubsetTables"),
                Input("FullFrameworkAssemblyTables")],
            "whitespace" => [Input("AppConfigFile", " "),
                Input("AssemblyInformationCachePaths", " "), Input("InstalledAssemblyTables", " "),
                Input("InstalledAssemblySubsetTables", " "),
                Input("FullFrameworkAssemblyTables", " ")],
            _ => throw new InvalidOperationException("Unknown optional resolver shape."),
        };
        ControlBuildTask resolver = fixture.Task("ResolveAssemblyReference",
            "Microsoft.Build.Tasks.Core.dll", parameters);

        ControlBuildConsumption result = fixture.Project(fixture.Log(
            [resolver, fixture.Compiler("Source.cs")]));

        Assert.Contains(new ControlSourceInput("control/Source.cs", "compiler Sources", true),
            result.Sources);
        Assert.Contains(new ControlExternalInput(resolver.Implementation,
            "selected SDK"), result.External);
    }

    [TestMethod]
    [DataRow("nonempty")]
    [DataRow("absent")]
    [DataRow("empty")]
    [DataRow("whitespace")]
    public async Task UnsignedNativeVersionKeyInputRequiresAnAdapter(string shape)
    {
        using Fixture fixture = await CreateAsync();
        string directory = System.IO.Path.Combine(fixture.ToolDirectory, "nbgv");
        string implementation = System.IO.Path.Combine(directory,
            "build", "MSBuildCore", "Nerdbank.GitVersioning.Tasks.dll");
        string generated = System.IO.Path.Combine(fixture.Operation.IntermediateDirectory,
            "version.cs");
        ControlBuildDependencies dependencies = fixture.Dependencies with
        {
            Directories = [new("Nerdbank.GitVersioning/3.10.94", directory, [implementation])],
        };
        ControlBuildParameter[] parameters = shape switch
        {
            "nonempty" => [Input("AssemblyOriginatorKeyFile", "unsigned.snk")],
            "absent" => [],
            "empty" => [Input("AssemblyOriginatorKeyFile")],
            "whitespace" => [Input("AssemblyOriginatorKeyFile", " ")],
            _ => throw new InvalidOperationException("Unknown native version key shape."),
        };
        ControlBuildTask producer = fixture.Task("Nerdbank.GitVersioning.Tasks.AssemblyVersionInfo",
            implementation, [Input("OutputFile", generated), .. parameters]);
        // An unsigned compiler supplies no KeyFile; NBGV's independent read still matters.
        ControlBuildTask compiler = fixture.Compiler("Source.cs") with
        {
            Parameters = [Input("Sources", "Source.cs", generated), Input("PublicSign", "False")],
        };
        ControlBuildLog log = fixture.Log([producer, compiler]);
        if (shape == "nonempty")
        {
            InvalidDataException error = Assert.ThrowsExactly<InvalidDataException>(() =>
                fixture.Project(log, dependencies));
            Assert.AreEqual("Unsupported native version key-file input.", error.Message);
            return;
        }

        ControlBuildConsumption result = fixture.Project(log, dependencies);

        Assert.Contains(new ControlGeneratedInput(generated, producer.Name), result.Generated);
        Assert.Contains(new ControlSourceInput("control/Source.cs", "compiler Sources", true),
            result.Sources);
    }

    [TestMethod]
    [DataRow("Configuration")]
    [DataRow("ContinuousIntegrationBuild")]
    [DataRow("RestoreLockedMode")]
    [DataRow("MSBuildLogVerboseTaskParameters")]
    [DataRow("NuGetInteractive")]
    [DataRow("all")]
    public async Task EvaluatedValuesCannotReplaceMissingExplicitGlobals(string missing)
    {
        using Fixture fixture = await CreateAsync();
        ControlBuildLog log = fixture.Log([fixture.Compiler("Source.cs")]);
        var globals = new Dictionary<string, string>(log.GlobalProperties);
        if (missing == "all") globals.Clear();
        else globals.Remove(missing);
        log = log with { GlobalProperties = globals };

        Assert.ThrowsExactly<InvalidDataException>(() => fixture.Project(log));
        Assert.ThrowsExactly<InvalidDataException>(() =>
            ControlBuildDependencyReader.Read(fixture.Operation, log));
        Assert.AreEqual(fixture.Operation.Properties["ContinuousIntegrationBuild"],
            log.Properties["ContinuousIntegrationBuild"]);
    }

    [TestMethod]
    public async Task NativeReaderFilesRemainInputsOfGeneratedCompilation()
    {
        using Fixture fixture = await CreateAsync();
        string generated = System.IO.Path.Combine(fixture.Operation.IntermediateDirectory,
            "from-native-reader.cs");
        ControlBuildTask reader = fixture.Task("ReadLinesFromFile",
            "Microsoft.Build.Tasks.Core.dll", Input("File", "native-input.txt"));
        ControlBuildTask producer = fixture.Task("WriteLinesToFile",
            "Microsoft.Build.Tasks.Core.dll", Input("File", generated));

        ControlBuildConsumption result = fixture.Project(fixture.Log(
            [reader, producer, fixture.Compiler(generated)]));

        Assert.Contains(new ControlSourceInput("control/native-input.txt", "native reader File",
            true), result.Sources);
        Assert.Contains(new ControlGeneratedInput(generated, "WriteLinesToFile"), result.Generated);
        reader = reader with { Parameters = [Input("File", "absent-native-input.txt")] };
        result = fixture.Project(fixture.Log([reader, producer, fixture.Compiler(generated)]));
        Assert.Contains(new ControlSourceInput("control/absent-native-input.txt",
            "native reader File", false), result.Sources);
    }

    [TestMethod]
    [DataRow("missing-file")]
    [DataRow("multiple-files")]
    [DataRow("unknown-reader-parameter")]
    [DataRow("unknown-external-file")]
    [DataRow("absent-external-file")]
    public async Task UnsupportedNativeReaderCannotHideAnInput(string defect)
    {
        using Fixture fixture = await CreateAsync();
        ControlBuildParameter[] parameters = defect switch
        {
            "missing-file" => [],
            "multiple-files" => [Input("File", "Source.cs", "native-input.txt")],
            "unknown-reader-parameter" => [Input("File", "native-input.txt"),
                Input("HiddenInput", "Source.cs")],
            "unknown-external-file" or "absent-external-file" =>
                [Input("File", System.IO.Path.Combine(fixture.ToolDirectory, "unresolved.txt"))],
            _ => throw new InvalidOperationException("Unknown reader fixture defect."),
        };
        ControlBuildTask reader = fixture.Task("ReadLinesFromFile",
            "Microsoft.Build.Tasks.Core.dll", parameters);
        if (defect == "unknown-external-file")
        {
            Directory.CreateDirectory(fixture.ToolDirectory);
            await File.WriteAllTextAsync(System.IO.Path.Combine(fixture.ToolDirectory,
                "unresolved.txt"), "external input", context.CancellationToken);
        }

        Assert.ThrowsExactly<InvalidDataException>(() => fixture.Project(fixture.Log(
            [reader, fixture.Compiler("Source.cs")])));
    }

    [TestMethod]
    [DataRow("Configuration")]
    [DataRow("TargetFramework")]
    [DataRow("TargetFrameworks")]
    [DataRow("RuntimeIdentifier")]
    [DataRow("RuntimeIdentifiers")]
    [DataRow("NETCoreSdkVersion")]
    [DataRow("MSBuildToolsPath")]
    [DataRow("NetCoreRoot")]
    [DataRow("NuGetPackageRoot")]
    [DataRow("OutputPath")]
    [DataRow("IntermediateOutputPath")]
    [DataRow("BaseIntermediateOutputPath")]
    [DataRow("ProjectAssetsFile")]
    [DataRow("TargetPath")]
    [DataRow("MSBuildStartupDirectory")]
    [DataRow("ContinuousIntegrationBuild")]
    [DataRow("RestoreLockedMode")]
    [DataRow("MSBuildLogVerboseTaskParameters")]
    [DataRow("NuGetInteractive")]
    [DataRow("missing-property")]
    [DataRow("different-global")]
    [DataRow("unknown-global")]
    public async Task LoggedContextMustMatchQueriedControlOperation(string field)
    {
        using Fixture fixture = await CreateAsync();
        ControlBuildLog log = fixture.Log([fixture.Compiler("Source.cs")]);
        var properties = new Dictionary<string, string>(log.Properties);
        var globals = new Dictionary<string, string>(log.GlobalProperties);
        if (field == "missing-property") properties.Remove("TargetFramework");
        else if (field == "different-global") globals["ContinuousIntegrationBuild"] = "false";
        else if (field == "unknown-global") globals["CustomControlInput"] = "value";
        else properties[field] += "different";
        log = log with { Properties = properties, GlobalProperties = globals };

        Assert.ThrowsExactly<InvalidDataException>(() => fixture.Project(log));
        Assert.ThrowsExactly<InvalidDataException>(() =>
            ControlBuildDependencyReader.Read(fixture.Operation, log));
    }

    [TestMethod]
    [DataRow("ordinary")]
    [DataRow("native-reader")]
    [DataRow("different-operation")]
    [DataRow("external-config")]
    public async Task NativeRestoreAndSdkImportsSupplyDependencyIdentity(string scenario)
    {
        using var repo = await GitFixture.CreateAsync(context.CancellationToken);
        string environmentDirectory = repo.Directory + ".restore-environment";
        try
        {
            string project = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <OutputType>Exe</OutputType>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
                  </PropertyGroup>
                </Project>
                """;
            await repo.SetAsync("Program.cs", "System.Console.WriteLine(\"fixture\");\n");
            if (scenario == "native-reader")
            {
                await repo.SetAsync("data.txt", "internal static class NativeFixture { }\n");
                project = project.Replace("</Project>", """
                    <Target Name="GenerateNativeInput" BeforeTargets="CoreCompile">
                      <ReadLinesFromFile File="data.txt">
                        <Output TaskParameter="Lines" ItemName="NativeLines" />
                      </ReadLinesFromFile>
                      <WriteLinesToFile File="$(IntermediateOutputPath)NativeGenerated.cs"
                                        Lines="@(NativeLines)" Overwrite="true" />
                      <ItemGroup>
                        <Compile Include="$(IntermediateOutputPath)NativeGenerated.cs" />
                      </ItemGroup>
                    </Target>
                    </Project>
                    """, StringComparison.Ordinal);
            }
            await repo.SetAsync("Directory.Build.props", """
                <Project>
                  <PropertyGroup>
                    <MSBuildEnableWorkloadResolver>false</MSBuildEnableWorkloadResolver>
                  </PropertyGroup>
                </Project>
                """);
            await repo.SetAsync("Control.csproj", project);
            await repo.SetAsync("nuget.config",
                "<configuration><packageSources><clear /></packageSources></configuration>");
            string configFile = System.IO.Path.Combine(repo.Directory, "nuget.config");
            if (scenario == "external-config")
            {
                Directory.CreateDirectory(environmentDirectory);
                configFile = System.IO.Path.Combine(environmentDirectory, "nuget.config");
                await File.WriteAllTextAsync(configFile,
                    "<configuration><packageSources><clear /></packageSources></configuration>",
                    context.CancellationToken);
            }
            await repo.CommitAsync();
            await repo.GitAsync("reset", "--hard", "HEAD");
            string binlog = System.IO.Path.Combine(context.TestResultsDirectory!,
                "control-consumption-" + Guid.NewGuid().ToString("N") + ".binlog");
            Directory.CreateDirectory(context.TestResultsDirectory!);
            NativeCommandResult initialized = await NativeProcess.ExecuteAsync(new("dotnet",
                repo.Directory,
                ["restore", "Control.csproj", "--configfile", configFile], 30),
                context.CancellationToken);
            Assert.IsTrue(initialized.Succeeded, initialized.Stdout + initialized.Stderr);
            await repo.SetAsync("packages.lock.json", await File.ReadAllTextAsync(
                System.IO.Path.Combine(repo.Directory, "packages.lock.json"),
                    context.CancellationToken));
            await repo.CommitAsync();
            await repo.GitAsync("reset", "--hard", "HEAD");
            Directory.Delete(System.IO.Path.Combine(repo.Directory, "obj"), recursive: true);
            NativeCommandResult restored = await NativeProcess.ExecuteAsync(new("dotnet",
                repo.Directory,
                ["restore", "Control.csproj", "--locked-mode", "--configfile", configFile,
                    "-p:ContinuousIntegrationBuild=true", "-bl:" + binlog + ".restore.binlog"], 30),
                context.CancellationToken);
            Assert.IsTrue(restored.Succeeded, restored.Stdout + restored.Stderr);
            string ci = scenario == "different-operation" ? "false" : "true";
            NativeCommandResult built = await NativeProcess.ExecuteAsync(new("dotnet",
                repo.Directory,
                ["build", "Control.csproj", "--no-restore", "-noAutoResponse",
                    "-p:Configuration=Debug", "-p:NuGetInteractive=false",
                    "-p:ContinuousIntegrationBuild=" + ci, "-p:RestoreLockedMode=" + ci,
                    "-p:MSBuildLogVerboseTaskParameters=true", "-bl:" + binlog], 30),
                context.CancellationToken);
            Assert.IsTrue(built.Succeeded, built.Stdout + built.Stderr);
            GitRevision revision = await new GitReader(repo.Directory).ReadAsync("HEAD",
                context.CancellationToken);
            GitMaterialization checkout = await GitMaterialization.BindAsync(repo.Directory,
                revision,
                context.CancellationToken);
            ControlBuildContext operation = await new ControlBuildContextReader(checkout).ReadAsync(
                "Control.csproj", context.CancellationToken);
            ControlBuildLog log = ControlBuildLogReader.Read(binlog, operation.Project,
                context.CancellationToken);

            if (scenario == "different-operation")
            {
                Assert.AreEqual("false", log.Properties["ContinuousIntegrationBuild"]);
                Assert.AreEqual("false", log.GlobalProperties["RestoreLockedMode"]);
                Assert.ThrowsExactly<InvalidDataException>(() =>
                    ControlBuildDependencyReader.Read(operation, log));
                Assert.ThrowsExactly<InvalidDataException>(() =>
                    ControlBuildInputProjection.Project(checkout, operation, log,
                        new([], [], []), []));
                return;
            }

            ControlBuildDependencies dependencies = ControlBuildDependencyReader.Read(
                operation, log);
            ControlBuildConsumption result = ControlBuildInputProjection.Project(checkout,
                operation, log, dependencies, []);

            var nativeAssets = new LockFileFormat().Read(operation.AssetsFile);
            Assert.IsNotNull(nativeAssets);
            ProjectRestoreMetadata? nativeRestore = nativeAssets.PackageSpec?.RestoreMetadata;
            Assert.IsNotNull(nativeRestore);
            CollectionAssert.AreEqual(new[] { configFile },
                nativeRestore.ConfigFilePaths.ToArray());
            CollectionAssert.AreEqual(new[] { configFile }, dependencies.RestoreConfigurationFiles);
            Assert.IsFalse(dependencies.Directories.Any(d => d.Files.Contains(configFile)));
            if (scenario == "external-config")
            {
                Assert.Contains(new ControlExternalInput(configFile, "native restore environment"),
                    result.External);
                Assert.AreEqual(1, result.External.Count(e => e.Path == configFile));
                Assert.IsFalse(result.Sources.Any(s => s.Role == "native restore configuration"));
            }
            else
            {
                Assert.Contains(new ControlSourceInput("nuget.config",
                    "native restore configuration", true), result.Sources);
                Assert.IsFalse(result.External.Any(e => e.Path == configFile));
            }
            Assert.Contains(operation.AssetsFile, dependencies.RestoreGeneratedFiles);
            Assert.Contains(new ControlSourceInput("Program.cs", "compiler Sources", true),
                result.Sources);
            Assert.Contains(new ControlSourceInput("Directory.Build.props", "import", true),
                result.Sources);
            Assert.IsTrue(result.Generated.Any(g => g.Producer == "WriteCodeFragment"));
            Assert.IsTrue(result.External.Any(e => e.Dependency.StartsWith(
                "selected SDK pack/Microsoft.NETCore.App.Ref/", StringComparison.Ordinal)));
            if (scenario == "native-reader")
            {
                Assert.Contains(new ControlSourceInput("data.txt", "native reader File", true),
                    result.Sources);
                Assert.Contains(new ControlGeneratedInput(System.IO.Path.Combine(
                    operation.IntermediateDirectory, "NativeGenerated.cs"), "WriteLinesToFile"),
                    result.Generated);
            }
        }
        finally
        {
            if (Directory.Exists(environmentDirectory))
                Directory.Delete(environmentDirectory, recursive: true);
        }
    }

    [TestMethod]
    [DataRow("project")]
    [DataRow("unlocked")]
    [DataRow("output")]
    [DataRow("framework")]
    [DataRow("rid")]
    [DataRow("multi-target")]
    [DataRow("missing-restore")]
    [DataRow("missing-assets")]
    public async Task MalformedNativeRestoreCannotBecomeDependencyFacts(string defect)
    {
        using Fixture fixture = await CreateAsync();
        ControlBuildContext operation = fixture.Operation;
        var restore = new ProjectRestoreMetadata
        {
            ProjectPath = operation.Project,
            OutputPath = operation.BaseIntermediateDirectory,
            RestoreLockProperties = new("true", "", true),
        };
        restore.OriginalTargetFrameworks.Add(operation.Dimension.TargetFramework);
        var target = new LockFileTarget
        {
            TargetFramework = NuGetFramework.ParseFolder(operation.Dimension.TargetFramework),
        };
        var assets = new LockFile
        {
            Version = 3,
            PackageSpec = new(
            )
            { RestoreMetadata = restore }
        };
        assets.Targets.Add(target);
        switch (defect)
        {
            case "project": restore.ProjectPath = fixture.Path("other.csproj"); break;
            case "unlocked": restore.RestoreLockProperties = new("true", "", false); break;
            case "output": restore.OutputPath = fixture.Path("other-obj"); break;
            case "framework": target.TargetFramework = NuGetFramework.ParseFolder("net9.0"); break;
            case "rid": target.RuntimeIdentifier = "linux-x64"; break;
            case "multi-target":
                assets.Targets.Add(new(
                )
                { TargetFramework = NuGetFramework.ParseFolder("net9.0") }); break;
            case "missing-restore": assets.PackageSpec = null; break;
            case "missing-assets": break;
            default: Assert.Fail("Unknown native restore defect."); break;
        }
        Directory.CreateDirectory(operation.BaseIntermediateDirectory);
        if (defect != "missing-assets") new LockFileFormat().Write(operation.AssetsFile, assets);

        if (defect == "missing-assets")
        {
            Assert.ThrowsExactly<FileNotFoundException>(() =>
                ControlBuildDependencyReader.Read(operation, fixture.Log([])));
            return;
        }
        InvalidDataException error = Assert.ThrowsExactly<InvalidDataException>(() =>
            ControlBuildDependencyReader.Read(operation, fixture.Log([])));

        Assert.StartsWith("Native restore", error.Message);
    }

    [TestMethod]
    [DataRow("committed")]
    [DataRow("external")]
    public async Task RestoreConfigurationContributorsRespectCommittedPrecedence(string location)
    {
        using Fixture fixture = await CreateAsync();
        string config = location == "committed" ? fixture.Path("control/settings.json")
            : System.IO.Path.Combine(fixture.ToolDirectory, "environment", "nuget.config");
        if (location == "external")
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(config)!);
            await File.WriteAllTextAsync(config, "opaque contract fixture",
                context.CancellationToken);
        }
        WriteRestoreAssets(fixture.Operation, config);
        ControlBuildLog log = fixture.Log([fixture.Compiler("Source.cs")]);

        ControlBuildDependencies dependencies = ControlBuildDependencyReader.Read(
            fixture.Operation, log);
        ControlBuildConsumption result = fixture.Project(log, dependencies);

        CollectionAssert.AreEqual(new[] { config }, dependencies.RestoreConfigurationFiles);
        Assert.IsEmpty(dependencies.Directories);
        Assert.Contains(new ControlSourceInput("control/Source.cs", "compiler Sources", true),
            result.Sources);
        if (location == "committed")
        {
            Assert.Contains(new ControlSourceInput("control/settings.json",
                "native restore configuration", true), result.Sources);
            Assert.IsFalse(result.External.Any(e => e.Path == config));
        }
        else
        {
            Assert.Contains(new ControlExternalInput(config, "native restore environment"),
                result.External);
            Assert.AreEqual(1, result.External.Count(e => e.Path == config));
            Assert.IsFalse(result.Sources.Any(s => s.Role == "native restore configuration"));
        }
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("directory")]
    [DataRow("relative")]
    [DataRow("empty")]
    public async Task MissingOrRelativeRestoreContributorsCannotBecomeFacts(string defect)
    {
        using Fixture fixture = await CreateAsync();
        Directory.CreateDirectory(fixture.ToolDirectory);
        string config = defect switch
        {
            "missing" => System.IO.Path.Combine(fixture.ToolDirectory, "missing.config"),
            "directory" => fixture.ToolDirectory,
            "relative" => Guid.NewGuid().ToString("N") + ".restore-input",
            "empty" => "",
            _ => throw new InvalidOperationException("Unknown contributor defect."),
        };
        string? ownedFile = defect == "relative"
            ? System.IO.Path.Combine(Environment.CurrentDirectory, config) : null;
        try
        {
            if (ownedFile is not null)
            {
                await File.WriteAllTextAsync(ownedFile, "opaque contract fixture",
                    context.CancellationToken);
                Assert.IsFalse(System.IO.Path.IsPathFullyQualified(config));
                Assert.IsTrue(File.Exists(config));
            }
            WriteRestoreAssets(fixture.Operation, config);
            var assets = new LockFileFormat().Read(fixture.Operation.AssetsFile);
            Assert.IsNotNull(assets);
            ProjectRestoreMetadata? restore = assets.PackageSpec?.RestoreMetadata;
            Assert.IsNotNull(restore);
            CollectionAssert.AreEqual(new[] { config }, restore.ConfigFilePaths.ToArray());
            ControlBuildLog log = fixture.Log([fixture.Compiler("Source.cs")]);

            InvalidDataException readerError = Assert.ThrowsExactly<InvalidDataException>(() =>
                ControlBuildDependencyReader.Read(fixture.Operation, log));
            InvalidDataException projectionError = Assert.ThrowsExactly<InvalidDataException>(() =>
                fixture.Project(log, new([], [config], [])));

            Assert.AreEqual("Native restore configuration is unavailable.", readerError.Message);
            Assert.AreEqual("Native restore configuration is unavailable.",
                projectionError.Message);
        }
        finally
        {
            if (ownedFile is not null) File.Delete(ownedFile);
        }
    }

    [TestMethod]
    [DataRow("import")]
    [DataRow("References")]
    [DataRow("AdditionalFiles")]
    [DataRow("task-definition")]
    [DataRow("task-implementation")]
    [DataRow("reader")]
    [DataRow("sibling")]
    public async Task RestoreContributorAssociationCannotAuthorizeOtherInputRoles(string role)
    {
        using Fixture fixture = await CreateAsync();
        string directory = System.IO.Path.Combine(fixture.ToolDirectory, "environment");
        Directory.CreateDirectory(directory);
        string config = System.IO.Path.Combine(directory, "nuget.config");
        string sibling = System.IO.Path.Combine(directory, "unobserved.dll");
        await File.WriteAllTextAsync(config, "opaque contract fixture", context.CancellationToken);
        await File.WriteAllTextAsync(sibling, "unrelated input", context.CancellationToken);
        WriteRestoreAssets(fixture.Operation, config);
        ControlBuildTask compiler = fixture.Compiler("Source.cs");
        ControlBuildLog baseline = fixture.Log([compiler]);
        ControlBuildDependencies dependencies = ControlBuildDependencyReader.Read(
            fixture.Operation, baseline);
        ControlBuildConsumption accepted = fixture.Project(baseline, dependencies);

        CollectionAssert.AreEqual(new[] { config }, dependencies.RestoreConfigurationFiles);
        Assert.IsEmpty(dependencies.Directories);
        Assert.Contains(new ControlExternalInput(config, "native restore environment"),
            accepted.External);
        Assert.IsFalse(accepted.External.Any(e => e.Path == sibling));
        string input = role == "sibling" ? sibling : config;
        ControlBuildLog log = role switch
        {
            "import" => fixture.Log([compiler], [new(fixture.Operation.Project, config)]),
            "task-definition" => fixture.Log([compiler with { Definition = config }]),
            "task-implementation" => fixture.Log([compiler with { Implementation = config }]),
            "reader" => fixture.Log([compiler, fixture.Task("ReadLinesFromFile",
                "Microsoft.Build.Tasks.Core.dll", Input("File", config))]),
            _ => fixture.Log([compiler with
            {
                Parameters = [Input("Sources", "Source.cs"),
                    Input(role == "sibling" ? "References" : role, input)],
            }]),
        };
        // Read the same official contributor with the actual misused-role log as well.
        dependencies = ControlBuildDependencyReader.Read(fixture.Operation, log);
        Assert.IsEmpty(dependencies.Directories);
        InvalidDataException error = Assert.ThrowsExactly<InvalidDataException>(() =>
            fixture.Project(log, dependencies));

        Assert.AreEqual(role == "task-implementation"
            ? "Unsupported native task implementation: Csc"
            : "Unresolved native external input: " + input, error.Message);
    }

    [TestMethod]
    [DataRow("logged-project")]
    [DataRow("working-directory")]
    [DataRow("global")]
    [DataRow("restore-project")]
    public async Task RestoreContributorFactsRemainBoundToControlOperation(string defect)
    {
        using Fixture fixture = await CreateAsync();
        Directory.CreateDirectory(fixture.ToolDirectory);
        string config = System.IO.Path.Combine(fixture.ToolDirectory, "nuget.config");
        await File.WriteAllTextAsync(config, "opaque contract fixture", context.CancellationToken);
        WriteRestoreAssets(fixture.Operation, config);
        ControlBuildLog log = fixture.Log([fixture.Compiler("Source.cs")]);
        ControlBuildDependencies dependencies = ControlBuildDependencyReader.Read(
            fixture.Operation, log);
        Assert.Contains(new ControlExternalInput(config, "native restore environment"),
            fixture.Project(log, dependencies).External);
        switch (defect)
        {
            case "logged-project": log = log with { Project = fixture.Path("other.csproj") }; break;
            case "working-directory":
                var properties = new Dictionary<string, string>(log.Properties)
                {
                    ["MSBuildStartupDirectory"] = fixture.ToolDirectory,
                };
                log = log with { Properties = properties };
                break;
            case "global":
                var globals = new Dictionary<string, string>(log.GlobalProperties)
                {
                    ["ContinuousIntegrationBuild"] = "false",
                };
                log = log with { GlobalProperties = globals };
                break;
            case "restore-project":
                WriteRestoreAssets(fixture.Operation, config, fixture.Path("other.csproj"));
                break;
            default: Assert.Fail("Unknown operation defect."); break;
        }

        Assert.ThrowsExactly<InvalidDataException>(() =>
            ControlBuildDependencyReader.Read(fixture.Operation, log));
        if (defect != "restore-project")
            Assert.ThrowsExactly<InvalidDataException>(() => fixture.Project(log, dependencies));
    }

    private static void WriteRestoreAssets(ControlBuildContext operation, string config,
        string? restoreProject = null)
    {
        var restore = new ProjectRestoreMetadata
        {
            ProjectPath = restoreProject ?? operation.Project,
            OutputPath = operation.BaseIntermediateDirectory,
            RestoreLockProperties = new("true", "", true),
        };
        restore.OriginalTargetFrameworks.Add(operation.Dimension.TargetFramework);
        restore.ConfigFilePaths.Add(config);
        var assets = new LockFile
        {
            Version = 3,
            PackageSpec = new() { RestoreMetadata = restore },
        };
        assets.Targets.Add(new LockFileTarget
        {
            TargetFramework = NuGetFramework.ParseFolder(operation.Dimension.TargetFramework),
        });
        Directory.CreateDirectory(operation.BaseIntermediateDirectory);
        new LockFileFormat().Write(operation.AssetsFile, assets);
    }

    private async Task<Fixture> CreateAsync()
    {
        GitFixture repo = await GitFixture.CreateAsync(context.CancellationToken);
        foreach (string path in new[] { "control/Control.csproj", "control/Source.cs",
            "control/Resource.txt", "control/settings.json", "control/native-input.txt",
            ".editorconfig", "Shared.props" })
            await repo.SetAsync(path);
        await repo.CommitAsync();
        await repo.GitAsync("reset", "--hard", "HEAD");
        GitRevision revision = await new GitReader(repo.Directory).ReadAsync("HEAD",
            context.CancellationToken);
        GitMaterialization checkout = await GitMaterialization.BindAsync(repo.Directory, revision,
            context.CancellationToken);
        return new(repo, checkout);
    }

    private static ControlBuildParameter Input(string name, params string[] paths) => new(
        TaskParameterMessageKind.TaskInput, name, name, null,
        paths.Select(path => new ControlBuildValue(path, new Dictionary<string, string>(
            ))).ToArray());

    private static ControlBuildParameter Output(string name, params string[] paths) => Input(name,
        paths) with
    { Kind = TaskParameterMessageKind.TaskOutput };

    private sealed class Fixture : IDisposable
    {
        private readonly GitFixture repo;
        private readonly GitMaterialization checkout;
        internal ControlBuildContext Operation { get; set; }
        internal string ToolDirectory { get; }
        internal ControlBuildDependencies Dependencies { get; } = new([], [], []);

        internal Fixture(GitFixture repo, GitMaterialization checkout)
        {
            this.repo = repo;
            this.checkout = checkout;
            ToolDirectory = repo.Directory + ".tools";
            string project = Path("control/Control.csproj");
            Operation = new(checkout.Revision.Commit, project, new("Debug", "net10.0"), "fixture",
                System.IO.Path.Combine(ToolDirectory, "sdk"), ToolDirectory,
                System.IO.Path.Combine(ToolDirectory, "packages"), Path("control/bin"),
                Path("control/obj/Debug/net10.0"), Path("control/obj"),
                Path("control/obj/project.assets.json"), Path("control/bin/Control.dll"), [], [],
                new Dictionary<string, string>());
            Operation = Operation with
            {
                Properties = new Dictionary<string, string>
                {
                    ["MSBuildProjectFullPath"] = Operation.Project,
                    ["Configuration"] = "Debug",
                    ["TargetFramework"] = "net10.0",
                    ["TargetFrameworks"] = "",
                    ["RuntimeIdentifier"] = "",
                    ["RuntimeIdentifiers"] = "",
                    ["NETCoreSdkVersion"] = Operation.SdkVersion,
                    ["MSBuildToolsPath"] = Operation.SdkDirectory,
                    ["NetCoreRoot"] = Operation.DotNetDirectory,
                    ["NuGetPackageRoot"] = Operation.PackageDirectory,
                    ["OutputPath"] = Operation.OutputDirectory,
                    ["IntermediateOutputPath"] = Operation.IntermediateDirectory,
                    ["BaseIntermediateOutputPath"] = Operation.BaseIntermediateDirectory,
                    ["ProjectAssetsFile"] = Operation.AssetsFile,
                    ["TargetPath"] = Operation.TargetFile,
                    ["ContinuousIntegrationBuild"] = "true",
                    ["RestoreLockedMode"] = "true",
                    ["MSBuildStartupDirectory"] = checkout.Root,
                    ["MSBuildLogVerboseTaskParameters"] = "true",
                    ["NuGetInteractive"] = "false",
                }
            };
        }

        internal string Path(string relative) =>
            System.IO.Path.GetFullPath(System.IO.Path.Combine(repo.Directory, relative));
        internal ControlBuildLog Log(ControlBuildTask[] tasks,
            ControlBuildImport[]? imports = null) => new(Operation.Project, tasks, imports ?? [],
                new Dictionary<string, string>(Operation.Properties),
                ControlBuildContextReader.GlobalPropertyNames.ToDictionary(name => name,
                    name => Operation.Properties[name], StringComparer.Ordinal));
        internal ControlBuildTask Compiler(string source) => Task("Csc",
            "Roslyn/Microsoft.Build.Tasks.CodeAnalysis.dll", Input("Sources", source));
        internal ControlBuildTask Task(string name, string implementation,
            params ControlBuildParameter[] parameters) => new(Operation.Project, name,
                System.IO.Path.Combine(Operation.SdkDirectory, "tasks.targets"),
                System.IO.Path.GetFullPath(System.IO.Path.IsPathRooted(implementation)
                    ? implementation : System.IO.Path.Combine(Operation.SdkDirectory,
                        implementation)), parameters);
        internal ControlBuildConsumption Project(ControlBuildLog log,
            ControlBuildDependencies? dependencies = null) => ControlBuildInputProjection.Project(
                checkout, Operation, log, dependencies ?? Dependencies, ["version.json"]);
        public void Dispose()
        {
            repo.Dispose();
            if (Directory.Exists(ToolDirectory))
                Directory.Delete(ToolDirectory, recursive: true);
        }
    }
}
