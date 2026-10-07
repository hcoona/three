using Microsoft.Build.Framework;

namespace WorkflowDelivery.Repository;

internal sealed record ControlDependencyDirectory(string Identity, string Directory,
    string[] Files);
internal sealed record ControlSourceInput(string Path, string Role, bool Present);
internal sealed record ControlGeneratedInput(string Path, string Producer);
internal sealed record ControlExternalInput(string Path, string Dependency);
internal sealed record ControlBuildConsumption(ControlBuildContext Operation,
    ControlSourceInput[] Sources, ControlGeneratedInput[] Generated,
        ControlExternalInput[] External);

// Adapt the finite maintained control build. This is not an arbitrary task/file tracer.
internal static class ControlBuildInputProjection
{
    private static readonly HashSet<string> CompilerFiles = new(StringComparer.Ordinal)
    {
        "Sources", "Resources", "References", "AdditionalFiles", "Analyzers",
        "AnalyzerConfigFiles", "EmbeddedFiles", "AddModules", "LinkResources",
    };
    private static readonly HashSet<string> IndirectCompilerFiles = new(StringComparer.Ordinal)
    {
        "ApplicationConfiguration", "CodeAnalysisRuleSet", "KeyFile", "ResponseFiles",
        "Win32Icon", "Win32Manifest", "Win32Resource", "AdditionalLibPaths",
    };
    private static readonly HashSet<string> CompilerValues = new(StringComparer.Ordinal)
    {
        "Optimize", "UseSharedCompilation", "WarningLevel", "Prefer32Bit", "AllowUnsafeBlocks",
        "TargetType", "NoStandardLib", "Nullable", "WarningsAsErrors", "DebugType",
        "HighEntropyVA", "Utf8Output", "ErrorReport", "FileAlignment", "DefineConstants",
        "UseHostCompilerIfAvailable", "TreatWarningsAsErrors", "CheckForOverflowUnderflow",
        "NoConfig", "LangVersion", "GenerateFullPaths", "TargetFramework", "DisabledWarnings",
        "ProjectName", "InterceptorsPreviewNamespaces", "Deterministic", "EmitDebugInformation",
        "PathMap", "Features", "WarningsNotAsErrors", "ChecksumAlgorithm",
        "PublicSign", "DelaySign", "Platform", "ReportAnalyzer", "SkipAnalyzers",
        "DebugInformationFormat", "ConcurrentBuild",
    };
    private static readonly HashSet<string> CompilerOutputs = new(StringComparer.Ordinal)
    {
        "OutputAssembly", "OutputRefAssembly", "PdbFile", "DocumentationFile",
        "GeneratedFilesOutputPath", "ErrorLog",
    };
    private static readonly HashSet<string> SupportedTasks = new(StringComparer.Ordinal)
    {
        "AllowEmptyTelemetry", "CheckIfPackageReferenceShouldBeFrameworkReference",
        "CheckForImplicitPackageReferenceOverrides", "ApplyImplicitVersions",
        "CheckForDuplicateFrameworkReferences", "ProcessFrameworkReferences", "ResolveAppHosts",
        "CheckForDuplicateNuGetItemsTask", "GetRestoreProjectStyleTask", "CheckForDuplicateItems",
        "Message", "DotNet.ReproducibleBuilds.Isolated.ValidateGlobalJsonSdkVersion",
        "FindAppConfigFile", "MakeDir", "GenerateGlobalUsings", "WriteLinesToFile",
        "ResolvePackageAssets", "JoinItems", "MSBuild", "SetRidAgnosticValueForProjects",
        "ValidateExecutableReferences", "GetPackageDirectory", "ResolveFrameworkReferences",
        "ResolveTargetingPackAssets", "ResolvePackageFileConflicts", "ResolveAssemblyReference",
        "CheckForUnsupportedWinMDReferences", "Nerdbank.GitVersioning.Tasks.GetBuildVersion",
        "CreateProperty", "Nerdbank.GitVersioning.Tasks.SetCloudBuildVariables",
        "Nerdbank.GitVersioning.Tasks.AssemblyVersionInfo",
            "Nerdbank.GitVersioning.Tasks.CompareFiles",
        "Copy", "AssignTargetPath", "AssignCulture", "GetFrameworkPath",
        "GenerateMSBuildEditorConfig", "Hash", "WriteCodeFragment", "Csc", "CreateAppHost",
        "Touch", "CallTarget", "GenerateDepsFile", "GenerateRuntimeConfigurationFiles",
        "CopyRefAssembly", "ReadLinesFromFile", "ConvertToAbsolutePath", "FindUnderPath",
        "RemoveDuplicates", "Delete", "Microsoft.CodeAnalysis.BuildTasks.MapSourceRoots",
        "GetAssemblyVersion",
        "Microsoft.SourceLink.Common.GenerateSourceLinkFile",
        "Microsoft.SourceLink.Common.SourceLinkHasSingleProvider",
        "Microsoft.SourceLink.GitHub.GetSourceLinkUrl",
        "Microsoft.SourceLink.GitHub.TranslateRepositoryUrls",
        "Microsoft.SourceLink.GitLab.GetSourceLinkUrl",
        "Microsoft.SourceLink.GitLab.TranslateRepositoryUrls",
        "Microsoft.SourceLink.AzureRepos.Git.GetSourceLinkUrl",
        "Microsoft.SourceLink.AzureRepos.Git.TranslateRepositoryUrls",
        "Microsoft.SourceLink.Bitbucket.Git.GetSourceLinkUrl",
        "Microsoft.SourceLink.Bitbucket.Git.TranslateRepositoryUrls",
        "Microsoft.Build.Tasks.Git.LocateRepository", "Microsoft.Build.Tasks.Git.GetUntrackedFiles",
    };

