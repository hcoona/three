using System.Text.Json;
using WorkflowDelivery.Ecosystems;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Release;

internal sealed record PythonPreparationRequest(ReleaseSubject Subject, string Checkout,
    string Scratch, string MiseDataDirectory);
internal sealed record PythonReleaseRecipe(ReleaseBuild Build, string Directory);
internal sealed record PythonPreparationResult(PreparedRelease Prepared,
    Dictionary<string, string> Native, ReleaseCommand[] Commands, string[] Errors)
    : IReleasePreparation
{
    public void Validate() => PythonRelease.Validate(this);
}

// Release qualifies its complete declaration independently of CI impact selection.
internal static class PythonPreparation
{
    internal static readonly string[] Required = ["native-input", "build", "contents",
        "wheel-consumer", "sdist-consumer", "original-bytes"];
    private static readonly string[] Tested = ["native-input", "build", "contents",
        "wheel-consumer", "sdist-consumer", "tests", "original-bytes"];

    // A unit adopting its native pytest suite also requires its successful complete result.
    internal static string[] RequiredFor(PythonReleaseUnit unit) =>
        unit.Tests is null ? Required : Tested;
    private static readonly string[] Inputs = ["pyproject.toml", "uv.lock", "mise.toml",
        "mise.lock", "global.json", ".config/dotnet-tools.json",
        "src/private/app/workflow-delivery/Native/Python/packages.py"];

    internal static Task<PythonPreparationResult> PrepareAsync(PythonPreparationRequest request,
        CancellationToken token) => PrepareAsync(request, NativeProcess.ExecuteAsync, token);

    internal static async Task<PythonPreparationResult> PrepareAsync(
        PythonPreparationRequest request,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        CancellationToken token)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        lifetime.CancelAfter(TimeSpan.FromMinutes(15));
        token = lifetime.Token;
        (string checkout, string scratch) = ReleaseQualification.RequireFreshScratch(
            request.Checkout, request.Scratch, "Python Release paths must be absolute.",
            "Python package execution requires fresh external scratch.");
        string helper = Path.Combine(checkout,
            "src/private/app/workflow-delivery/Native/Python/packages.py");
        // Validate Git-free scratch before creating any owned execution state.
        PythonPackageOperations.RequireGitFreeScratch(scratch);
        var qualification = new ReleaseQualification(execute, token);
        var files = new List<ReleaseFile>();
        var native = new Dictionary<string, string>(StringComparer.Ordinal);
        Dictionary<string, string?> environment = EnvironmentFor(scratch, checkout,
            request.Subject);
        // The caller supplies the platform's existing tool installation resource.
        environment["MISE_DATA_DIR"] = Absolute(request.MiseDataDirectory);
        PythonPackageOperations? operations = null;
        string[] required = Required;
        ReleaseBuild? build = null;
        PythonDistribution[] originals = [];
        string name = "", version = "";

