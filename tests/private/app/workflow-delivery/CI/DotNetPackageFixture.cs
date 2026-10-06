using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Microsoft.Build.Evaluation;
using WorkflowDelivery.CI;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;
using GitFixture = WorkflowDelivery.Tests.Repository.GitReaderTests.GitFixture;

namespace WorkflowDelivery.Tests.CI;

internal sealed class DotNetPackageFixture(GitFixture repo, string candidate) : IDisposable
{
    internal const string Project = "product/product.csproj";
    internal const string Declaration = "product/workflow-delivery.release-unit.yml";
    internal GitFixture Repo { get; } = repo;
    internal string Candidate { get; } = candidate;
    internal string Scratch { get; } = Directory.CreateTempSubdirectory("workflow-nuget-").FullName;
    internal List<NativeCommand> Commands { get; } = [];
    internal Func<NativeCommand, NativeCommandResult?>? Override { get; set; }
    internal Action<JsonObject>? ModifyPack { get; set; }
    internal Action<string>? ModifyArchive { get; set; }
    internal Action<JsonObject>? ModifyAssets { get; set; }
    internal Action<string>? ModifyInstalled { get; set; }
    internal DotNetRunRequest Request => new(Repo.Directory,
        Directory.CreateDirectory(Path.Combine(Scratch, "run")).FullName, 60);
    internal static ReleaseUnitDeclaration Unit =>
        ReleaseUnitDeclarationReader.Read(UnitText, Declaration);
    internal DotNetGraphNode Node => new(new(Path.Combine(Repo.Directory, Project),
        new(StringComparer.Ordinal)
        {
            ["Configuration"] = "Debug",
            ["TargetFramework"] = "net10.0",
            ["ContinuousIntegrationBuild"] = "true",
            ["RestoreLockedMode"] = "true",
            ["Probe"] = "space%3Bsemi%25percent",
        }), false, new("Debug", "net10.0", ""), "", "", [], "None");
    internal Dictionary<string, string> Values => new(StringComparer.Ordinal)
    {
        ["MSBuildProjectFullPath"] = Path.Combine(Repo.Directory, Project),
        ["TargetFramework"] = "net10.0",
        ["RuntimeIdentifier"] = "",
        ["NETCoreSdkVersion"] = "10.0.401",
        ["IsPackable"] = "true",
        ["AssemblyName"] = typeof(DotNetPackageFixture).Assembly.GetName().Name!,
        ["DocumentationFile"] = "obj/Debug/WorkflowDelivery.Tests.xml",
        ["GenerateDocumentationFile"] = "true",
        ["PackageReadmeFile"] = "README.md",
        ["IncludeSymbols"] = "false",
        ["IncludeSource"] = "",
        ["PublishRepositoryUrl"] = "true",
        ["RepositoryUrl"] = "https://example.invalid/source",
        ["RepositoryType"] = "git",
        ["RepositoryCommit"] = "",
        ["PackageId"] = "WorkflowDelivery.Tests",
        ["NuGetPackageVersion"] = "1.0.0",
        ["PackageVersion"] = "1.0.0",
        ["AssemblyVersion"] = typeof(DotNetPackageFixture).Assembly.GetName().Version!.ToString(),
        ["AssemblyFileVersion"] = typeof(DotNetPackageFixture).Assembly
            .GetCustomAttribute<AssemblyFileVersionAttribute>()!.Version,
        ["AssemblyInformationalVersion"] = typeof(DotNetPackageFixture).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion,
    };
    internal DotNetPackageContext PackageContext => new(Project, Unit, Unit.Builds[0], Node);
    internal DotNetSelection Selection
    {
        get
        {
            var project = new ProjectFacts(Project, "product", [], [], Unit.Id,
                DotNetPackageChecks.Preset, [], [Project, Declaration]);
            var facts = new RepositoryFacts(Candidate, DotNetFactsAssembler.Scope, [project],
                [new(Declaration, [Project])], [], []);
            return new(new(facts, facts, [Project], false),
                new Dictionary<string, HashSet<SelectionReason>>
                { [Project] = [new(Project, Candidate, Project)] },
                new Dictionary<string, DotNetGraphNode[]> { [Project] = [Node] }, [Unit]);
        }
    }
    internal CiPlan Plan => new(Candidate, Candidate, DotNetFactsAssembler.Scope,
        DotNetPackageChecks.Expand(PackageContext, Values).Select(check => new PlannedCheck(
            check, [DotNetPackageChecks.Preset], [new(Project, Candidate, Project)],
            [CheckOrigin.Preset])).ToArray());

