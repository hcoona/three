using System.Text.Json;
using System.Xml;
using Microsoft.Build.Evaluation;
using WorkflowDelivery.Ecosystems;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Release;

internal sealed record NuGetPreparationRequest(ReleaseSubject Subject, string Checkout,
    string Scratch);
internal sealed record NuGetPreparationResult(PreparedRelease Prepared,
    Dictionary<string, string> Native, ReleaseCommand[] Commands, string[] Errors);

// The adopted Release recipe uses native versioning and packaging independently of CI.
internal static class NuGetPreparation
{
    internal const string Preset = "dotnet/hcoona-release-smoke-github-packages-v1";
    private static readonly string[] Required = ["native-input", "build", "pack", "contents",
        "consumer", "original-bytes"];
    private static readonly string[] Versions = ["PackageId", "NuGetPackageVersion",
        "PackageVersion", "AssemblyVersion", "AssemblyFileVersion", "AssemblyInformationalVersion"];
    private static readonly string[] TestProperties = ["IsTestProject",
        "IsTestingPlatformApplication"];
    private static readonly string[] Dimensions = ["-property:Configuration=Release",
        "-property:TargetFramework=net10.0", "-property:RuntimeIdentifier=",
        "-property:ContinuousIntegrationBuild=true", "-property:RestoreLockedMode=true"];

    internal static Task<NuGetPreparationResult> PrepareAsync(NuGetPreparationRequest request,
        CancellationToken token) => PrepareAsync(request, NativeProcess.ExecuteAsync, token);

    internal static async Task<NuGetPreparationResult> PrepareAsync(NuGetPreparationRequest request,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        CancellationToken token)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        lifetime.CancelAfter(TimeSpan.FromMinutes(15));
        token = lifetime.Token;
        string checkout = Absolute(request.Checkout);
        string scratch = Absolute(request.Scratch);
        if (!Directory.Exists(checkout) || !Directory.Exists(scratch) ||
            Within(scratch, checkout) || Within(checkout, scratch) ||
            Linked(checkout) || Linked(scratch) ||
                Directory.EnumerateFileSystemEntries(scratch).Any())
            throw new InvalidDataException("NuGet Release requires fresh external scratch.");
        var checks = new List<ReleaseCheck>();
        var commands = new List<ReleaseCommand>();
        var errors = new List<string>();
        var files = new List<ReleaseFile>();
        var native = new Dictionary<string, string>(StringComparer.Ordinal);
        ReleaseUnitDeclaration? unit = null;
        ReleaseBuild? build = null;
        string project = "";
        DotNetOriginalPackage? original = null;
        string originals = Directory.CreateDirectory(Path.Combine(scratch, "original")).FullName;
        Dictionary<string, string?> environment = EnvironmentFor(scratch);