    private static readonly HashSet<string> CoreTasks = new(StringComparer.Ordinal)
    {
        "Message",
        "FindAppConfigFile",
        "MakeDir",
        "WriteLinesToFile",
        "SetRidAgnosticValueForProjects",
        "ResolveAssemblyReference",
        "CreateProperty",
        "AssignTargetPath",
        "AssignCulture",
        "GetFrameworkPath",
        "Hash",
        "WriteCodeFragment",
        "Touch",
        "ReadLinesFromFile",
        "ConvertToAbsolutePath",
        "FindUnderPath",
        "RemoveDuplicates",
        "Delete",
    };
    private static readonly HashSet<string> DotNetTasks = new(StringComparer.Ordinal)
    {
        "AllowEmptyTelemetry",
        "CheckIfPackageReferenceShouldBeFrameworkReference",
        "CheckForImplicitPackageReferenceOverrides",
        "ApplyImplicitVersions",
        "CheckForDuplicateFrameworkReferences",
        "ProcessFrameworkReferences",
        "ResolveAppHosts",
        "CheckForDuplicateItems",
        "GenerateGlobalUsings",
        "ResolvePackageAssets",
        "JoinItems",
        "ValidateExecutableReferences",
        "GetPackageDirectory",
        "ResolveFrameworkReferences",
        "ResolveTargetingPackAssets",
        "ResolvePackageFileConflicts",
        "CheckForUnsupportedWinMDReferences",
        "CreateAppHost",
        "GenerateDepsFile",
        "GenerateRuntimeConfigurationFiles",
        "GetAssemblyVersion",
    };

    internal static ControlBuildConsumption Project(GitMaterialization checkout,
        ControlBuildContext operation, ControlBuildLog log,
        ControlBuildDependencies dependencies, string[] versionConfigurationCandidates)
    {
        if (operation.Revision != checkout.Revision.Commit || operation.Project != log.Project)
            throw new InvalidDataException("Control consumption has a different native subject.");
        operation.RequireSameOperation(log);
        return new Projection(checkout, operation, dependencies).Read(log,
            versionConfigurationCandidates);
    }

    private sealed record Producer(string Name, string[] Inputs);

    private sealed class Projection
    {
        private readonly GitMaterialization checkout;
        private readonly ControlBuildContext operation;
        private readonly ControlDependencyDirectory[] dependencies;
        private readonly ControlBuildDependencies restore;
        private readonly Dictionary<string, GitEntry> entries;
        private readonly Dictionary<string, Producer> producers;
        private readonly HashSet<ControlSourceInput> sources = [];
        private readonly HashSet<ControlGeneratedInput> generated = [];
        private readonly HashSet<ControlExternalInput> external = [];
        private readonly HashSet<string> visiting;
        private readonly StringComparer comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        internal Projection(GitMaterialization checkout, ControlBuildContext operation,
            ControlBuildDependencies dependencies)
        {
            this.checkout = checkout;
            this.operation = operation;
            restore = dependencies;
            this.dependencies = dependencies.Directories;
            entries = checkout.Revision.Entries.ToDictionary(e => e.Path, comparer);
            producers = new(comparer);
            visiting = new(comparer);
        }

