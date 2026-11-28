using System.Text.Json;
using WorkflowDelivery.Core;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Ecosystems;

internal delegate Task<NativeCommandResult> DotNetPackCommand(string check, NativeCommand command,
    CancellationToken? cancellation = null);

internal sealed class DotNetPackRecipe(string checkout, string scratch, string project,
    string framework, string[]? frameworks, string assembly, string? probe, string? declarations,
    string[] analyzers, DotNetPackCommand run)
{
    private static readonly string[] Versions =
    [
        "PackageId", "NuGetPackageVersion", "PackageVersion",
        "AssemblyVersion", "AssemblyFileVersion", "AssemblyInformationalVersion"
    ];
    internal const string SuppressCloudVariables = "-property:NBGV_SetCloudBuildVersionVars=false";
    internal Dictionary<string, string> Native { get; private set; } = new(StringComparer.Ordinal);

    internal async Task ReadVersionAsync(CancellationToken token) =>
        Native = await ProjectionAsync("native-input", "GetBuildVersion", token);

    internal async Task<QualifiedUnitOriginals?> QualifyAsync(UnitBuildSubject subject,
        RecordNativeCheck check, CancellationToken token)
    {
        string originals = Directory.CreateDirectory(Path.Combine(scratch, "original")).FullName;
        DotNetOriginalPackage? original = null;
        QualifiedUnitOriginals? result = null;
        await check("build", async () => Compare(await ProjectionAsync("build", "Build", token)));
        await check("pack", async () =>
        {
            string response = Path.Combine(scratch, "pack.json");
            await run("pack", Query("Pack,_GetOutputItemsFromPack", response,
                ["-property:PackageOutputPath=" + DotNetTaskHost.Escape(originals)]));
            using JsonDocument document = await DotNetPackageResults.ResponseAsync(response, token);
            DotNetPackageResults.Target(document.RootElement, "Pack");
            Dictionary<string, string> packed =
                DotNetPackageResults.Properties(document.RootElement);
            DotNetPackageResults.Validate(project, framework, "", packed);
            Compare(packed);
            UnitOutput output = subject.ForKind("nuget-package");
            original =
                DotNetPackageResults.Archive(subject.Unit, subject.Build, output.Id, output.Role,
                output.Kind, document.RootElement, originals, packed, frameworks is not null &&
                    packed["IncludeSymbols"].Equals("true", StringComparison.OrdinalIgnoreCase));
            Native = packed;
        });
        await check("contents", () =>
        {
            DotNetPackageArchive.RequireOriginal(original!);
            DotNetPackageArchive.Contents(original!.Path, Native, frameworks, analyzers);
            return Task.CompletedTask;
        });
        await check("consumer", async () => await DotNetPackageConsumer.RunAsync(original!, Native,
            Path.Combine(scratch, "probe"), 300, (command, cancellation) => run("consumer", command,
                cancellation), token, probe is not null ? new("net" +
                    Version.Parse(Native["NETCoreSdkVersion"]).Major + ".0", probe, assembly, true,
                    declarations) : null));
        await check("original-bytes", () =>
        {
            DotNetPackageArchive.RequireOriginal(original!);
            result = subject.Complete(new UnitOriginal(subject.ForKind(original!.Kind),
                Native["PackageId"], Native["NuGetPackageVersion"], original.Path, original.Size,
                original.Sha256));
            return Task.CompletedTask;
        });
        return result;
    }

    private NativeCommand Query(string targets, string response, string[]? extra = null) =>
        new("dotnet", checkout, ["msbuild", DotNetCli.DisableBuildServers, project, "-nologo",
            "-noAutoResponse", "-target:" + (frameworks is not null && targets != "GetBuildVersion"
                ? "GetBuildVersion," + targets : targets), "-getTargetResult:" + targets,
            "-getProperty:" + string.Join(',', DotNetPackageResults.NativeProperties),
            "-getResultOutputFile:" + response, "-property:Configuration=Release",
            .. (frameworks is null ? new[] { "-property:TargetFramework=" + framework } : []),
            "-property:RuntimeIdentifier=", "-property:ContinuousIntegrationBuild=true",
            "-property:RestoreLockedMode=true",
            "-property:PathMap=" + DotNetTaskHost.Escape(checkout + "=/_/"),
            // Concurrent inner builds must not append NBGV's discarded variables to one GITHUB_ENV.
            SuppressCloudVariables, .. (extra ?? [])], 300);

    private async Task<Dictionary<string, string>> ProjectionAsync(string check, string target,
        CancellationToken token)
    {
        string response = Path.Combine(scratch, check + ".json");
        await run(check, Query(target, response));
        using JsonDocument document = await DotNetPackageResults.ResponseAsync(response, token);
        DotNetPackageResults.Target(document.RootElement, target);
        Dictionary<string, string> values = DotNetPackageResults.Properties(document.RootElement);
        DotNetPackageResults.Validate(project, framework, "", values);
        return values;
    }

    private void Compare(Dictionary<string, string> values)
    {
        if (Versions.Any(name => values[name] != Native[name]))
            throw new InvalidDataException(
                "Native version recomputation differs across Release phases.");
    }
}