    internal const string UnitText = """
        schema: workflow-delivery/v3/release-unit
        release-unit: product
        builds:
          - id: package
            definition: dotnet/nuget-package-v1
            entry-point: product.csproj
            outputs:
              - id: package
                role: primary-package
                kind: nuget-package
        """;

    internal static async Task<DotNetPackageFixture> CreateAsync(CancellationToken token)
    {
        GitFixture repo = await GitFixture.CreateAsync(token);
        try
        {
            await repo.SetAsync(Project, "<Project />");
            await repo.SetAsync(Declaration, UnitText);
            string candidate = await repo.CommitAsync();
            await repo.GitAsync("reset", "--hard", candidate);
            return new(repo, candidate);
        }
        catch { repo.Dispose(); throw; }
    }

    internal Task<DotNetPackageRunResult> RunAsync(CancellationToken token, CiPlan? plan = null) =>
        DotNetPackageExecution.RunAsync(plan ?? Plan, Request, ExecuteAsync, token);

    internal async Task<NativeCommandResult> ExecuteAsync(NativeCommand command,
        CancellationToken token)
    {
        Commands.Add(command);
        if (Override?.Invoke(command) is { } result) return result;
        string? response = command.Arguments.SingleOrDefault(argument =>
            argument.StartsWith("-getResultOutputFile:", StringComparison.Ordinal))?
            ["-getResultOutputFile:".Length..];
        if (command.Arguments.Contains("-target:GetBuildVersion"))
            await File.WriteAllTextAsync(response!, JsonSerializer.Serialize(new
            {
                Properties = Values,
                TargetResults =
                    new
                    { GetBuildVersion = new { Result = "Success", Items = Array.Empty<string>() } }
            }), token);
        else if (command.Arguments.Contains("-target:Pack,_GetOutputItemsFromPack"))
        {
            string destination = ProjectCollection.Unescape(command.Arguments.Single(argument =>
                argument.StartsWith("-property:PackageOutputPath=", StringComparison.Ordinal))
                ["-property:PackageOutputPath=".Length..].Trim('"'));
            string path = Path.Combine(destination, "original.nupkg");
            CreateArchive(path);
            ModifyArchive?.Invoke(path);
            var document = JsonSerializer.SerializeToNode(new
            {
                Properties = Values,
                TargetResults = new
                {
                    Pack = new { Result = "Success", Items = Array.Empty<string>() },
                    _GetOutputItemsFromPack = new
                    {
                        Result = "Success",
                        Items =
                        new[] { new { Identity = path, FullPath = path } }
                    },
                }
            })!.AsObject();
            ModifyPack?.Invoke(document);
            await File.WriteAllTextAsync(response!, document.ToJsonString(), token);
        }
        else if (command.Arguments.Contains("restore"))
        {
            string root = Path.GetDirectoryName(command.Directory)!;
            string packages = command.Environment!["NUGET_PACKAGES"]!;
            string archive = Path.Combine(root, "feed", "original.nupkg");
            string installed = new NuGet.Packaging.VersionFolderPathResolver(packages)
                .GetPackageFilePath(Values["PackageId"],
                    NuGet.Versioning.NuGetVersion.Parse("1.0.0"));
            Directory.CreateDirectory(Path.GetDirectoryName(installed)!);
            File.Copy(archive, installed);
            ModifyInstalled?.Invoke(installed);
            string obj = Directory.CreateDirectory(Path.Combine(command.Directory, "obj")).FullName;
            string coordinate = Values["PackageId"] + "/1.0.0";
            var targets = new JsonObject
            {
                ["net10.0"] = new JsonObject
                { [coordinate] = new JsonObject { ["type"] = "package" } }
            };
            var assets = new JsonObject
            {
                ["version"] = NuGet.ProjectModel.LockFileFormat.Version,
                ["targets"] = targets,
                ["libraries"] = new JsonObject
                {
                    [coordinate] = new JsonObject
                    {
                        ["type"] = "package",
                        ["path"] = "workflowdelivery.tests/1.0.0",
                        ["files"] = new JsonArray()
                    }
                },
                ["packageFolders"] =
                    new JsonObject { [packages + Path.DirectorySeparatorChar] = new JsonObject() },
                ["project"] = new JsonObject
                {
                    ["version"] = "1.0.0",
                    ["restore"] = new JsonObject
                    {
                        ["projectUniqueName"] = Path.Combine(command.Directory, "Consumer.csproj"),
                        ["projectName"] = "Consumer",
                        ["projectPath"] = Path.Combine(command.Directory, "Consumer.csproj"),
                        ["projectStyle"] = "PackageReference",
                        ["packagesPath"] = packages,
                        ["configFilePaths"] =
                            new JsonArray(Path.Combine(command.Directory, "NuGet.Config")),
                        ["sources"] =
                            new JsonObject { [Path.Combine(root, "feed")] = new JsonObject() },
                        ["originalTargetFrameworks"] = new JsonArray("net10.0"),
                        ["frameworks"] = new JsonObject
                        {
                            ["net10.0"] = new JsonObject
                            {
                                ["framework"] = "net10.0",
                                ["targetAlias"] = "net10.0",
                                ["projectReferences"] = new JsonObject()
                            }
                        },
                    },
                    ["frameworks"] = new JsonObject
                    {
                        ["net10.0"] = new JsonObject
                        { ["targetAlias"] = "net10.0" }
                    },
                }
            };
            ModifyAssets?.Invoke(assets);
            await File.WriteAllTextAsync(Path.Combine(obj, "project.assets.json"),
                assets.ToJsonString(), token);
        }
        else if (response is not null)
        {
            string output = Path.Combine(command.Directory, "Consumer.dll");
            await File.WriteAllTextAsync(output, "native consumer output", token);
            await File.WriteAllTextAsync(response, JsonSerializer.Serialize(new
            {
                TargetResults = new
                {
                    Build = new { Result = "Success", Items = Array.Empty<string>() },
                    GetTargetPath = new
                    {
                        Result = "Success",
                        Items =
                        new[] { new { Identity = output, FullPath = output } }
                    }
                }
            }), token);
        }
        return new(NativeTermination.Exited, 0,
            response is null && !command.Arguments.Contains("restore") ?
                DotNetPackageConsumer.Marker + "\n" : "", "", 0.1, null);
    }

    internal void CreateArchive(string path)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        var nuspec = new XDocument(new XElement("package", new XElement("metadata",
            new XElement("id", Values["PackageId"]), new XElement("version", "1.0.0"),
            new XElement("authors", "fixture"), new XElement("description", "fixture"),
            new XElement("readme", "README.md"), new XElement("repository",
                new XAttribute("type", "git"), new XAttribute("url", Values["RepositoryUrl"])))));
        using (Stream output = zip.CreateEntry("product.nuspec").Open()) nuspec.Save(output);
        zip.CreateEntryFromFile(typeof(DotNetPackageFixture).Assembly.Location,
            "lib/net10.0/WorkflowDelivery.Tests.dll");
        Write("lib/net10.0/WorkflowDelivery.Tests.xml",
            "<doc><assembly><name>WorkflowDelivery.Tests</name></assembly></doc>");
        Write("README.md", "Native product fixture");
        void Write(string name, string content)
        {
            using var writer =
            new StreamWriter(zip.CreateEntry(name).Open()); writer.Write(content);
        }
    }

    public void Dispose()
    { Repo.Dispose(); Directory.Delete(Scratch, true); }
}