        internal ControlBuildConsumption Read(ControlBuildLog log, string[] versionCandidates)
        {
            foreach (string output in restore.RestoreGeneratedFiles)
                AddGenerated(output, "locked NuGet restore", []);
            foreach (ControlBuildTask task in log.Tasks)
            {
                if (!SupportedTasks.Contains(task.Name))
                    throw new InvalidDataException("Unsupported control task: " + task.Name);
                if (task.Name == "ResolveAssemblyReference" && task.Parameters.Any(p =>
                    p.Kind == TaskParameterMessageKind.TaskInput &&
                    p.Name is "AppConfigFile" or "AssemblyInformationCachePaths" or
                        "InstalledAssemblyTables" or "InstalledAssemblySubsetTables" or
                        "FullFrameworkAssemblyTables" &&
                    p.Values.Any(value => !string.IsNullOrWhiteSpace(value.Identity))))
                    throw new InvalidDataException("Unsupported indirect assembly resolver input.");
                if (task.Name == "Nerdbank.GitVersioning.Tasks.AssemblyVersionInfo" &&
                    task.Parameters.Any(p => p.Kind == TaskParameterMessageKind.TaskInput &&
                        p.Name == "AssemblyOriginatorKeyFile" &&
                        p.Values.Any(value => !string.IsNullOrWhiteSpace(value.Identity))))
                    throw new InvalidDataException("Unsupported native version key-file input.");
                AddFile(task.Definition, "task definition");
                RequireImplementation(task);
                AddExternal(task.Implementation);
                AddProducer(task);
            }
            AddFile(operation.Project, "project");
            foreach (ControlBuildImport import in log.Imports)
                AddFile(import.Path, "import");
            foreach (string config in restore.RestoreConfigurationFiles)
                AddRestoreConfiguration(config);
            foreach (string candidate in operation.EditorConfigCandidates
                .Concat(operation.GlobalConfigCandidates))
                AddFile(candidate, "native configuration candidate", optional: true);
            foreach (string path in versionCandidates)
                AddFile(Path.Combine(checkout.Root, path), "native version configuration",
                    optional: true);
            AddConfigurationCandidates();
            foreach (ControlBuildTask task in log.Tasks)
            {
                if (task.Name == "Csc") ReadCompiler(task);
                if (task.Name == "Copy") ReadCopy(task);
                if (task.Name == "ReadLinesFromFile")
                {
                    string[] files = Paths(task, "File");
                    if (files.Length != 1 || task.Parameters.Any(p =>
                        p.Kind == TaskParameterMessageKind.TaskInput && p.Name != "File"))
                        throw new InvalidDataException("Unsupported native reader input roles.");
                    if (Below(checkout.Root, files[0]))
                        AddFile(files[0], "native reader File", optional: true);
                    else
                        AddExternal(files[0]);
                }
                if (task.Name == "GenerateDepsFile")
                    foreach (string role in new[] { "CompileReferences", "ReferencePaths",
                        "ReferenceDependencyPaths", "UserRuntimeAssemblies" })
                        foreach (string path in Paths(task, role))
                            AddFile(path, "runtime dependency");
                if (task.Name is "GenerateDepsFile" or "GenerateRuntimeConfigurationFiles" or
                    "ResolvePackageAssets")
                    foreach (string path in Paths(task, task.Name == "ResolvePackageAssets"
                        ? "ProjectAssetsFile" : "AssetsFilePath"))
                        AddFile(path, "native restore assets");
                if (task.Name == "GenerateRuntimeConfigurationFiles")
                    foreach (string path in Paths(task, "UserRuntimeConfig"))
                        AddFile(path, "runtime configuration", optional: true);
                if (task.Name == "CreateAppHost")
                    foreach (string path in Paths(task, "AppHostSourcePath"))
                        AddFile(path, "native apphost");
            }
            return new(operation, sources.OrderBy(s => s.Path, StringComparer.Ordinal)
                .ThenBy(s => s.Role, StringComparer.Ordinal).ToArray(),
                generated.OrderBy(g => g.Path, StringComparer.Ordinal).ToArray(),
                external.OrderBy(e => e.Path, StringComparer.Ordinal).ToArray());
        }

