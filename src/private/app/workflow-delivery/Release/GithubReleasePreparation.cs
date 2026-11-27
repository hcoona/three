using System.IO.Compression;
using System.Security.Cryptography;
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
    // ZIP entry times are fixed so the archive depends only on the published file set.
    private static readonly DateTimeOffset EntryTime = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

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
        var files = new List<ReleaseFile>();
        var native = new Dictionary<string, string>(StringComparer.Ordinal);
        string publishRoot = Path.Combine(scratch, "publish");
        string installerRoot = Path.Combine(scratch, "installer");
        string originals = Path.Combine(scratch, "originals");
        string consumers = Path.Combine(scratch, "consumers");
        Dictionary<string, string?> environment = EnvironmentFor(scratch, request.Subject);
        GithubReleaseUnit? registered = null;
        string product = "";
        ReleaseBuild? build = null;
        string version = "", publish = "", installer = "", portable = "";
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
        await qualification.CheckAsync("build", async () =>
        {
            string entryDir = Path.GetDirectoryName(registered!.EntryPoint)!
                .Replace('/', Path.DirectorySeparatorChar);
            await RunAsync(new("pwsh", Path.Combine(product, entryDir),
                [.. Pwsh, Path.Combine(product, entryDir, "Update-LocalSvgEditor.ps1"), "-Force"],
                600));
            await RunAsync(new("pwsh", product, [.. Pwsh, Path.Combine(product,
                "script/Publish-ImageOcclusionEditor.ps1"), "-Configuration", "Release",
                "-OutputRoot", publishRoot], 2700));
            await RunAsync(new("pwsh", product, [.. Pwsh, Path.Combine(product,
                "script/Build-InnoInstaller.ps1"), "-Configuration", "Release",
                "-PublishOutputRoot", publishRoot, "-InstallerOutputPath", installerRoot], 900));
            string[] executables = Directory.Exists(publishRoot) ? Directory.GetFiles(
                publishRoot, registered.Executable, SearchOption.AllDirectories) : [];
            if (executables is not [string executable])
                throw new InvalidDataException("Native publish produced no unique application.");
            publish = Path.GetDirectoryName(executable)!;
            string[] setups = Directory.Exists(installerRoot)
                ? Directory.GetFiles(installerRoot, "*.exe") : [];
            if (setups is not [string setup])
                throw new InvalidDataException("Native installer build produced no unique setup.");
            Directory.CreateDirectory(originals);
            installer = Path.Combine(originals, registered.Asset("inno-installer", version));
            portable = Path.Combine(originals, registered.Asset("portable-zip", version));
            File.Copy(setup, installer, overwrite: false);
            CreatePortable(publish, portable);
        });
        await qualification.CheckAsync("contents", () =>
        {
            RequirePortableExecutable(installer);
            using ZipArchive archive = ZipFile.OpenRead(portable);
            var expected = Tree(publish);
            var actual = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                RelativeEntry(entry.FullName);
                if (!actual.TryAdd(entry.FullName, entry.Length))
                    throw new InvalidDataException("Portable package repeats an entry.");
            }
            if (actual.Count != expected.Count || expected.Any(pair =>
                    !actual.TryGetValue(pair.Key, out long length) || length != pair.Value) ||
                !actual.ContainsKey(registered!.Executable) ||
                !actual.ContainsKey(registered.Editor))
                throw new InvalidDataException("Portable package differs from the publish tree.");
            return Task.CompletedTask;
        });
        await qualification.CheckAsync("installer-consumer", async () =>
        {
            string target = Path.Combine(consumers, "installed");
            Directory.CreateDirectory(consumers);
            await RunAsync(new(installer, consumers, ["/VERYSILENT", "/SUPPRESSMSGBOXES",
                "/NORESTART", "/SP-", "/NOICONS", "/DIR=" + target,
                "/LOG=" + Path.Combine(consumers, "install.log")], 600));
            RequireConsumed(registered!, target, publish);
        });
        await qualification.CheckAsync("portable-consumer", () =>
        {
            string target = Path.Combine(consumers, "portable");
            ZipFile.ExtractToDirectory(portable, target);
            RequireConsumed(registered!, target, publish);
            return Task.CompletedTask;
        });
        await qualification.CheckAsync("original-bytes", () =>
        {
            foreach (ReleaseOutput output in build!.Outputs)
            {
                string path = output.Kind == "inno-installer" ? installer : portable;
                (long size, string digest) = Original(path);
                files.Add(new(build.Id, output.Id, output.Kind, Path.GetFileName(path), version,
                    Path.GetRelativePath(scratch, path).Replace('\\', '/'), size, digest));
            }
            return Task.CompletedTask;
        });
        return new(new(request.Subject, [.. files], Required.ToArray(), qualification.Checks,
                ReleaseNotes: qualification.Errors.Length == 0 && files.Count > 0
                    ? releaseNotes : null),
            native, qualification.Commands, qualification.Errors);

        Task<NativeCommandResult> RunAsync(NativeCommand command) =>
            qualification.RunAsync(command with
            {
                Environment = new Dictionary<string, string?>(environment, StringComparer.Ordinal),
            }, requireNullError: true);
    }

    private static readonly string[] Pwsh = ["-NoLogo", "-NoProfile", "-NonInteractive",
        "-ExecutionPolicy", "Bypass", "-File"];

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

    // Ordinal entry order and a fixed entry time make the archive a function of the tree.
    internal static void CreatePortable(string directory, string archive)
    {
        using FileStream stream = new(archive, FileMode.CreateNew, FileAccess.Write);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (string name in Tree(directory).Keys.Order(StringComparer.Ordinal))
        {
            ZipArchiveEntry entry = zip.CreateEntry(name, CompressionLevel.Optimal);
            entry.LastWriteTime = EntryTime;
            using Stream target = entry.Open();
            using FileStream source = File.OpenRead(Path.Combine(directory, name));
            source.CopyTo(target);
        }
    }

    internal static (long Size, string Sha256) Original(string path) =>
        OwnedFile.Measure(path, emptyMessage: "Empty original GitHub Release asset.");

    private static Dictionary<string, long> Tree(string directory) =>
        Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).ToDictionary(
            file => Path.GetRelativePath(directory, file).Replace('\\', '/'),
            file => new FileInfo(file).Length, StringComparer.Ordinal);

    // A consumer tree must carry the published application byte for byte and its editor.
    private static void RequireConsumed(GithubReleaseUnit unit, string target, string publish)
    {
        string executable = Path.Combine(target, unit.Executable);
        if (!File.Exists(executable) || !File.Exists(Path.Combine(target,
                unit.Editor.Replace('/', Path.DirectorySeparatorChar))) ||
            Original(executable) != Original(Path.Combine(publish, unit.Executable)))
            throw new InvalidDataException("Consumed package lacks the published application.");
        RequirePortableExecutable(executable);
    }

    private static void RequirePortableExecutable(string path)
    {
        using FileStream stream = File.OpenRead(path);
        if (stream.ReadByte() != 'M' || stream.ReadByte() != 'Z')
            throw new InvalidDataException("Original is not a Windows executable.");
    }

    private static void RelativeEntry(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Contains('\\') || name.Contains(':') ||
            name.StartsWith('/') || name.Split('/').Any(part => part is "" or "." or ".."))
            throw new InvalidDataException("Portable package entry leaves its root.");
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
