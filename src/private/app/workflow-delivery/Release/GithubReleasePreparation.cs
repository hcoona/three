using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Release;

internal sealed record GithubReleasePreparationRequest(ReleaseSubject Subject, string Checkout,
    string Scratch);
internal sealed record GithubReleasePreparationResult(PreparedRelease Prepared,
    Dictionary<string, string> Native, ReleaseCommand[] Commands, string[] Errors);

// Static registration of the supported GitHub Release unit. The tag follows the product's
// committed NBGV publicReleaseRefSpec and the repository's release/<unit>/v<version> tags.
internal static class GithubReleaseUnits
{
    internal const string ImageOcclusionEditor = "image-occlusion-editor";
    internal const string Product = "src/public/app/ImageOcclusionEditor";
    internal const string Declaration = Product + "/workflow-delivery.release-unit.yml";
    internal const string Buddy = "github-release-prerelease";
    internal const string Official = "github-release";
    internal const string Definition = "dotnet/github-release-assets-v1";
    internal const string Entry = "ImageOcclusionEditorWinUI3/ImageOcclusionEditorWinUI3.csproj";
    internal const string Executable = "ImageOcclusionEditor.exe";
    internal const string Editor = "svg-edit/index.html";

    internal static string Tag(string unit, string version) => "release/" + unit + "/v" + version;

    internal static string Title(string unit, string version) => unit + " v" + version;

    internal static string Asset(string kind, string version) => kind switch
    {
        "inno-installer" => "ImageOcclusionEditor-" + version + "-win-x64-setup.exe",
        "portable-zip" => "ImageOcclusionEditor-" + version + "-win-x64-portable.zip",
        _ => throw new InvalidDataException("Unsupported GitHub Release asset kind."),
    };

    // Pure subject admission, before any Git or native work.
    internal static bool Admit(ReleaseSubject subject)
    {
        bool buddy = subject.Channel == "buddy" && subject.Destination == Buddy;
        bool official = subject.Channel == "official" && subject.Destination == Official;
        if (!(buddy || official) || string.IsNullOrWhiteSpace(subject.SelectedRef))
            throw new InvalidDataException("Unsupported GitHub Release channel or ref.");
        ReleasePolicy.RequireEligible(subject);
        if (!GitReader.IsCommitId(subject.Revision))
            throw new InvalidDataException("GitHub Release requires the exact selected revision.");
        if (subject.Unit != ImageOcclusionEditor || subject.Declaration != Declaration)
            throw new InvalidDataException("Unsupported GitHub Release unit.");
        return official;
    }
}

// Builds the installer and portable package once on Windows with the product's own
// scripts, then qualifies both by clean local consumption before any write capability.
internal static class GithubReleasePreparation
{
    internal static readonly string[] Required = ["native-input", "build", "contents",
        "installer-consumer", "portable-consumer", "original-bytes"];
    private static readonly string[] Inputs = ["global.json", ".config/dotnet-tools.json",
        GithubReleaseUnits.Product + "/version.json",
        GithubReleaseUnits.Product + "/" + GithubReleaseUnits.Entry,
        GithubReleaseUnits.Product + "/ImageOcclusionEditorWinUI3/Update-LocalSvgEditor.ps1",
        GithubReleaseUnits.Product + "/script/Helpers.ps1",
        GithubReleaseUnits.Product + "/script/Publish-ImageOcclusionEditor.ps1",
        GithubReleaseUnits.Product + "/script/Build-InnoInstaller.ps1",
        GithubReleaseUnits.Product + "/script/Setup.iss"];
    // ZIP entry times are fixed so the archive depends only on the published file set.
    private static readonly DateTimeOffset EntryTime = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    internal static Task<GithubReleasePreparationResult> PrepareAsync(
        GithubReleasePreparationRequest request, CancellationToken token) =>
        PrepareAsync(request, NativeProcess.ExecuteAsync, OperatingSystem.IsWindows(), token);