        private void ReadCompiler(ControlBuildTask task)
        {
            foreach (ControlBuildParameter parameter in task.Parameters.Where(p =>
                p.Kind == TaskParameterMessageKind.TaskInput))
            {
                if (CompilerFiles.Contains(parameter.Name))
                {
                    foreach (ControlBuildValue value in parameter.Values)
                        AddFile(NativePath(task, value.Identity), "compiler " + parameter.Name);
                }
                else if (parameter.Name == "SourceLink")
                {
                    foreach (string path in Paths(task, "SourceLink"))
                    {
                        if (!producers.TryGetValue(path, out Producer? sourceLink) ||
                            sourceLink.Name != "Microsoft.SourceLink.Common.GenerateSourceLinkFile")
                            throw new InvalidDataException("SourceLink has no native producer.");
                        AddFile(path, "compiler SourceLink");
                    }
                }
                else if (IndirectCompilerFiles.Contains(parameter.Name))
                {
                    if (parameter.Values.Any(value => !string.IsNullOrWhiteSpace(value.Identity)))
                        throw new InvalidDataException(
                            "Unsupported indirect compiler input: " + parameter.Name);
                }
                else if (!CompilerValues.Contains(parameter.Name) &&
                    !CompilerOutputs.Contains(parameter.Name))
                    throw new InvalidDataException(
                        "Unsupported compiler parameter: " + parameter.Name);
            }
        }

        private void AddProducer(ControlBuildTask task)
        {
            string? role = task.Name switch
            {
                "WriteLinesToFile" => "File",
                "WriteCodeFragment" => "OutputFile",
                "GenerateMSBuildEditorConfig" => "FileName",
                "Nerdbank.GitVersioning.Tasks.AssemblyVersionInfo" => "OutputFile",
                "Microsoft.SourceLink.Common.GenerateSourceLinkFile" => "OutputFile",
                "CreateAppHost" => "AppHostDestinationPath",
                "GenerateDepsFile" => "DepsFilePath",
                "GenerateRuntimeConfigurationFiles" => "RuntimeConfigPath",
                _ => null,
            };
            if (role is not null)
                foreach (string output in Paths(task, role)) AddGenerated(output, task.Name, []);
            if (task.Name == "Csc")
            {
                foreach (string outputRole in new[] { "OutputAssembly", "OutputRefAssembly",
                    "PdbFile", "DocumentationFile" })
                    foreach (string output in Paths(task, outputRole))
                        AddGenerated(output, "Csc", []);
                // The maintained Csc contract derives the default PDB from OutputAssembly.
                // This retains compiler output identity, never a guessed source identity.
                if (Paths(task, "PdbFile").Length == 0 &&
                    Values(task, "EmitDebugInformation").SequenceEqual(["True"],
                        StringComparer.OrdinalIgnoreCase) &&
                    !Values(task, "DebugType").Contains("embedded",
                        StringComparer.OrdinalIgnoreCase))
                    foreach (string assembly in Paths(task, "OutputAssembly"))
                        AddGenerated(Path.ChangeExtension(assembly, ".pdb"), "Csc", []);
            }
            if (task.Name == "Copy")
            {
                string[] inputs = Paths(task, "SourceFiles");
                string[] outputs = CopyOutputs(task);
                if (inputs.Length != outputs.Length)
                    throw new InvalidDataException(
                        "Native copy input/output association is incomplete.");
                for (int i = 0; i < inputs.Length; i++)
                    AddGenerated(outputs[i], "Copy", [inputs[i]]);
            }
        }

        private void ReadCopy(ControlBuildTask task)
        {
            foreach (string input in Paths(task, "SourceFiles"))
                AddFile(input, "runtime copy source");
        }