        await qualification.CheckAsync("native-input", async () =>
        {
            ReleaseSubject subject = request.Subject;
            (PythonReleaseUnit unit, bool official) = Admit(subject);
            required = RequiredFor(unit);
            GitMaterialization bound = await GitMaterialization.BindAsync(checkout,
                subject.Revision, token);
            PythonReleaseRecipe recipe = await ReadRecipeAsync(subject, bound.Root,
                bound.ReadOptionalTextAsync, token);
            build = recipe.Build;
            string directory = recipe.Directory;
            await ReleasePreparationContext.RequireCompleteHistoryAsync(checkout,
                command => RunAsync(command));
            string uvVersion = await ToolAsync("uv");
            string pythonVersion = await ToolAsync("python");
            string interpreter = Absolute((await RunAsync(new("mise", checkout,
                ["which", "python"], 30))).Stdout.Trim());
            string uv = Absolute((await RunAsync(new("mise", checkout,
                ["which", "uv"], 30))).Stdout.Trim());
            LockedToolSelection.RequireReadback("uv", uvVersion, (await RunAsync(new(uv,
                checkout, ["self", "version", "--short"], 30))).Stdout.Trim());
            await RunAsync(new("dotnet", checkout, ["tool", "restore"], 300));
            operations = PythonReleaseUnits.Package(unit.Id, new(checkout, scratch, uv,
                interpreter, helper, 300, environment, false), Path.Combine(scratch, "package"));
            (name, version) = await operations.PrepareAsync(directory, ExecuteAsync, token);
            using JsonDocument facts = await operations.VersionFactsAsync(directory,
                ExecuteAsync, token);
            bool? publicRelease = QualifyNative(subject, official, name, version,
                pythonVersion, facts.RootElement);
            native.Add("name", name);
            native.Add("version", version);
            native.Add("commit", subject.Revision);
            if (publicRelease is { } release)
                native.Add("public-release", release ? "true" : "false");
            native.Add("python", pythonVersion);
            native.Add("uv", uvVersion);
        });
        await qualification.CheckAsync("build", async () => originals =
            await operations!.BuildAsync(name, version, ExecuteAsync, token));
        await qualification.CheckAsync("contents", () =>
            operations!.ContentsAsync(originals, name, version, ExecuteAsync, token));
        await qualification.CheckAsync("wheel-consumer", () =>
            operations!.ConsumeAsync("python-wheel", originals, name, version, ExecuteAsync,
                token));
        await qualification.CheckAsync("sdist-consumer", () =>
            operations!.ConsumeAsync("python-sdist", originals, name, version, ExecuteAsync,
                token));
        if (required.Contains("tests"))
            await qualification.CheckAsync("tests", () =>
                operations!.TestReleaseAsync(ExecuteAsync, token));
        await qualification.CheckAsync("original-bytes", () =>
        {
            foreach (PythonDistribution original in originals)
                PythonPackageOperations.RequireOriginal(original);
            foreach (PythonDistribution original in originals)
            {
                ReleaseOutput output = build!.Outputs.Single(value => value.Kind == original.Kind);
                files.Add(new(build.Id, output.Id, original.Kind, name, version,
                    Path.GetRelativePath(scratch, original.Path).Replace('\\', '/'),
                    original.Length, original.Sha256));
            }
            return Task.CompletedTask;
        });
        return new(new(request.Subject, [.. files], required.ToArray(), qualification.Checks),
            native, qualification.Commands, qualification.Errors);

        Task<NativeCommandResult> ExecuteAsync(NativeCommand command,
            CancellationToken cancellation)
            => RunAsync(command, cancellation);

        Task<NativeCommandResult> RunAsync(NativeCommand command,
            CancellationToken? cancellation = null)
        {
            var effective = new Dictionary<string, string?>(environment, StringComparer.Ordinal);
            if (command.Environment is not null)
                foreach (var pair in command.Environment) effective[pair.Key] = pair.Value;
            return qualification.RunAsync(command with
            {
                Environment = effective,
                InheritEnvironment = false,
            }, cancellation, requireNullError: true);
        }

