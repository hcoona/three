using System.Security;
using System.Text.Json;
using System.Xml.Linq;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Tests.Repository;

internal sealed class DotNetGraphTaskFixture : IDisposable
{
    internal static string Properties(params string[] properties) =>
        "<PropertyGroup>" + string.Concat(properties) + "</PropertyGroup>";

    internal static string Items(params string[] items) =>
        "<ItemGroup>" + string.Concat(items) + "</ItemGroup>";

    internal static string References(params string[] projects) => Items(projects.Select(path =>
        $"""<ProjectReference Include="{path}" />""").ToArray());

    internal const string Revision = "1111111111111111111111111111111111111111";
    private static readonly string[] ProjectExtensions = [".csproj", ".fsproj", ".vbproj"];
    internal static string NbgvVersion =>
        RepositoryToolSelection.GlobalPackageVersion("Nerdbank.GitVersioning");
    internal string Root { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
        "workflow-native-graph-" + Guid.NewGuid().ToString("N"));
    internal Dictionary<string, string> Globals { get; } = new(StringComparer.Ordinal)
    {
        ["Configuration"] = "Debug",
        ["ContinuousIntegrationBuild"] = "true",
        ["RestoreLockedMode"] = "true",
    };
    internal string[] CommittedPaths { get; set; } = [];
    internal string? RequestRoot { get; set; }
    internal bool IncludeRootConfiguration { get; set; } = true;
    private bool _restoreTraversal;
    private readonly HashSet<string> _projects = new(StringComparer.Ordinal);
    private bool _prepared;

    internal DotNetGraphTaskFixture()
    {
        Directory.CreateDirectory(Root);
        Write("global.json", RepositoryToolSelection.GlobalJson());
        Write("nuget.config", """
            <configuration><packageSources><clear />
              <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
            </packageSources></configuration>
            """);
        BuildProperties();
    }
    internal void BuildProperties(string extra = "", bool traversal = false,
        string pathPrefix = "", string items = "")
    {
        string lockCondition = traversal
            ? " Condition=\"'$(MSBuildProjectExtension)' != '.proj'\"" : "";
        WriteProject("Directory.Build.props", Properties(
            $"<BaseIntermediateOutputPath>{pathPrefix}obj/$(MSBuildProjectName)/" +
                "</BaseIntermediateOutputPath>",
            $"<RestorePackagesWithLockFile{lockCondition}>true</RestorePackagesWithLockFile>",
            $"<NuGetLockFilePath>{pathPrefix}packages.$(MSBuildProjectName).lock.json" +
                "</NuGetLockFilePath>",
            "<NuGetAudit>false</NuGetAudit>", extra) + items);
    }

    internal void Entry(params string[] projects) =>
        WriteProject("dirs.proj", References(projects));

    internal void ControlProject(string body)
    {
        Entry(NodeGroupReader.ControlProject);
        SdkProject(NodeGroupReader.ControlProject, body: body);
    }

    internal void SdkProject(string name, string frameworks = "net10.0", string body = "",
        string before = "", string sdk = "Microsoft.NET.Sdk", bool restoreProject = true)
    {
        string framework = frameworks.Contains(';') ? "TargetFrameworks" : "TargetFramework";
        WriteProject(name, before + Properties($"<{framework}>{frameworks}</{framework}>") +
            body, sdk, restoreProject);
    }

    internal void WriteProject(string name, string body, string? sdk = null,
        bool restoreProject = true) => Write(name,
        (sdk is null ? "<Project>" : $"""<Project Sdk="{sdk}">""") + body + "</Project>",
        restoreProject);

    internal void Commit(params string[] paths) => CommittedPaths = [.. paths.Select(PathOf)];