        private string[] CopyOutputs(ControlBuildTask task)
        {
            string[] explicitOutputs = Paths(task, "DestinationFiles");
            ControlBuildParameter[] outputRoles = task.Parameters.Where(p =>
                p.Kind == TaskParameterMessageKind.TaskOutput &&
                p.Name == "DestinationFiles").ToArray();
            if (outputRoles.Length == 0 && explicitOutputs.Length == 0)
                throw new InvalidDataException("Native folder copy omitted its output roles.");
            if (outputRoles.Length == 0) return explicitOutputs;
            string[] outputs = outputRoles[0].Values.Select(v =>
                NativePath(task, v.Identity)).ToArray();
            if (outputRoles.Skip(1).Any(p => !p.Values.Select(v =>
                NativePath(task, v.Identity)).SequenceEqual(outputs, comparer)))
                throw new InvalidDataException("Conflicting native copy output roles.");
            if (explicitOutputs.Length != 0 && !explicitOutputs.SequenceEqual(outputs, comparer))
                throw new InvalidDataException("Native copy outputs differ from their input role.");
            return outputs;
        }

        private static string NativePath(ControlBuildTask task, string identity)
        {
            if (string.IsNullOrWhiteSpace(identity))
                throw new InvalidDataException("Native file identity is unavailable.");
            return Path.GetFullPath(identity.Replace('\\', Path.DirectorySeparatorChar),
                Path.GetDirectoryName(task.Project)!);
        }

        private static string[] Paths(ControlBuildTask task, string role) => task.Parameters
            .Where(p => p.Kind == TaskParameterMessageKind.TaskInput && p.Name == role)
            .SelectMany(p => p.Values).Where(v => !string.IsNullOrWhiteSpace(v.Identity))
            .Select(v => NativePath(task, v.Identity)).ToArray();

        private static string[] Values(ControlBuildTask task, string role) => task.Parameters
            .Where(p => p.Kind == TaskParameterMessageKind.TaskInput && p.Name == role)
            .SelectMany(p => p.Values).Select(v => v.Identity).ToArray();

        private void AddGenerated(string output, string producer, string[] inputs)
        {
            if (!Below(operation.IntermediateDirectory, output) &&
                !Below(operation.OutputDirectory, output) &&
                !Below(operation.BaseIntermediateDirectory, output))
                throw new InvalidDataException("Unsupported native generated-output location.");
            if (producers.TryGetValue(output, out Producer? previous))
            {
                if (previous.Name != producer || !previous.Inputs.SequenceEqual(inputs, comparer))
                    throw new InvalidDataException("Conflicting native generated producers.");
            }
            else producers.Add(output, new(producer, inputs));
        }

        private void AddFile(string path, string role, bool optional = false)
        {
            path = Path.GetFullPath(path);
            if (producers.TryGetValue(path, out Producer? producer))
            {
                if (!visiting.Add(path))
                    throw new InvalidDataException("Cyclic native generated-input relation.");
                generated.Add(new(path, producer.Name));
                foreach (string input in producer.Inputs) AddFile(input, role);
                visiting.Remove(path);
                return;
            }
            if (Below(checkout.Root, path))
            {
                string relative = Path.GetRelativePath(checkout.Root, path)
                    .Replace(Path.DirectorySeparatorChar, '/');
                if (entries.TryGetValue(relative, out GitEntry? entry) &&
                    entry.ObjectType == "blob" && entry.Mode is "100644" or "100755" &&
                    File.Exists(Path.Combine(checkout.Root, entry.Path)))
                    sources.Add(new(entry.Path, role, true));
                else if (optional && !entries.ContainsKey(relative) && !File.Exists(path) &&
                    !Directory.Exists(path))
                    sources.Add(new(relative, role, false));
                else
                    throw new InvalidDataException(
                        "Unresolved native repository input: " + relative);
                return;
            }
            if (optional && !File.Exists(path) && !Directory.Exists(path)) return;
            AddExternal(path);
        }

        private void AddRestoreConfiguration(string path)
        {
            if (!Path.IsPathFullyQualified(path) || !File.Exists(path))
                throw new InvalidDataException("Native restore configuration is unavailable.");
            if (Below(checkout.Root, path)) AddFile(path, "native restore configuration");
            else external.Add(new(path, "native restore environment"));
        }

        private void AddExternal(string path)
        {
            string? identity = Below(operation.SdkDirectory, path) ? "selected SDK" : null;
            if (identity is null)
            {
                ControlDependencyDirectory[] owners = dependencies.Where(d =>
                    d.Files.Contains(path, comparer) && Below(d.Directory, path)).ToArray();
                if (owners.Length != 1)
                    throw new InvalidDataException("Unresolved native external input: " + path);
                identity = owners[0].Identity;
            }
            external.Add(new(path, identity));
        }

