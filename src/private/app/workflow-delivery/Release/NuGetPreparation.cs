using System.Text.Json;
using Microsoft.Build.Evaluation;
using WorkflowDelivery.Ecosystems;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Release;

// The adopted Release recipes use native versioning and packaging independently of CI. The
// smoke recipe packs one framework; the shared library recipe packs the native multi-target
// outer build of an ordinary registered library.
internal static class NuGetPreparation
{
    internal const string Preset = "dotnet/hcoona-release-smoke-github-packages-v1";
    internal static readonly string[] Required = ["native-input", "build", "pack", "contents",
        "consumer", "original-bytes"];
    private static readonly string[] Versions = ["PackageId", "NuGetPackageVersion",
        "PackageVersion", "AssemblyVersion", "AssemblyFileVersion", "AssemblyInformationalVersion"];
    private static readonly string[] TestProperties = ["IsTestProject",
        "IsTestingPlatformApplication"];
    private static readonly string[] Dimensions = ["-property:Configuration=Release",
        "-property:TargetFramework=", "-property:RuntimeIdentifier=",
        "-property:ContinuousIntegrationBuild=true", "-property:RestoreLockedMode=true"];
    internal const string SuppressCloudVariables =
        "-property:NBGV_SetCloudBuildVersionVars=false";

    internal static Task<ReleasePreparationResult> PrepareAsync(ReleasePreparationRequest request,
        CancellationToken token) => PrepareAsync(request, NativeProcess.ExecuteAsync, token);

    internal static async Task<ReleasePreparationResult> PrepareAsync(
        ReleasePreparationRequest request,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        CancellationToken token)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        lifetime.CancelAfter(TimeSpan.FromMinutes(15));
        token = lifetime.Token;
        (string checkout, string scratch) = ReleaseQualification.RequireFreshScratch(
            request.Checkout, request.Scratch, PathMessage,
            "NuGet Release requires fresh external scratch.");
        var qualification = new ReleaseQualification(execute, token);
        var files = new List<ReleaseFile>();
        var native = new Dictionary<string, string>(StringComparer.Ordinal);
        ReleaseUnitDeclaration? unit = null;
        NuGetReleaseUnit registered = ReleaseUnitCatalog.AdmitNuGet(request.Subject);
        string framework = "";
        string[]? frameworks = null;
        ReleaseBuild? build = null;
        string project = "";
        DotNetOriginalPackage? original = null;
        string originals = Directory.CreateDirectory(Path.Combine(scratch, "original")).FullName;
        Dictionary<string, string?> environment = EnvironmentFor(scratch, request.Subject);