    internal string PathOf(string name) => System.IO.Path.GetFullPath(
        System.IO.Path.Combine(Root, name));
    internal void Write(string name, string content, bool restoreProject = true)
    {
        string path = PathOf(name);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        if (restoreProject && ProjectExtensions.Contains(
            System.IO.Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            _projects.Add(path);
    }

    internal void ConfigureNbgv(bool rootVersionBase)
    {
        EnableTraversalRestore();
        const string auxiliaryPath = "$(MSBuildThisFileDirectory)shared/nbgv-aux.targets";
        const string sourceLinkCommon = "$(PkgMicrosoft_SourceLink_Common)/" +
            "build/Microsoft.SourceLink.Common.props";
        Write("global.json", RepositoryToolSelection.GlobalJson("Microsoft.Build.Traversal"));
        BuildProperties($"""
            <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
            <GitVersionBaseDirectory>$(MSBuildThisFileDirectory)</GitVersionBaseDirectory>
            <NBGV_PrivateP2PAuxTargets Condition="'$(MSBuildProjectName)' == 'consumer'"
              >{auxiliaryPath}</NBGV_PrivateP2PAuxTargets>
            """, traversal: true, pathPrefix: "$(MSBuildThisFileDirectory)", items: Items($"""
            <GlobalPackageReference Include="DotNet.ReproducibleBuilds" Version="2.0.5" />
            <GlobalPackageReference Include="Nerdbank.GitVersioning" Version="{NbgvVersion}" />
            <GlobalPackageReference Include="Microsoft.SourceLink.GitHub" Version="10.0.401" />
            """));
        Write("Directory.Packages.props", "<Project />");
        // Explicitly consume a restored package asset rather than the SDK's SourceLink.
        SdkProject("dirs.proj", sdk: "Microsoft.Build.Traversal",
            body: References("product/consumer.csproj") + $"""
            <Import Project="{sourceLinkCommon}"
              Condition="'$(PkgMicrosoft_SourceLink_Common)' != ''" />
            """);
        string versionBase = rootVersionBase ? "" :
            "<GitVersionBaseDirectory>$(MSBuildProjectDirectory)</GitVersionBaseDirectory>";
        SdkProject("product/consumer.csproj", "net10.0;net9.0", Properties(versionBase) +
            Items("""
                <ProjectReference Include="../dependency/dependency.csproj" />
                <ProjectReference Include="../analyzer/analyzer.csproj"
                  OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
                """));
        SdkProject("dependency/dependency.csproj", "net9.0", References("../leaf/leaf.csproj"));
        SdkProject("leaf/leaf.csproj", "net9.0");
        SdkProject("analyzer/analyzer.csproj");
        Write("version.json", """{"version":"1.0"}""");
        Write("product/version.json", """{"version":"2.0"}""");
        Write("shared/nbgv-aux.targets", """
            <Project><ItemGroup>
              <AdditionalFiles Include="$(MSBuildThisFileDirectory)aux-input.txt" />
            </ItemGroup></Project>
            """);
        Write("shared/aux-input.txt", "Committed helper input.");
        Commit("global.json", "Directory.Build.props", "Directory.Packages.props", "dirs.proj",
            "product/consumer.csproj", "dependency/dependency.csproj", "leaf/leaf.csproj",
            "analyzer/analyzer.csproj", "version.json", "product/version.json",
            "shared/nbgv-aux.targets", "shared/aux-input.txt");
    }

    internal void EnableTraversalRestore() => _restoreTraversal = true;

    internal async Task PrepareAsync(CancellationToken token)
    {
        if (_prepared) return;
        if (_projects.Count != 0)
        {
            string solution = PathOf("restore.slnx");
            new XDocument(new XElement("Solution",
                _projects.Order(StringComparer.Ordinal).Select(project =>
                    new XElement("Project", new XAttribute("Path", project)))))
                .Save(solution);
            // Observed on the pinned SDK 10.0.401 (not a documented contract): locked mode
            // writes absent fixture locks. If that changes, commit literal locks as data.
            await RestoreAsync(["restore", solution, "--locked-mode",
                "-property:Configuration=Debug", "-property:ContinuousIntegrationBuild=true"],
                token);
        }
        if (_restoreTraversal)
            await RestoreAsync(["restore", PathOf("dirs.proj"), "--locked-mode",
                "-property:Configuration=Debug", "-property:ContinuousIntegrationBuild=true"],
                token);
        _prepared = true;
    }

    private async Task RestoreAsync(string[] arguments, CancellationToken token)
    {
        // MSTest already runs independent fixture restores in parallel.
        NativeCommandResult result = await NativeProcess.ExecuteAsync(
            new("dotnet", Root, [.. arguments, DotNetCli.DisableBuildServers, "-maxcpucount:1"],
                30), token);
        token.ThrowIfCancellationRequested();
        if (!result.Succeeded)
            throw new InvalidDataException(
                $"Fixture restore {result.Termination}, exit {result.ExitCode}: " +
                result.Error + "\n" + result.Stdout + result.Stderr);
    }

    internal async Task<DotNetGraphResponse> ReadAsync(CancellationToken token)
    {
        await PrepareAsync(token);
        Write("request.json", JsonSerializer.Serialize(new DotNetGraphRequest(Revision,
            RequestRoot ?? Root, PathOf("dirs.proj"), Globals, PathOf("response.json"),
            IncludeRootConfiguration ? [.. CommittedPaths, PathOf("nuget.config")]
                : CommittedPaths),
            DotNetNativeJson.Default.DotNetGraphRequest));
        string assembly = SecurityElement.Escape(typeof(DotNetGraphTask).Assembly.Location)!;
        string request = SecurityElement.Escape(PathOf("request.json"))!;
        Write("read.proj", $"""
            <Project>
              <UsingTask TaskName="WorkflowDelivery.Repository.DotNetGraphTask"
                         AssemblyFile="{assembly}" />
              <Target Name="Read">
                <WorkflowDelivery.Repository.DotNetGraphTask RequestFile="{request}" />
              </Target>
            </Project>
            """);
        NativeCommandResult query = await NativeProcess.ExecuteAsync(new("dotnet", Root,
            ["msbuild", DotNetCli.DisableBuildServers, PathOf("read.proj"), "-nologo",
                "-noAutoResponse", "-target:Read", "-property:WrapperSentinel=wrapper-only"],
            30), token);
        token.ThrowIfCancellationRequested();
        if (query.Termination == NativeTermination.TimedOut)
            throw new TimeoutException(query.Error);
        if (!query.Succeeded)
            throw new InvalidDataException(
                $"Native graph {query.Termination}, exit {query.ExitCode}: " +
                query.Error + "\n" + query.Stdout + query.Stderr);
        return JsonSerializer.Deserialize(await File.ReadAllTextAsync(
            PathOf("response.json"), token), DotNetNativeJson.Default.DotNetGraphResponse)
            ?? throw new InvalidDataException("Missing graph response.");
    }

    public void Dispose() => Directory.Delete(Root, true);
}