        private void RequireImplementation(ControlBuildTask task)
        {
            string relative = Path.GetRelativePath(operation.SdkDirectory,
                task.Implementation).Replace(Path.DirectorySeparatorChar, '/');
            bool supported = task.Name switch
            {
                "Csc" or "CopyRefAssembly" or "GenerateMSBuildEditorConfig" or
                    "Microsoft.CodeAnalysis.BuildTasks.MapSourceRoots" =>
                    relative == "Roslyn/Microsoft.Build.Tasks.CodeAnalysis.dll",
                "MSBuild" or "CallTarget" => relative == "Microsoft.Build.dll",
                "CheckForDuplicateNuGetItemsTask" or "GetRestoreProjectStyleTask" =>
                    relative == "NuGet.Build.Tasks.dll",
                "Copy" => relative == "Microsoft.Build.Tasks.Core.dll" ||
                    PackageImplementation("Microsoft.Build.CopyOnWrite",
                        "build/netstandard2.0/Microsoft.Build.CopyOnWrite.dll"),
                _ when task.Name.StartsWith("Nerdbank.GitVersioning.Tasks.",
                    StringComparison.Ordinal) => PackageImplementation("Nerdbank.GitVersioning",
                        "build/MSBuildCore/Nerdbank.GitVersioning.Tasks.dll"),
                "DotNet.ReproducibleBuilds.Isolated.ValidateGlobalJsonSdkVersion" =>
                    PackageImplementation("DotNet.ReproducibleBuilds.Isolated",
                        "tasks/net6.0/DotNet.ReproducibleBuilds.Isolated.dll"),
                _ when task.Name.StartsWith("Microsoft.SourceLink.", StringComparison.Ordinal) =>
                    SourceLinkImplementation(),
                _ when task.Name.StartsWith("Microsoft.Build.Tasks.Git.",
                    StringComparison.Ordinal) => SourceControlImplementation(
                        "Microsoft.Build.Tasks.Git"),
                _ when CoreTasks.Contains(
                    task.Name) => relative == "Microsoft.Build.Tasks.Core.dll",
                _ when DotNetTasks.Contains(task.Name) =>
                    relative == "Sdks/Microsoft.NET.Sdk/tools/" +
                        operation.Dimension.TargetFramework +
                        "/Microsoft.NET.Build.Tasks.dll",
                _ => false,
            };
            if (!supported)
                throw new InvalidDataException(
                    "Unsupported native task implementation: " + task.Name);

            bool SourceLinkImplementation()
            {
                string package = task.Name[..task.Name.LastIndexOf('.')];
                return SourceControlImplementation(package);
            }

            bool SourceControlImplementation(string package) =>
                relative == "Sdks/" + package + "/tools/net/" + package + ".dll" ||
                PackageImplementation(package, "tools/net/" + package + ".dll");

            bool PackageImplementation(string package, string assembly) => dependencies.Any(d =>
                d.Identity.StartsWith(package + "/", StringComparison.OrdinalIgnoreCase) &&
                comparer.Equals(task.Implementation, Path.Combine(d.Directory,
                    assembly.Replace('/', Path.DirectorySeparatorChar))) &&
                d.Files.Contains(task.Implementation, comparer));
        }

        private void AddConfigurationCandidates()
        {
            AddFile(Path.Combine(checkout.Root, "global.json"), "SDK configuration", true);
            AddFile(Path.Combine(Path.GetDirectoryName(operation.Project)!, "packages.lock.json"),
                "locked restore", true);
            string directory = Path.GetDirectoryName(operation.Project)!;
            while (Below(checkout.Root, directory) || comparer.Equals(checkout.Root, directory))
            {
                foreach (string name in new[] { "Directory.Build.props", "Directory.Build.targets",
                    "Directory.Packages.props", "NuGet.Config", "nuget.config" })
                    AddFile(Path.Combine(directory, name), "configuration search candidate", true);
                if (comparer.Equals(checkout.Root, directory)) break;
                directory = Path.GetDirectoryName(directory)!;
            }
        }

        private static bool Below(string directory, string path)
        {
            string relative = Path.GetRelativePath(directory, path);
            return relative != "." && !Path.IsPathRooted(relative) && relative != ".." &&
                !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
        }
    }
}
