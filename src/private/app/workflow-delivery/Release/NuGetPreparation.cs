using System.Text.Json;
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
    private static readonly string[] TestProperties = ["IsTestProject",
        "IsTestingPlatformApplication"];
    internal const string SuppressCloudVariables = DotNetPackRecipe.SuppressCloudVariables;

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
        NuGetReleaseUnit registered = ReleaseUnitCatalog.AdmitNuGet(request.Subject);
        string framework = "";
        string[]? frameworks = null;
        ReleaseBuild? build = null;
        string project = "";
        DotNetPackRecipe? operations = null;
        Dictionary<string, string?> environment = EnvironmentFor(scratch, request.Subject);

        await qualification.CheckAsync("native-input", async () =>
        {
            ReleasePolicy.RequireEligible(request.Subject);
            if (!GitReader.IsCommitId(request.Subject.Revision))
                throw new InvalidDataException(
                    "NuGet Release requires the exact selected revision.");
            GitMaterialization bound = await GitMaterialization.BindAsync(checkout,
                request.Subject.Revision, token);
            ReleaseUnitDeclaration unit =
                ReleaseUnitDeclarationReader.Read(await bound.ReadOptionalTextAsync(
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
            operations =
                new(checkout, scratch, project, framework, frameworks, registered.AssemblyName,
                registered.Probe, registered.Declarations, registered.Analyzers, RunAsync);
            await operations.ReadVersionAsync(token);
            if (operations.Native["NETCoreSdkVersion"] != selected ||
                operations.Native["PackageId"] != registered.PackageId ||
                operations.Native["AssemblyName"] != registered.AssemblyName)
                throw new InvalidDataException("Unsupported native NuGet product or SDK.");
        });
        ReleaseFile[] files = await UnitPreparation.QualifyAsync(request.Subject, build, scratch,
            qualification, Required, subject => operations!.QualifyAsync(subject,
                qualification.CheckAsync, token));
        Dictionary<string, string> native = operations?.Native ?? new(StringComparer.Ordinal);
        // Physical paths are execution facts, not transferred consumer expectations.
        native.Remove("MSBuildProjectFullPath");
        if (native.TryGetValue("DocumentationFile", out string? documentation))
            native["DocumentationFile"] = Path.GetFileName(documentation.Replace('\\', '/'));
        return new(new(request.Subject, [.. files], Required.ToArray(), qualification.Checks),
            native, qualification.Commands, qualification.Errors);

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