    internal static async Task<GithubReleasePreparationResult> PrepareAsync(
        GithubReleasePreparationRequest request,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        bool windows, CancellationToken token)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        lifetime.CancelAfter(TimeSpan.FromMinutes(60));
        token = lifetime.Token;
        string checkout = Absolute(request.Checkout), scratch = Absolute(request.Scratch);
        if (!Directory.Exists(checkout) || !Directory.Exists(scratch) ||
            OwnedPath.Overlap(scratch, checkout) ||
            Directory.EnumerateFileSystemEntries(scratch).Any())
            throw new InvalidDataException("GitHub Release requires fresh external scratch.");
        var commands = new List<ReleaseCommand>();
        var checks = new List<ReleaseCheck>();
        var errors = new List<string>();
        var files = new List<ReleaseFile>();
        var native = new Dictionary<string, string>(StringComparer.Ordinal);
        string product = Path.Combine(checkout, GithubReleaseUnits.Product);
        string publishRoot = Path.Combine(scratch, "publish");
        string installerRoot = Path.Combine(scratch, "installer");
        string originals = Path.Combine(scratch, "originals");
        string consumers = Path.Combine(scratch, "consumers");
        Dictionary<string, string?> environment = EnvironmentFor(scratch, request.Subject);
        ReleaseBuild? build = null;
        string version = "", publish = "", installer = "", portable = "";
        string current = "native-input";