        await CheckAsync("native-input", async () =>
        {
            if (request.Subject.Channel != "buddy" ||
                request.Subject.Destination != "github-packages-nuget")
                throw new InvalidDataException("Unsupported NuGet Release channel or destination.");
            GitRevision revision = await new GitReader(checkout).ReadAsync(request.Subject.Revision,
                token);
            if (revision.Commit != request.Subject.Revision)
                throw new InvalidDataException(
                    "NuGet Release requires the exact selected revision.");
            GitMaterialization bound = await GitMaterialization.BindAsync(checkout, revision,
                token);
            unit = ReleaseUnitDeclarationReader.Read(await bound.ReadOptionalTextAsync(
                request.Subject.Declaration, token) ??
                throw new InvalidDataException("Missing committed NuGet release declaration."),
                request.Subject.Declaration);
            if (unit.Id != request.Subject.Unit || unit.Builds is not
                [{ Definition: "dotnet/nuget-package-v1", Outputs:
                    [{ Role: "primary-package", Kind: "nuget-package" }] }])
                throw new InvalidDataException("Unsupported complete NuGet Release recipe.");
            build = unit.Builds[0];
            string entry = DotNetReleaseUnits.Entry(unit, build);
            if (await bound.ReadOptionalTextAsync(entry, token) is null)
                throw new InvalidDataException("Missing committed NuGet project entry.");
            project = Path.GetFullPath(Path.Combine(checkout, entry));
            QualitySelection? quality = await new QualitySelectionReader(checkout,
                bound.ReadOptionalTextAsync).ReadAsync(DotNetRepositoryReader.Parent(entry),
                    "dotnet", token);
            if (quality?.Preset != Preset)
                throw new InvalidDataException("Unsupported complete NuGet quality selection.");
            string globalText = await bound.ReadOptionalTextAsync("global.json", token) ??
                throw new InvalidDataException("Missing committed SDK selection.");
            using JsonDocument global = JsonDocument.Parse(globalText);
            JsonElement sdk = global.RootElement.GetProperty("sdk");
            string selected = sdk.GetProperty("version").GetString()!;
            if (sdk.GetProperty("rollForward").GetString() != "disable" ||
                !Version.TryParse(selected, out _) ||
                (await RunAsync("native-input", new("dotnet", checkout, ["--version"], 30,
                    environment, false))).Stdout.Trim() != selected)
                throw new InvalidDataException("Actual SDK differs from committed selection.");
            await RunAsync("native-input", new("dotnet", checkout,
                ["restore", project, "--locked-mode", "-property:Configuration=Release",
                    "-property:ContinuousIntegrationBuild=true"], 300, environment, false));
            using JsonDocument shape = JsonDocument.Parse((await RunAsync("native-input",
                new("dotnet", checkout, ["msbuild", project, "-nologo", "-noAutoResponse",
                    "-property:Configuration=Release", "-property:ContinuousIntegrationBuild=true",
                    "-getProperty:TargetFramework,TargetFrameworks,RuntimeIdentifier," +
                    "RuntimeIdentifiers,IsTestProject,IsTestingPlatformApplication"],
                    60, environment, false))).Stdout);
            Dictionary<string, string> recipe = DotNetPackageResults.Properties(shape.RootElement);
            if (recipe["TargetFramework"] != "net10.0" || recipe["TargetFrameworks"] != "" ||
                recipe["RuntimeIdentifier"] != "" || recipe["RuntimeIdentifiers"] != "" ||
                TestProperties.Any(name =>
                    recipe[name] != "" && !recipe[name].Equals("false",
                        StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Unsupported native variants or required tests.");
            native = await ProjectionAsync("native-input", "GetBuildVersion");
            if (native["NETCoreSdkVersion"] != selected ||
                native["PackageId"] != "Hcoona.ReleaseSmoke.GithubPackages" ||
                native["AssemblyName"] != "Hcoona.ReleaseSmoke.GithubPackages")
                throw new InvalidDataException("Unsupported native NuGet product or SDK.");
        });
        await CheckAsync("build", async () => Compare(await ProjectionAsync("build", "Build")));
        await CheckAsync("pack", async () =>
        {
            string response = Path.Combine(scratch, "pack.json");
            await RunAsync("pack", Query("Pack,_GetOutputItemsFromPack", response,
                ["-property:PackageOutputPath=" + ProjectCollection.Escape(originals)]));
            using JsonDocument document = await DotNetPackageResults.ResponseAsync(response, token);
            DotNetPackageResults.Target(document.RootElement, "Pack");
            Dictionary<string, string> packed =
                DotNetPackageResults.Properties(document.RootElement);
            DotNetPackageResults.Validate(project, "net10.0", "", packed);
            Compare(packed);
            ReleaseOutput output = build!.Outputs[0];
            original = DotNetPackageResults.Archive(unit!.Id, build.Id, output.Id, output.Role,
                output.Kind, document.RootElement, originals, packed);
            native = packed;
        });
        await CheckAsync("contents", () =>
        {
            DotNetPackageArchive.RequireOriginal(original!);
            DotNetPackageArchive.Contents(original!.Path, native);
            return Task.CompletedTask;
        });
        await CheckAsync("consumer", async () => await DotNetPackageConsumer.RunAsync(original!,
            native, Path.Combine(scratch, "probe"), 300,
            (command, cancellation) => RunAsync("consumer", command, cancellation), token));
        await CheckAsync("original-bytes", () =>
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
        return new(new(request.Subject, [.. files], Required.ToArray(), [.. checks]), native,
            [.. commands], [.. errors]);

        NativeCommand Query(string targets, string response, string[]? extra = null) => new(
            "dotnet", checkout, ["msbuild", project, "-nologo", "-noAutoResponse",
                "-target:" + targets, "-getTargetResult:" + targets,
                "-getProperty:" + string.Join(',', DotNetPackageResults.NativeProperties),
                "-getResultOutputFile:" + response, .. Dimensions,
                "-property:PathMap=" + ProjectCollection.Escape(checkout + "=/_/"),
                .. (extra ?? [])], 300,
            environment, false);

        async Task<Dictionary<string, string>> ProjectionAsync(string check, string target)
        {
            string response = Path.Combine(scratch, check + ".json");
            await RunAsync(check, Query(target, response));
            using JsonDocument document = await DotNetPackageResults.ResponseAsync(response, token);
            DotNetPackageResults.Target(document.RootElement, target);
            Dictionary<string, string> values =
                DotNetPackageResults.Properties(document.RootElement);
            DotNetPackageResults.Validate(project, "net10.0", "", values);
            return values;
        }

        void Compare(Dictionary<string, string> values)
        {
            if (Versions.Any(name => values[name] != native[name]))
                throw new InvalidDataException(
                    "Native version recomputation differs across Release phases.");
        }

        async Task<NativeCommandResult> RunAsync(string check, NativeCommand command,
            CancellationToken? cancellation = null)
        {
            // Consumer-owned isolation overrides the preparation profile.
            var effective = new Dictionary<string, string?>(environment, StringComparer.Ordinal);
            if (command.Environment is not null)
                foreach (var pair in command.Environment) effective[pair.Key] = pair.Value;
            command = command with { Environment = effective, InheritEnvironment = false };
            NativeCommandResult result = await execute(command, cancellation ?? token);
            commands.Add(new(check, command, result));
            if (result.Termination == NativeTermination.Cancelled)
                throw new OperationCanceledException(token);
            if (result.Termination == NativeTermination.TimedOut)
                throw new TimeoutException("Native " + check + " timed out.");
            if (!result.Succeeded)
                throw new InvalidDataException("Native " + check + " failed: " +
                    result.Termination + ".");
            (cancellation ?? token).ThrowIfCancellationRequested();
            return result;
        }

        async Task CheckAsync(string id, Func<Task> action)
        {
            if (checks.Any(check => check.Status != ReleaseCheckStatus.Passed))
            { checks.Add(new(id, ReleaseCheckStatus.Skipped)); return; }
            try { token.ThrowIfCancellationRequested(); await action();
                token.ThrowIfCancellationRequested(); checks.Add(new(id,
                    ReleaseCheckStatus.Passed)); }
            catch (OperationCanceledException)
            { checks.Add(new(id, ReleaseCheckStatus.Cancelled)); errors.Add(id + " cancelled."); }
            catch (TimeoutException)
            { checks.Add(new(id, ReleaseCheckStatus.TimedOut)); errors.Add(id + " timed out."); }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or
                InvalidDataException or JsonException or KeyNotFoundException or
                    InvalidOperationException or
                ArgumentException or FormatException or BadImageFormatException or XmlException)
            { checks.Add(new(id, ReleaseCheckStatus.Failed)); errors.Add(failure.Message); }
        }
    }

    private static Dictionary<string, string?> EnvironmentFor(string scratch)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (string name in new[] { "PATH", "SystemRoot", "WINDIR", "PATHEXT", "DOTNET_ROOT",
            "DOTNET_ROOT_X64", "NUGET_PACKAGES", "GITHUB_ACTIONS", "GITHUB_REF", "GITHUB_SHA",
            "GITHUB_RUN_ID", "GITHUB_RUN_NUMBER", "GITHUB_REPOSITORY", "SOURCE_DATE_EPOCH",
            "DOTNET_CLI_TELEMETRY_OPTOUT", "DOTNET_SKIP_FIRST_TIME_EXPERIENCE" })
            if (Environment.GetEnvironmentVariable(name) is { } value) values.Add(name, value);
        string home = Directory.CreateDirectory(Path.Combine(scratch, "profile")).FullName;
        foreach (string name in new[] { "HOME", "USERPROFILE", "DOTNET_CLI_HOME" }) values[name] =
            home;
        foreach ((string name, string path) in new[] { ("APPDATA", "appdata"),
            ("LOCALAPPDATA", "localappdata"), ("XDG_CONFIG_HOME", "config"),
            ("XDG_DATA_HOME", "data"), ("XDG_CACHE_HOME", "cache") })
            values[name] = Directory.CreateDirectory(Path.Combine(home, path)).FullName;
        return values;
    }

    private static string Absolute(string path) => Path.IsPathFullyQualified(path)
        ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)) :
        throw new InvalidDataException("NuGet Release paths must be absolute.");
    private static bool Within(string path, string parent) => path.Equals(parent,
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase :
            StringComparison.Ordinal) ||
        path.StartsWith(parent + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ?
            StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private static bool Linked(string path)
    {
        for (DirectoryInfo? directory = new(path); directory is not null; directory =
            directory.Parent)
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) return true;
        return false;
    }
}
