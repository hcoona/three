using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Build.Evaluation;
using NuGet.Commands;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Frameworks;
using NuGet.LibraryModel;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.ProjectModel;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Ecosystems;

internal sealed record DotNetSmokeRemoteConsumerResult(string Output, int Reads,
    NativeCommandResult[] Commands);

// The caller supplies a separate no-write process/job. Only HTTP receives its read capability.
internal static class DotNetSmokeRemoteConsumer
{
    internal const string ServiceIndex = "https://nuget.pkg.github.com/hcoona/index.json";
    internal const string Package = "Hcoona.ReleaseSmoke.GithubPackages";

    internal static async Task<DotNetSmokeRemoteConsumerResult> RunAsync(
        DotNetOriginalPackage original,
        Dictionary<string, string> native, string root, string credential,
        CancellationToken cancellation, HttpMessageHandler? controlledTransport = null,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>>? execute = null)
    {
        DotNetPackageConsumer.RequireFile(original.Path);
        DotNetPackageArchive.RequireOriginal(original);
        PackageIdentity identity = DotNetPackageArchive.Identity(original.Path);
        DotNetPackageArchive.RequireIdentity(identity, native);
        if (!StringComparer.OrdinalIgnoreCase.Equals(identity.Id, Package) ||
            native["TargetFramework"] != "net10.0" || original.Size > 25 * 1024 * 1024 ||
            string.IsNullOrEmpty(credential) || credential.Any(c => c is '\r' or '\n' or '\0') ||
            !Path.IsPathFullyQualified(root) || !Directory.Exists(root) ||
            Directory.EnumerateFileSystemEntries(root).Any())
            throw new InvalidDataException("Unsupported remote NuGet consumer input.");
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        for (DirectoryInfo? item = new(root); item is not null; item = item.Parent)
            if ((item.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Linked remote consumer directory.");
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        lifetime.CancelAfter(TimeSpan.FromMinutes(10));
        CancellationToken token = lifetime.Token;
        var commands = new List<NativeCommandResult>();
        string consumer = Directory.CreateDirectory(Path.Combine(root, "consumer")).FullName;
        string packages = Directory.CreateDirectory(Path.Combine(root, "packages")).FullName;
        string http = Directory.CreateDirectory(Path.Combine(root, "http")).FullName;
        string temp = Directory.CreateDirectory(Path.Combine(root, "temp")).FullName;
        Dictionary<string, string?> environment = EnvironmentFor(root, packages, http, temp);
        string project = Path.Combine(consumer, "Consumer.csproj");
        string config = Path.Combine(consumer, "NuGet.Config");
        var range = new VersionRange(identity.Version, true, identity.Version, true);
        new XDocument(new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
            new XElement("PropertyGroup",
                new XElement("OutputType", "Exe"),
                new XElement("TargetFramework", "net10.0"),
                new XElement("UseAppHost", "false"),
                new XElement("ImportDirectoryBuildProps", "false"),
                new XElement("ImportDirectoryBuildTargets", "false"),
                new XElement("ImportDirectoryPackagesProps", "false"),
                new XElement("ManagePackageVersionsCentrally", "false"),
                new XElement("DisableImplicitLibraryPacksFolder", "true"),
                new XElement("RestoreFallbackFolders", ""),
                new XElement("RestoreAdditionalProjectFallbackFolders", ""),
                new XElement("RestoreAdditionalProjectSources", ""),
                new XElement("RestorePackagesPath", packages),
                new XElement("NuGetAudit", "false")),
            new XElement("ItemGroup", new XElement("PackageReference",
                new XAttribute("Include", identity.Id),
                new XAttribute("Version", range.ToNormalizedString()))))).Save(project);
        new XDocument(new XElement("configuration",
            new XElement("packageSources", new XElement("clear"), new XElement("add",
                new XAttribute("key", "selected"), new XAttribute("value", ServiceIndex))),
            new XElement("packageSourceMapping", new XElement("clear"),
                new XElement("packageSource", new XAttribute("key", "selected"),
                    new XElement("package", new XAttribute("pattern", identity.Id)))),
            new XElement("disabledPackageSources", new XElement("clear")),
            new XElement("fallbackPackageFolders", new XElement("clear")))).Save(config);
        await File.WriteAllTextAsync(Path.Combine(consumer, "global.json"),
            JsonSerializer.Serialize(new { sdk = new { version = native["NETCoreSdkVersion"],
                rollForward = "disable" } }), token);
        await File.WriteAllTextAsync(Path.Combine(consumer, "Program.cs"),
            "System.Console.WriteLine(HcoonaReleaseSmokeGithubPackages.Smoke.ProjectId);", token);
        await RunAsync(["--version"], 30, native["NETCoreSdkVersion"]);
        string graphPath = Path.Combine(consumer, "graph.json");
        await RunAsync(["msbuild", project, "-nologo", "-noAutoResponse", "-nodeReuse:false",
            "-target:GenerateRestoreGraphFile", "-property:RestoreGraphOutputPath=" +
                ProjectCollection.Escape(graphPath), "-property:RestoreConfigFile=" +
                ProjectCollection.Escape(config)], 60);
        DotNetPackageConsumer.RequireFile(graphPath);
        if (OwnedFile.Length(graphPath) > 2 * 1024 * 1024)
            throw new InvalidDataException("Native consumer graph exceeds its limit.");
        DependencyGraphSpec graph = DependencyGraphSpec.Load(graphPath);
        RequireGraph(graph, project, config, packages, identity);
        using var restoreLifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        restoreLifetime.CancelAfter(TimeSpan.FromSeconds(90));
        using var standard = new HttpClientHandler
        {
            AllowAutoRedirect = false, UseCookies = false, UseProxy = false,
            UseDefaultCredentials = false,
            AutomaticDecompression = System.Net.DecompressionMethods.None,
        };
        using var handler = new NuGetConsumerReadHandler(credential,
            controlledTransport ?? standard, restoreLifetime.Token);
        var handlerResource = new HttpHandlerResourceV3(standard, handler);
        using var source = new HttpSource(new PackageSource(ServiceIndex, "selected"),
            () => Task.FromResult<HttpHandlerResource>(handlerResource), NullThrottle.Instance)
            { HttpCacheDirectory = http };
        var resources = new NuGet.Protocol.Core.Types.Repository.ProviderFactory().GetCoreV3()
            .Where(item =>
            item.Value is not HttpSourceResourceProvider and not HttpHandlerResourceV3Provider
                and not DownloadResourcePluginProvider and not PluginFindPackageByIdResourceProvider
                and not PluginResourceProvider)
            .ToList();
        resources.Add(new Lazy<INuGetResourceProvider>(() => new ConsumerSourceProvider(source)));
        var provider = new CachingSourceProvider(new PackageSourceProvider(NullSettings.Instance));
        provider.AddSourceRepository(new SourceRepository(new PackageSource(ServiceIndex,
            "selected"), resources));
        using var cache = new SourceCacheContext
        {
            NoCache = true, DirectDownload = true,
            GeneratedTempFolder = Directory.CreateDirectory(Path.Combine(root, "restore-temp"))
                .FullName,
        };
        var arguments = new RestoreArgs
        {
            ConfigFile = config, GlobalPackagesFolder = packages, DisableParallel = true,
            AllowNoOp = false, CacheContext = cache, CachingSourceProvider = provider,
            Log = NullLogger.Instance,
        };
        arguments.PreLoadedRequestProviders.Add(new DependencyGraphSpecRequestProvider(
            new RestoreCommandProvidersCache(), graph,
            Settings.LoadSpecificSettings(consumer, "NuGet.Config")));
        IReadOnlyList<RestoreSummary> summaries = [];
        Exception? restoreFailure = null;
        try
        {
            summaries = await RestoreRunner.RunAsync(arguments, restoreLifetime.Token);
        }
        catch (Exception failure)
        {
            restoreFailure = failure;
        }
        // Native cancellation can return before initiated HTTP reads finish unwinding.
        try { await handler.CompleteAsync(); }
        catch (Exception cleanupFailure) when (restoreFailure is not null)
        {
            throw new AggregateException("Native restore and owned read cleanup failed.",
                restoreFailure, cleanupFailure);
        }
        if (restoreFailure is not null) ExceptionDispatchInfo.Throw(restoreFailure);
        restoreLifetime.Token.ThrowIfCancellationRequested();
        if (summaries.Count != 1 || !summaries[0].Success)
            throw new InvalidDataException("Selected remote native restore did not succeed.");
        RequireAssets(Path.Combine(consumer, "obj", "project.assets.json"), project, config,
            packages, identity, original);
        string response = Path.Combine(consumer, "build.json");
        await RunAsync(["msbuild", project, "-nologo", "-noAutoResponse", "-nodeReuse:false",
            "-target:Build,GetTargetPath", "-getTargetResult:Build,GetTargetPath",
            "-getResultOutputFile:" + response], 120);
        using JsonDocument document = await DotNetPackageResults.ResponseAsync(response, token);
        DotNetPackageResults.Target(document.RootElement, "Build");
        JsonElement target = DotNetPackageResults.Target(document.RootElement, "GetTargetPath");
        JsonElement items = target.GetProperty("Items");
        if (items.GetArrayLength() != 1)
            throw new InvalidDataException("Missing unique native consumer output.");
        string output = items[0].GetProperty("FullPath").GetString()!;
        if (!Path.IsPathFullyQualified(output) || !Path.GetFullPath(output).StartsWith(consumer +
                Path.DirectorySeparatorChar, OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException("Native consumer output is outside its directory.");
        DotNetPackageConsumer.RequireFile(output);
        await RunAsync([output], 30, DotNetPackageConsumer.Marker);
        DotNetPackageArchive.RequireOriginal(original);
        return new(output, handler.Reads, [.. commands]);

        async Task RunAsync(string[] argv, int seconds, string? expected = null)
        {
            NativeCommandResult result = await (execute ?? NativeProcess.ExecuteAsync)(new(
                "dotnet", consumer, argv, seconds, environment, false), token);
            commands.Add(result);
            if (!result.Succeeded || expected is not null && result.Stdout.Trim() != expected)
            {
                string failure = "Native remote consumer command failed: " +
                    result.Termination + "; exit " + result.ExitCode;
                if (result.Termination == NativeTermination.Cancelled)
                    throw new OperationCanceledException(failure, token);
                if (result.Termination == NativeTermination.TimedOut)
                    throw new TimeoutException(failure);
                throw new InvalidDataException(failure);
            }
        }
    }

    internal static void RequireGraph(DependencyGraphSpec graph, string project, string config,
        string packages, PackageIdentity identity)
    {
        if (graph.Projects is not [var spec] || !graph.Restore.SequenceEqual([project]))
            throw new InvalidDataException("Unsupported native consumer project closure.");
        ProjectRestoreMetadata restore = spec.RestoreMetadata;
        RequireMetadata(restore, project, config, packages);
        if (!SamePath(restore.OutputPath, Path.Combine(Path.GetDirectoryName(project)!, "obj")) ||
            spec.TargetFrameworks is not [var framework] ||
            !framework.FrameworkName.Equals(NuGetFramework.ParseFolder("net10.0")) ||
            framework.Dependencies is not [var dependency] ||
            dependency.LibraryRange.TypeConstraint != LibraryDependencyTarget.Package ||
            !StringComparer.OrdinalIgnoreCase.Equals(dependency.Name, identity.Id) ||
            dependency.LibraryRange.VersionRange is null ||
            !dependency.LibraryRange.VersionRange.Equals(new VersionRange(identity.Version, true,
                identity.Version, true)) || framework.DownloadDependencies.Any() ||
            restore.TargetFrameworks.Any(item => item.ProjectReferences.Count != 0))
            throw new InvalidDataException("Unsupported native consumer dependency scope.");
    }

    internal static void RequireAssets(string path, string project, string config, string packages,
        PackageIdentity identity, DotNetOriginalPackage original)
    {
        DotNetPackageConsumer.RequireFile(path);
        LockFile assets = new LockFileFormat().Read(path);
        RequireMetadata(assets.PackageSpec?.RestoreMetadata, project, config, packages);
        if (assets.Version != LockFileFormat.Version || assets.PackageFolders is not [var folder] ||
            !SamePath(folder.Path, packages) || assets.Libraries is not [var library] ||
            library.Type != "package" || !PackageIdentityComparer.Default.Equals(identity,
                new PackageIdentity(library.Name, library.Version)) || assets.Targets is not
                [var target] ||
            !target.TargetFramework.Equals(NuGetFramework.ParseFolder("net10.0")) ||
            !string.IsNullOrEmpty(target.RuntimeIdentifier) ||
            target.Libraries is not [var installed] ||
            installed.Type != "package" || string.IsNullOrEmpty(installed.Name) ||
            !PackageIdentityComparer.Default.Equals(identity,
                new PackageIdentity(installed.Name, installed.Version)))
            throw new InvalidDataException("Native remote consumer assets differ from request.");
        string archive = new VersionFolderPathResolver(packages)
            .GetPackageFilePath(identity.Id, identity.Version);
        DotNetPackageConsumer.RequireFile(archive);
        if (OwnedFile.Length(archive) != original.Size || DotNetPackageArchive.Digest(archive) !=
                original.Sha256)
            throw new InvalidDataException("Installed remote original bytes differ from request.");
    }

    private static void RequireMetadata(ProjectRestoreMetadata? restore, string project,
        string config, string packages)
    {
        if (restore is null || restore.ProjectStyle != ProjectStyle.PackageReference ||
            !SamePath(restore.ProjectPath, project) ||
            !SamePath(restore.ProjectUniqueName, project) ||
            !SamePath(restore.PackagesPath, packages) || restore.FallbackFolders.Count != 0 ||
            restore.Sources is not [var source] || source.Source != ServiceIndex ||
            restore.ConfigFilePaths is not [string selected] || !SamePath(selected, config) ||
            !restore.OriginalTargetFrameworks.SequenceEqual(["net10.0"]))
            throw new InvalidDataException("Native remote consumer restore inputs differ.");
    }

    private static bool SamePath(string left, string right) => Path.IsPathFullyQualified(left) &&
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)).Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static Dictionary<string, string?> EnvironmentFor(string root, string packages,
        string http, string temp)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (string name in new[] { "PATH", "SystemRoot", "WINDIR", "PATHEXT", "ProgramFiles",
            "ProgramFiles(x86)", "DOTNET_ROOT",
            "DOTNET_ROOT_X64" })
            if (Environment.GetEnvironmentVariable(name) is { } value) values[name] = value;
        string profile = Directory.CreateDirectory(Path.Combine(root, "profile")).FullName;
        foreach (string name in new[] { "HOME", "USERPROFILE" }) values[name] = profile;
        foreach ((string name, string path) in new[] { ("APPDATA", "appdata"),
            ("LOCALAPPDATA", "localappdata"), ("XDG_CONFIG_HOME", "config"),
            ("XDG_DATA_HOME", "data"), ("XDG_CACHE_HOME", "cache") })
            values[name] = Directory.CreateDirectory(Path.Combine(profile, path)).FullName;
        values["DOTNET_CLI_HOME"] = Directory.CreateDirectory(Path.Combine(root, "cli")).FullName;
        values["NUGET_PACKAGES"] = packages;
        values["NUGET_HTTP_CACHE_PATH"] = http;
        values["NUGET_PLUGINS_CACHE_PATH"] =
            Directory.CreateDirectory(Path.Combine(root, "plugins")).FullName;
        foreach (string name in new[] { "TMP", "TEMP", "TMPDIR" }) values[name] = temp;
        values["DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER"] = "1";
        values["MSBUILDDISABLENODEREUSE"] = "1";
        values["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        values["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        values["DOTNET_GENERATE_ASPNET_CERTIFICATE"] = "false";
        values["DOTNET_NOLOGO"] = "true";
        return values;
    }

    private sealed class ConsumerSourceProvider(HttpSource source) : ResourceProvider(
        typeof(HttpSourceResource), nameof(ConsumerSourceProvider),
        NuGetResourceProviderPositions.Last)
    {
        public override Task<Tuple<bool, INuGetResource?>> TryCreate(SourceRepository repository,
            CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (repository.PackageSource.Source != ServiceIndex)
                throw new InvalidDataException("Different native consumer source.");
            return Task.FromResult(Tuple.Create<bool, INuGetResource?>(true,
                new HttpSourceResource(source)));
        }
    }
}