        await CheckAsync("native-input", async () =>
        {
            ReleaseSubject subject = request.Subject;
            bool official = GithubReleaseUnits.Admit(subject);
            if (!windows)
                throw new InvalidDataException("GitHub Release preparation requires Windows.");
            GitMaterialization bound = await GitMaterialization.BindAsync(checkout,
                subject.Revision, token);
            build = await ReadRecipeAsync(subject, bound.ReadOptionalTextAsync, token);
            if ((await RunAsync(new("git", checkout,
                ["rev-parse", "--is-shallow-repository"], 30))).Stdout.Trim() != "false")
                throw new InvalidDataException("Native versioning requires complete Git history.");
            using JsonDocument global = JsonDocument.Parse(await bound.ReadOptionalTextAsync(
                "global.json", token) ?? throw new InvalidDataException("Missing SDK selection."));
            JsonElement sdk = global.RootElement.GetProperty("sdk");
            string selected = sdk.GetProperty("version").GetString() ?? "";
            if (sdk.GetProperty("rollForward").GetString() != "disable" ||
                (await RunAsync(new("dotnet", checkout, ["--version"], 30))).Stdout.Trim() !=
                    selected)
                throw new InvalidDataException("Actual SDK differs from committed selection.");
            await RunAsync(new("dotnet", checkout, ["tool", "restore"], 300));
            using JsonDocument projection = JsonDocument.Parse((await RunAsync(new("dotnet",
                product, ["nbgv", "get-version", "--format", "json"], 180))).Stdout);
            (version, bool publicRelease) = QualifyNative(subject, official,
                projection.RootElement);
            native.Add("name", subject.Unit);
            native.Add("version", version);
            native.Add("commit", subject.Revision);
            native.Add("public-release", publicRelease ? "true" : "false");
            native.Add("tag", GithubReleaseUnits.Tag(subject.Unit, version));
            native.Add("sdk", selected);
        });
        await CheckAsync("build", async () =>
        {
            // The product's own scripts: the svg-edit payload, the NativeAOT publish and ISCC.
            await RunAsync(new("pwsh", Path.Combine(product, "ImageOcclusionEditorWinUI3"),
                [.. Pwsh, Path.Combine(product,
                    "ImageOcclusionEditorWinUI3/Update-LocalSvgEditor.ps1"), "-Force"], 600));
            await RunAsync(new("pwsh", product, [.. Pwsh, Path.Combine(product,
                "script/Publish-ImageOcclusionEditor.ps1"), "-Configuration", "Release",
                "-OutputRoot", publishRoot], 2700));
            await RunAsync(new("pwsh", product, [.. Pwsh, Path.Combine(product,
                "script/Build-InnoInstaller.ps1"), "-Configuration", "Release",
                "-PublishOutputRoot", publishRoot, "-InstallerOutputPath", installerRoot], 900));
            string[] executables = Directory.Exists(publishRoot) ? Directory.GetFiles(
                publishRoot, GithubReleaseUnits.Executable, SearchOption.AllDirectories) : [];
            if (executables is not [string executable])
                throw new InvalidDataException("Native publish produced no unique application.");
            publish = Path.GetDirectoryName(executable)!;
            string[] setups = Directory.Exists(installerRoot)
                ? Directory.GetFiles(installerRoot, "*.exe") : [];
            if (setups is not [string setup])
                throw new InvalidDataException("Native installer build produced no unique setup.");
            Directory.CreateDirectory(originals);
            installer = Path.Combine(originals, GithubReleaseUnits.Asset("inno-installer",
                version));
            portable = Path.Combine(originals, GithubReleaseUnits.Asset("portable-zip",
                version));
            File.Copy(setup, installer, overwrite: false);
            CreatePortable(publish, portable);
        });
        await CheckAsync("contents", () =>
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
                !actual.ContainsKey(GithubReleaseUnits.Executable) ||
                !actual.ContainsKey(GithubReleaseUnits.Editor))
                throw new InvalidDataException("Portable package differs from the publish tree.");
            return Task.CompletedTask;
        });
        await CheckAsync("installer-consumer", async () =>
        {
            string target = Path.Combine(consumers, "installed");
            Directory.CreateDirectory(consumers);
            await RunAsync(new(installer, consumers, ["/VERYSILENT", "/SUPPRESSMSGBOXES",
                "/NORESTART", "/SP-", "/NOICONS", "/DIR=" + target,
                "/LOG=" + Path.Combine(consumers, "install.log")], 600));
            RequireConsumed(target, publish);
        });
        await CheckAsync("portable-consumer", () =>
        {
            string target = Path.Combine(consumers, "portable");
            ZipFile.ExtractToDirectory(portable, target);
            RequireConsumed(target, publish);
            return Task.CompletedTask;
        });
        await CheckAsync("original-bytes", () =>
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
        return new(new(request.Subject, [.. files], Required.ToArray(), [.. checks]), native,
            [.. commands], [.. errors]);

        async Task<NativeCommandResult> RunAsync(NativeCommand command)
        {
            var effective = new Dictionary<string, string?>(environment, StringComparer.Ordinal);
            command = command with { Environment = effective };
            NativeCommandResult result = await execute(command, token);
            commands.Add(new(current, command, result));
            if (result.Termination == NativeTermination.Cancelled)
                throw new OperationCanceledException(token);
            if (result.Termination == NativeTermination.TimedOut)
                throw new TimeoutException("Native " + current + " timed out.");
            if (!result.Succeeded || result.Error is not null)
                throw new InvalidDataException("Native " + current + " failed: " +
                    result.Termination + ".");
            token.ThrowIfCancellationRequested();
            return result;
        }

        async Task CheckAsync(string id, Func<Task> action)
        {
            current = id;
            if (checks.Any(check => check.Status != ReleaseCheckStatus.Passed))
            { checks.Add(new(id, ReleaseCheckStatus.Skipped)); return; }
            try
            {
                token.ThrowIfCancellationRequested(); await action();
                token.ThrowIfCancellationRequested();
                checks.Add(new(id, ReleaseCheckStatus.Passed));
            }
            catch (OperationCanceledException)
            { checks.Add(new(id, ReleaseCheckStatus.Cancelled)); errors.Add(id + " cancelled."); }
            catch (TimeoutException)
            { checks.Add(new(id, ReleaseCheckStatus.TimedOut)); errors.Add(id + " timed out."); }
            catch (Exception failure) when (failure is IOException or InvalidDataException or
                UnauthorizedAccessException or JsonException or KeyNotFoundException or
                InvalidOperationException or ArgumentException or FormatException)
            { checks.Add(new(id, ReleaseCheckStatus.Failed)); errors.Add(failure.Message); }
        }
    }

    private static readonly string[] Pwsh = ["-NoLogo", "-NoProfile", "-NonInteractive",
        "-ExecutionPolicy", "Bypass", "-File"];

    // The complete committed recipe, read through a revision-bound reader.
    internal static async Task<ReleaseBuild> ReadRecipeAsync(ReleaseSubject subject,
        Func<string, CancellationToken, Task<string?>> read, CancellationToken token)
    {
        ReleaseUnitDeclaration unit = ReleaseUnitDeclarationReader.Read(
            await read(subject.Declaration, token) ??
                throw new InvalidDataException("Missing committed GitHub Release declaration."),
            subject.Declaration);
        if (unit.Id != subject.Unit || unit.Builds is not [{ Definition:
                GithubReleaseUnits.Definition, EntryPoint: GithubReleaseUnits.Entry,
                Outputs: { Length: 2 } }])
            throw new InvalidDataException("Unsupported complete GitHub Release recipe.");
        ReleaseBuild build = unit.Builds[0];
        if (build.Outputs.Count(output => output is
                { Role: "primary-package", Kind: "inno-installer" }) != 1 ||
            build.Outputs.Count(output => output is
                { Role: "portable-package", Kind: "portable-zip" }) != 1)
            throw new InvalidDataException("Unsupported complete GitHub Release asset set.");
        foreach (string input in Inputs)
            if (await read(input, token) is null)
                throw new InvalidDataException("Missing committed native input: " + input);
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

    internal static (long Size, string Sha256) Original(string path)
    {
        using FileStream stream = File.OpenRead(path);
        long size = stream.Length;
        if (size <= 0) throw new InvalidDataException("Empty original GitHub Release asset.");
        return (size, Convert.ToHexStringLower(SHA256.HashData(stream)));
    }

    private static Dictionary<string, long> Tree(string directory) =>
        Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).ToDictionary(
            file => Path.GetRelativePath(directory, file).Replace('\\', '/'),
            file => new FileInfo(file).Length, StringComparer.Ordinal);

    // A consumer tree must carry the published application byte for byte and its editor.
    private static void RequireConsumed(string target, string publish)
    {
        string executable = Path.Combine(target, GithubReleaseUnits.Executable);
        if (!File.Exists(executable) || !File.Exists(Path.Combine(target,
                GithubReleaseUnits.Editor.Replace('/', Path.DirectorySeparatorChar))) ||
            Original(executable) != Original(Path.Combine(publish, GithubReleaseUnits.Executable)))
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
        ReleaseSubject subject)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["GITHUB_ACTIONS"] = "true",
            ["GITHUB_REF"] = subject.SelectedRef,
            ["GITHUB_SHA"] = subject.Revision,
            ["GITHUB_TOKEN"] = null,
            ["GH_TOKEN"] = null,
            ["ACTIONS_ID_TOKEN_REQUEST_TOKEN"] = null,
            ["ACTIONS_ID_TOKEN_REQUEST_URL"] = null,
            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
        };
        string temporary = Directory.CreateDirectory(Path.Combine(scratch, "tmp")).FullName;
        values["TEMP"] = temporary;
        values["TMP"] = temporary;
        string output = Path.Combine(scratch, "nbgv-environment");
        File.WriteAllText(output, "");
        values["GITHUB_ENV"] = output;
        return values;
    }

    private static string Absolute(string path) =>
        OwnedPath.Absolute(path, "GitHub Release paths must be absolute.");
}