        Task<string> ToolAsync(string tool) => ReleasePreparationContext.SelectLockedMiseToolAsync(
            checkout, tool, command => RunAsync(command));
    }

    // Pure subject admission, before any Git or native work.
    internal static (PythonReleaseUnit Unit, bool Official) Admit(ReleaseSubject subject)
    {
        bool buddy = subject.Channel == "buddy" && subject.Destination == "testpypi";
        bool official = subject.Channel == "official" && subject.Destination == "pypi";
        if (!(buddy || official) || string.IsNullOrWhiteSpace(subject.SelectedRef))
            throw new InvalidDataException("Unsupported Python Release channel or ref.");
        ReleasePolicy.RequireEligible(subject);
        if (!GitReader.IsCommitId(subject.Revision))
            throw new InvalidDataException("Python Release requires the exact selected revision.");
        return (PythonReleaseUnits.Admit(subject), official);
    }

    // The complete committed recipe and native inputs, read through a revision-bound reader.
    internal static async Task<PythonReleaseRecipe> ReadRecipeAsync(ReleaseSubject subject,
        string root, Func<string, CancellationToken, Task<string?>> read,
        CancellationToken token)
    {
        ReleaseUnitDeclaration unit = ReleaseUnitDeclarationReader.Read(
            await read(subject.Declaration, token) ??
                throw new InvalidDataException("Missing committed Python release declaration."),
            subject.Declaration);
        if (unit.Id != subject.Unit || unit.Builds is not
            [{ Definition: "python/distribution-set-v1", Outputs: { Length: 2 } }])
            throw new InvalidDataException("Unsupported complete Python Release recipe.");
        ReleaseBuild build = unit.Builds[0];
        if (build.Outputs.Count(output => output is
            { Role: "primary-package", Kind: "python-wheel" }) != 1 ||
            build.Outputs.Count(output => output is
            { Role: "source-package", Kind: "python-sdist" }) != 1)
            throw new InvalidDataException("Unsupported complete Python distribution set.");
        string parent = Path.GetDirectoryName(subject.Declaration)!.Replace('\\', '/');
        string entry = parent.Length == 0 ? build.EntryPoint : parent + "/" + build.EntryPoint;
        string directory = Path.GetDirectoryName(entry)!.Replace('\\', '/');
        if (Path.GetFileName(entry) != "pyproject.toml" || await read(entry, token) is null)
            throw new InvalidDataException("Missing committed native Python entry.");
        QualitySelection? quality = await new QualitySelectionReader(root, read).ReadAsync(
            directory.Length == 0 ? "." : directory, "python", token);
        if (quality?.Preset != PythonPackageOperations.Preset)
            throw new InvalidDataException("Unsupported complete Python quality selection.");
        await ReleasePreparationContext.RequireCommittedInputsAsync(read, Inputs, token);
        return new(build, directory);
    }

    // Pure check of the native version facts against the admitted subject. Only the NBGV
    // projection of the smoke unit has a public-release fact.
    internal static bool? QualifyNative(ReleaseSubject subject, bool official, string name,
        string version, string pythonVersion, JsonElement value)
    {
        if (name != subject.Unit || value.GetProperty("name").GetString() != name ||
            value.GetProperty("version").GetString() != version ||
            value.GetProperty("implementation").GetString() != "cpython" ||
            value.GetProperty("interpreter_version").GetString() != pythonVersion ||
            !subject.DryRun && value.GetProperty("local_version").ValueKind !=
                JsonValueKind.Null)
            throw new InvalidDataException("Unsupported native Python Release identity.");
        if (name == PythonReleaseUnits.Nbgv)
        {
            // Hatchling's static [project].version; no NBGV projection (WD-REL-001).
            if (value.EnumerateObject().Any(item => item.Name is "commit" or "public_release")
                || value.GetProperty("dependencies").GetArrayLength() != 1 ||
                value.GetProperty("dependencies")[0].GetString() != "packaging>=24.0")
                throw new InvalidDataException("Unsupported native Python Release identity.");
            return null;
        }
        bool publicRelease = value.GetProperty("public_release").GetBoolean();
        if (name != PythonReleaseUnits.Smoke ||
            value.GetProperty("commit").GetString() != subject.Revision ||
            value.GetProperty("dependencies").GetArrayLength() != 0 ||
            !subject.DryRun && official && !publicRelease)
            throw new InvalidDataException("Unsupported native Python Release identity.");
        return publicRelease;
    }

    private static Dictionary<string, string?> EnvironmentFor(string scratch, string checkout,
        ReleaseSubject subject)
    {
        string home = Path.Combine(scratch, "home");
        var values = ReleasePreparationContext.CreateHomeEnvironment(
            ["PATH", "SystemRoot", "WINDIR", "PATHEXT", "DOTNET_ROOT", "DOTNET_ROOT_X64",
                "MISE_DATA_DIR"],
            [("MISE_TRUSTED_CONFIG_PATHS", checkout), ("MISE_AUTO_INSTALL", "0"),
                ("MISE_EXEC_AUTO_INSTALL", "0"), ("UV_PYTHON_DOWNLOADS", "never"),
                ("UV_NO_PROGRESS", "1"), ("PYTHONDONTWRITEBYTECODE", "1")],
            home, Path.Combine(home, "tmp"), createProfileDirectories: true);
        values["MISE_CACHE_DIR"] = Directory.CreateDirectory(Path.Combine(home, "mise-cache"))
            .FullName;
        foreach (var (key, value) in ReleasePreparationContext.GithubVersioningEnvironment(
            scratch, subject))
            values[key] = value;
        return values;
    }

    private static string Absolute(string path) =>
        OwnedPath.Absolute(path, "Python Release paths must be absolute.");
}
