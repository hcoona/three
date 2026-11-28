using System.Text.Json;
using WorkflowDelivery.Ecosystems;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Release;

// Builds the installer and portable package once on Windows with the product's own
// scripts, then qualifies both by clean local consumption before any write capability.
internal static class GithubReleasePreparation
{
    internal static readonly string[] Required = ["native-input", "build", "contents",
        "installer-consumer", "portable-consumer", "original-bytes"];

    private static string[] InputsFor(GithubReleaseUnit unit) =>
    [
        "global.json", ".config/dotnet-tools.json",
        unit.ProductDirectory + "/version.json",
        unit.ProductDirectory + "/" + unit.EntryPoint,
        unit.ProductDirectory + "/ImageOcclusionEditorWinUI3/Update-LocalSvgEditor.ps1",
        unit.ProductDirectory + "/script/Helpers.ps1",
        unit.ProductDirectory + "/script/Publish-ImageOcclusionEditor.ps1",
        unit.ProductDirectory + "/script/Build-InnoInstaller.ps1",
        unit.ProductDirectory + "/script/Setup.iss",
    ];

    internal static Task<ReleasePreparationResult> PrepareAsync(
        ReleasePreparationRequest request, CancellationToken token) =>
        PrepareAsync(request, NativeProcess.ExecuteAsync, OperatingSystem.IsWindows(), token);

    internal static async Task<ReleasePreparationResult> PrepareAsync(
        ReleasePreparationRequest request,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        bool windows, CancellationToken token)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        lifetime.CancelAfter(TimeSpan.FromMinutes(60));
        token = lifetime.Token;
        (string checkout, string scratch) = ReleaseQualification.RequireFreshScratch(
            request.Checkout, request.Scratch, "GitHub Release paths must be absolute.",
            "GitHub Release requires fresh external scratch.");
        var qualification = new ReleaseQualification(execute, token);
        var native = new Dictionary<string, string>(StringComparer.Ordinal);
        Dictionary<string, string?> environment = EnvironmentFor(scratch, request.Subject);
        GithubReleaseUnit? registered = null;
        string product = "";
        ReleaseBuild? build = null;
        string version = "";
        string? releaseNotes = null;

        await qualification.CheckAsync("native-input", async () =>
        {
            ReleaseSubject subject = request.Subject;
            (GithubReleaseUnit admitted, bool official) =
                ReleaseUnitCatalog.AdmitGithubRelease(subject);
            registered = admitted;
            product = Path.Combine(checkout, admitted.ProductDirectory);
            if (!windows)
                throw new InvalidDataException("GitHub Release preparation requires Windows.");
            GitMaterialization bound = await GitMaterialization.BindAsync(checkout,
                subject.Revision, token);
            build = await ReadRecipeAsync(subject, admitted, bound.ReadOptionalTextAsync, token);
            await ReleasePreparationContext.RequireCompleteHistoryAsync(checkout, RunAsync);
            string selected = await ReleasePreparationContext.RequirePinnedSdkAsync(bound,
                checkout, "dotnet", RunAsync, token);
            await RunAsync(new("dotnet", checkout, ["tool", "restore"], 300));
            using JsonDocument projection = JsonDocument.Parse((await RunAsync(new("dotnet",
                product, ["nbgv", "get-version", "--format", "json"], 180))).Stdout);
            (version, bool publicRelease) = QualifyNative(subject, official,
                projection.RootElement);
            string changelogPath = admitted.ProductDirectory + "/CHANGELOG.md";
            if (await bound.ReadOptionalTextAsync(changelogPath, token) is { } changelog)
                releaseNotes = ReleaseChangelog.Extract(changelog, version, subject.Revision,
                    changelogPath);
            native.Add("name", subject.Unit);
            native.Add("version", version);
            native.Add("commit", subject.Revision);
            native.Add("public-release", publicRelease ? "true" : "false");
            native.Add("tag", GithubReleaseUnit.Tag(subject, version));
            native.Add("sdk", selected);
        });
        ReleaseFile[] files = await UnitPreparation.QualifyAsync(request.Subject, build, scratch,
            qualification, Required, subject => registered!.Native.QualifyAsync(subject, product,
                scratch, version, qualification.CheckAsync, RunAsync));
        return new(new(request.Subject, [.. files], Required.ToArray(), qualification.Checks,
                ReleaseNotes: qualification.Errors.Length == 0 && files.Length > 0
                    ? releaseNotes : null),
            native, qualification.Commands, qualification.Errors);

        Task<NativeCommandResult> RunAsync(NativeCommand command) =>
            qualification.RunAsync(command with
            {
                Environment = new Dictionary<string, string?>(environment, StringComparer.Ordinal),
            }, requireNullError: true);
    }

    // The complete committed recipe, read through a revision-bound reader.
    internal static async Task<ReleaseBuild> ReadRecipeAsync(ReleaseSubject subject,
        GithubReleaseUnit expectedUnit, Func<string, CancellationToken, Task<string?>> read,
        CancellationToken token)
    {
        ReleaseUnitDeclaration unit = ReleaseUnitDeclarationReader.Read(
            await read(subject.Declaration, token) ??
                throw new InvalidDataException("Missing committed GitHub Release declaration."),
            subject.Declaration);
        if (unit.Id != subject.Unit || unit.Builds is not [
            {
                Definition: GithubReleaseUnit.BuildDefinition, Outputs: { Length: 2 }
            } build] || build.EntryPoint != expectedUnit.EntryPoint)
            throw new InvalidDataException("Unsupported complete GitHub Release recipe.");
        if (build.Outputs.Count(output => output is
            { Role: "primary-package", Kind: "inno-installer" }) != 1 ||
            build.Outputs.Count(output => output is
            { Role: "portable-package", Kind: "portable-zip" }) != 1)
            throw new InvalidDataException("Unsupported complete GitHub Release asset set.");
        await ReleasePreparationContext.RequireCommittedInputsAsync(read,
            InputsFor(expectedUnit), token);
        return build;
    }

    // Pure check of the native NBGV projection against the admitted subject.
    internal static (string Version, bool PublicRelease) QualifyNative(ReleaseSubject subject,
        bool official, JsonElement projection)
    {
        string version = projection.GetProperty("SemVer2").GetString() ?? "";
        bool publicRelease = projection.GetProperty("PublicRelease").GetBoolean();
        if (version.Length == 0 || version.Any(c => !(char.IsAsciiLetterOrDigit(c) ||
                c is '.' or '-')) ||
            projection.GetProperty("GitCommitId").GetString() != subject.Revision ||
            !subject.DryRun && official && !publicRelease)
            throw new InvalidDataException("Unsupported native GitHub Release identity.");
        return (version, publicRelease);
    }

    // The runner's build toolchain (SDK, Visual C++ for NativeAOT, ISCC, 7-Zip) is inherited;
    // NBGV reads the selected ref and commit, and no credential reaches native work.
    private static Dictionary<string, string?> EnvironmentFor(string scratch,
        ReleaseSubject subject) => ReleasePreparationContext.CreateHomeEnvironment(
            [], ReleasePreparationContext.GithubVersioningEnvironment(scratch, subject).Concat(
                [("GITHUB_TOKEN", null), ("GH_TOKEN", null),
                    ("ACTIONS_ID_TOKEN_REQUEST_TOKEN", null),
                    ("ACTIONS_ID_TOKEN_REQUEST_URL", null),
                    ("DOTNET_CLI_TELEMETRY_OPTOUT", "1")]),
            home: null, temp: Path.Combine(scratch, "tmp"));
}