        await qualification.CheckAsync("native-input", async () =>
        {
            ReleasePolicy.RequireEligible(request.Subject);
            if (!GitReader.IsCommitId(request.Subject.Revision))
                throw new InvalidDataException(
                    "NuGet Release requires the exact selected revision.");
            GitMaterialization bound = await GitMaterialization.BindAsync(checkout,
                request.Subject.Revision, token);
            unit = ReleaseUnitDeclarationReader.Read(await bound.ReadOptionalTextAsync(
                request.Subject.Declaration, token) ??
                throw new InvalidDataException("Missing committed NuGet release declaration."),
                request.Subject.Declaration);
            if (unit.Id != request.Subject.Unit || unit.Builds is not
                [
                {
                    Definition: "dotnet/nuget-package-v1", Outputs:
                    [{ Role: "primary-package", Kind: "nuget-package" }]
                }])
                throw new InvalidDataException("Unsupported complete NuGet Release recipe.");
            build = unit.Builds[0];
            string entry = DotNetReleaseUnits.Entry(unit, build);
            if (await bound.ReadOptionalTextAsync(entry, token) is null)
                throw new InvalidDataException("Missing committed NuGet project entry.");
            project = Path.GetFullPath(Path.Combine(checkout, entry));
            QualitySelection? quality = await new QualitySelectionReader(checkout,
                bound.ReadOptionalTextAsync).ReadAsync(DotNetRepositoryReader.Parent(entry),
                    "dotnet", token);
            // Ordinary libraries keep their native CI checks and select no quality preset.
            if (registered.Library ? quality is not null : quality?.Preset != Preset)
                throw new InvalidDataException("Unsupported complete NuGet quality selection.");
            string selected = await ReleasePreparationContext.RequirePinnedSdkAsync(bound,
                checkout, "dotnet", command => RunAsync("native-input", command), token,
                missingSelection: "Missing committed SDK selection.");
            if (!Version.TryParse(selected, out _))
                throw new InvalidDataException("Actual SDK differs from committed selection.");
            await RunAsync("native-input", new("dotnet", checkout,
                ["restore", DotNetCli.DisableBuildServers, project, "--locked-mode",
                    "-property:Configuration=Release",
                    "-property:ContinuousIntegrationBuild=true"], 300, environment, false));
            using JsonDocument shape = JsonDocument.Parse((await RunAsync("native-input",
                new("dotnet", checkout, ["msbuild", DotNetCli.DisableBuildServers, project,
                    "-nologo", "-noAutoResponse", "-property:Configuration=Release",
                    "-property:ContinuousIntegrationBuild=true",
                    "-getProperty:TargetFramework,TargetFrameworks,RuntimeIdentifier," +
                    "RuntimeIdentifiers,IsTestProject,IsTestingPlatformApplication"],
                    60, environment, false))).Stdout);
            Dictionary<string, string> recipe = DotNetPackageResults.Properties(shape.RootElement);
            if (registered.Library)
            {
                frameworks = recipe["TargetFrameworks"]
                    .Split(';', StringSplitOptions.RemoveEmptyEntries);
                if (recipe["TargetFramework"] != "" || frameworks.Length == 0)
                    throw new InvalidDataException(
                        "Unsupported native variants or required tests.");
            }
            else
            {
                framework = recipe["TargetFramework"];
                if (string.IsNullOrWhiteSpace(framework) || recipe["TargetFrameworks"] != "")
                    throw new InvalidDataException(
                        "Unsupported native variants or required tests.");
            }
            if (recipe["RuntimeIdentifier"] != "" || recipe["RuntimeIdentifiers"] != "" ||
                TestProperties.Any(name =>
                    recipe[name] != "" && !recipe[name].Equals("false",
                        StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Unsupported native variants or required tests.");
            native = await ProjectionAsync("native-input", "GetBuildVersion");
            if (native["NETCoreSdkVersion"] != selected ||
                native["PackageId"] != registered.PackageId ||
                native["AssemblyName"] != registered.AssemblyName)
                throw new InvalidDataException("Unsupported native NuGet product or SDK.");
        });
        await qualification.CheckAsync("build", async () =>
            Compare(await ProjectionAsync("build", "Build")));
        await qualification.CheckAsync("pack", async () =>
        {
            string response = Path.Combine(scratch, "pack.json");
            await RunAsync("pack", Query("Pack,_GetOutputItemsFromPack", response,
                ["-property:PackageOutputPath=" + ProjectCollection.Escape(originals)]));
            using JsonDocument document = await DotNetPackageResults.ResponseAsync(response, token);
            DotNetPackageResults.Target(document.RootElement, "Pack");
            Dictionary<string, string> packed =
                DotNetPackageResults.Properties(document.RootElement);
            DotNetPackageResults.Validate(project, framework, "", packed);
            Compare(packed);
            ReleaseOutput output = build!.Outputs[0];
            original = DotNetPackageResults.Archive(unit!.Id, build.Id, output.Id, output.Role,
                output.Kind, document.RootElement, originals, packed, registered.Library &&
                    packed["IncludeSymbols"].Equals("true", StringComparison.OrdinalIgnoreCase));
            native = packed;
        });
        await qualification.CheckAsync("contents", () =>
        {
            DotNetPackageArchive.RequireOriginal(original!);
            DotNetPackageArchive.Contents(original!.Path, native, frameworks, registered.Analyzers);
            return Task.CompletedTask;
        });
        await qualification.CheckAsync("consumer", async () =>
            await DotNetPackageConsumer.RunAsync(original!, native, Path.Combine(scratch, "probe"),
                300, (command, cancellation) => RunAsync("consumer", command, cancellation),
                token, registered.Library ? new("net" +
                    Version.Parse(native["NETCoreSdkVersion"]).Major + ".0",
                    registered.Probe!, registered.AssemblyName, true, registered.Declarations)
                    : null));
        await qualification.CheckAsync("original-bytes", () =>
        {
            DotNetPackageArchive.RequireOriginal(original!);
            files.Add(new(original!.Build, original.Output, original.Kind, native["PackageId"],
                native["NuGetPackageVersion"], Path.GetRelativePath(scratch, original.Path)
                    .Replace('\\', '/'), original.Size, original.Sha256));
            return Task.CompletedTask;
        });
        // Physical paths are execution facts, not transferred consumer expectations.
        native.Remove("MSBuildProjectFullPath");
        if (native.TryGetValue("DocumentationFile", out string? documentation))
            native["DocumentationFile"] = Path.GetFileName(documentation.Replace('\\', '/'));
        return new(new(request.Subject, [.. files], Required.ToArray(), qualification.Checks),
            native, qualification.Commands, qualification.Errors);

        NativeCommand Query(string targets, string response, string[]? extra = null) => new(
            "dotnet", checkout, ["msbuild", DotNetCli.DisableBuildServers, project, "-nologo",
                "-noAutoResponse",
                // The multi-target outer build dispatches to inner builds, so its own
                // version projection exists only after it runs the versioning target.
                "-target:" + (registered.Library && targets != "GetBuildVersion"
                    ? "GetBuildVersion," + targets : targets), "-getTargetResult:" + targets,
                "-getProperty:" + string.Join(',', DotNetPackageResults.NativeProperties),
                "-getResultOutputFile:" + response, .. Dimensions.Where(dimension =>
                    !registered.Library || dimension != "-property:TargetFramework=").Select(
                    dimension => dimension == "-property:TargetFramework=" ? dimension + framework
                        : dimension),
                "-property:PathMap=" + ProjectCollection.Escape(checkout + "=/_/"),
                // Concurrent inner builds would each append NBGV's discarded cloud-build
                // variables to the one GITHUB_ENV file and can collide on its lock.
                SuppressCloudVariables, .. (extra ?? [])], 300,
            environment, false);

        async Task<Dictionary<string, string>> ProjectionAsync(string check, string target)
        {
            string response = Path.Combine(scratch, check + ".json");
            await RunAsync(check, Query(target, response));
            using JsonDocument document = await DotNetPackageResults.ResponseAsync(response, token);
            DotNetPackageResults.Target(document.RootElement, target);
            Dictionary<string, string> values =
                DotNetPackageResults.Properties(document.RootElement);
            DotNetPackageResults.Validate(project, framework, "", values);
            return values;
        }

        void Compare(Dictionary<string, string> values)
        {
            if (Versions.Any(name => values[name] != native[name]))
                throw new InvalidDataException(
                    "Native version recomputation differs across Release phases.");
        }

        Task<NativeCommandResult> RunAsync(string check, NativeCommand command,
            CancellationToken? cancellation = null)
        {
            // Consumer-owned isolation overrides the preparation profile.
            var effective = new Dictionary<string, string?>(environment, StringComparer.Ordinal);
            if (command.Environment is not null)
                foreach (var pair in command.Environment) effective[pair.Key] = pair.Value;
            return qualification.RunAsync(command with
            {
                Environment = effective,
                InheritEnvironment = false,
            }, cancellation, check: check, requireNullError: false);
        }
    }

    private static Dictionary<string, string?> EnvironmentFor(string scratch,
        ReleaseSubject subject) => ReleasePreparationContext.CreateHomeEnvironment(
            ["PATH", "SystemRoot", "WINDIR", "PATHEXT", "ProgramFiles", "ProgramFiles(x86)",
                "DOTNET_ROOT", "DOTNET_ROOT_X64", "NUGET_PACKAGES", "DOTNET_CLI_TELEMETRY_OPTOUT",
                "DOTNET_SKIP_FIRST_TIME_EXPERIENCE"],
            [.. ReleasePreparationContext.GithubVersioningEnvironment(scratch, subject)],
            Path.Combine(scratch, "profile"), createProfileDirectories: true);

    private const string PathMessage = "NuGet Release paths must be absolute.";
}
