using System.Text.Json;
using WorkflowDelivery.CI;
using WorkflowDelivery.Ecosystems;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Release;

internal sealed record RubyReleaseRecipe(ReleaseBuild Build, string Entry, string Directory);

// The complete declared native gem is qualified independently of CI selection.
internal static class RubyPreparation
{
    internal static readonly string[] Required = ["native-input", "build", "contents",
        "consumer", "original-bytes"];
    private static readonly string[] Tested = ["native-input", "build", "contents",
        "consumer", "tests", "original-bytes"];

    // A unit adopting the native RSpec suite also requires its successful complete result.
    internal static string[] RequiredFor(RubyReleaseUnit unit) =>
        unit.Preset == RubyPackageChecks.RSpecPreset ? Tested : Required;
    private static readonly string[] ToolConfiguration = ["mise.toml", "mise.lock"];

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
            request.Checkout, request.Scratch, "Ruby Release requires absolute prepared paths.",
            "Ruby Release requires fresh external state.");
        RubyPreparationTools tools = request.Tools
            ?? throw new InvalidDataException("Ruby Release requires prepared tools.");
        string miseData = request.MiseDataDirectory ?? "";
        foreach (string path in new[] { tools.Ruby, tools.Gem, tools.Dotnet, tools.Mise, miseData })
            _ = Absolute(path);
        var qualification = new ReleaseQualification(execute, token);
        var native = new Dictionary<string, string>(StringComparer.Ordinal);
        Dictionary<string, string?> environment = EnvironmentFor(scratch, request, tools);
        GitMaterialization? bound = null;
        RubyPackageOperations? operations = null;
        ReleaseBuild? build = null;
        string[] required = Required;
        string entry = "", directory = "", version = "";
        string? releaseNotes = null;

        await qualification.CheckAsync("native-input", async () =>
        {
            ReleaseSubject subject = request.Subject;
            (RubyReleaseUnit unit, bool official) = Admit(subject);
            required = RequiredFor(unit);
            bound = await GitMaterialization.BindAsync(checkout, subject.Revision, token);
            RubyReleaseRecipe recipe = await ReadRecipeAsync(subject, bound.Root,
                bound.ReadOptionalTextAsync, token);
            (build, entry, directory) = (recipe.Build, recipe.Entry, recipe.Directory);
            string projectDirectory = Path.Combine(checkout, directory);
            // The runtime selection query reads the checkout; the revision must commit it.
            await ReleasePreparationContext.RequireCommittedInputsAsync(bound, ToolConfiguration,
                token);
            await ReleasePreparationContext.RequireCompleteHistoryAsync(checkout,
                command => RunAsync(command));
            string manifestText = await bound.ReadOptionalTextAsync(".config/dotnet-tools.json",
                token) ?? throw new InvalidDataException("Missing committed NBGV tool manifest.");
            using JsonDocument manifest = JsonDocument.Parse(manifestText);
            string pin = manifest.RootElement.GetProperty("tools").GetProperty("nbgv")
                .GetProperty("version").GetString() ??
                    throw new InvalidDataException("Missing committed NBGV pin.");
            await ReleasePreparationContext.RequirePinnedSdkAsync(bound, checkout,
                tools.Dotnet, command => RunAsync(command), token);
            string toolsDirectory = Directory.CreateDirectory(Path.Combine(scratch,
                "native-tools")).FullName;
            await RunAsync(new(tools.Dotnet, checkout,
                ["tool", "install", "nbgv", "--version", pin, "--tool-path", toolsDirectory], 300));
            string nbgv = Path.Combine(toolsDirectory, OperatingSystem.IsWindows() ?
                "nbgv.exe" : "nbgv");
            if (!File.Exists(nbgv))
                throw new InvalidDataException("Native NBGV preparation omitted its executable.");
            using JsonDocument nativeTools = JsonDocument.Parse((await RunAsync(new(
                tools.Ruby, checkout, ["-r", "json", "-r", "rubygems", "-e",
                    "puts JSON.generate({ruby: RUBY_VERSION, rubygems: Gem::VERSION})"], 30)))
                .Stdout);
            string ruby = nativeTools.RootElement.GetProperty("ruby").GetString()!;
            string rubygems = nativeTools.RootElement.GetProperty("rubygems").GetString()!;
            // The prepared runtime is the committed locked selection; no adapter floor applies.
            string selectedRuby = await ReleasePreparationContext.SelectLockedMiseToolAsync(
                checkout, "ruby", command => RunAsync(command),
                MiseEnvironment(environment, checkout, miseData));
            LockedToolSelection.RequireReadback("ruby", selectedRuby, ruby);
            if (string.IsNullOrWhiteSpace(rubygems))
                throw new InvalidDataException("Native Ruby runtime omits its RubyGems version.");
            string helper = Path.Combine(checkout,
                "src/private/app/workflow-delivery/Native/Ruby/facts.rb");
            if (await bound.ReadOptionalTextAsync(
                "src/private/app/workflow-delivery/Native/Ruby/facts.rb", token) is null)
                throw new InvalidDataException("Missing committed native Ruby helper.");
            var facts = new RubyFactTools(tools.Ruby, helper,
                Directory.CreateDirectory(Path.Combine(scratch, "queries")).FullName,
                environment, 180);
            using JsonDocument projection = JsonDocument.Parse((await RunAsync(new(nbgv,
                projectDirectory, ["get-version", "--format", "json"], 180)))
                .Stdout);
            (string raw, bool publicRelease) = QualifyNative(subject, official,
                projection.RootElement);
            version = (await new RubyNativeFactCollector(facts, ExecuteAsync)
                .VersionAsync(Path.Combine(checkout, entry), raw, token)).Version;
            RubySourceGemFacts source = await RubySourceCollector.ReadSourceAsync(bound, entry,
                facts, nbgv, ExecuteAsync, token);
            if (source.Gem.Name != unit.Id ||
                source.Gem.Version != version || source.Gem.Platform != "ruby")
                throw new InvalidDataException("Unsupported native Ruby source identity.");
            if (official)
            {
                string changelogPath = directory + "/CHANGELOG.md";
                string changelog = await bound.ReadOptionalTextAsync(changelogPath, token)
                    ?? throw new InvalidDataException($"Missing committed {changelogPath}.");
                releaseNotes = ReleaseChangelog.Extract(changelog, version, subject.Revision,
                    changelogPath);
            }
            native.Add("name", source.Gem.Name);
            native.Add("version", version);
            native.Add("platform", source.Gem.Platform);
            native.Add("commit", subject.Revision);
            native.Add("public-release", publicRelease ? "true" : "false");
            native.Add("ruby", ruby);
            native.Add("rubygems", rubygems);
            native.Add("nbgv", pin);
            operations = ReleaseUnitCatalog.RubyPackage(unit, facts, tools.Gem, nbgv,
                Directory.CreateDirectory(Path.Combine(scratch, "package")).FullName);
        });
        ReleaseFile[] files = await UnitPreparation.QualifyAsync(request.Subject, build, scratch,
            qualification, required, subject => operations!.QualifyAsync(subject, bound!, directory,
                entry, version, RunSuiteAsync, qualification.CheckAsync, ExecuteAsync, token));
        PreparedGithubAnchor? anchor = releaseNotes is not null &&
            qualification.Errors.Length == 0 && files.Length > 0
                ? DestinationBindings.GithubAnchor(request.Subject, files, releaseNotes)
                : null;
        return new(new(request.Subject, [.. files], required.ToArray(), qualification.Checks,
                Anchor: anchor),
            native, qualification.Commands, qualification.Errors);

        async Task RunSuiteAsync(RubyRSpecRequest suite)
        {
            RubyRSpecRun run = await RubyRSpecExecution.RunAsync(
                new(directory, RubyProjectChecks.RSpec, "default"), suite, ExecuteAsync, token);
            if (run.Status != CheckStatus.Passed)
                throw new InvalidDataException(run.ReportError ?? "Native RSpec suite did not " +
                    "pass: " + run.Status + ". " + NativeDiagnostic.Describe(run.Result));
        }

        Task<NativeCommandResult> ExecuteAsync(NativeCommand command,
            CancellationToken cancellation) => RunAsync(command, cancellation);

        Task<NativeCommandResult> RunAsync(NativeCommand command,
            CancellationToken? cancellation = null) =>
            qualification.RunAsync(command with
            {
                Environment = command.Environment ?? environment,
                InheritEnvironment = false,
                CloseStandardInput = true,
            }, cancellation, requireNullError: true);
    }

    // Pure subject admission, before any Git or native work.
    internal static (RubyReleaseUnit Unit, bool Official) Admit(ReleaseSubject subject)
    {
        bool buddy = subject.Channel == "buddy" && subject.Destination == "github-packages-ruby";
        bool official = subject.Channel == "official" && subject.Destination == "rubygems";
        if (!(buddy || official) || string.IsNullOrWhiteSpace(subject.SelectedRef))
            throw new InvalidDataException("Unsupported Ruby Release channel or ref.");
        ReleasePolicy.RequireEligible(subject);
        if (!GitReader.IsCommitId(subject.Revision))
            throw new InvalidDataException("Ruby Release requires the exact source revision.");
        return (ReleaseUnitCatalog.AdmitRuby(subject), official);
    }

    // The complete committed recipe, read through a revision-bound reader.
    internal static async Task<RubyReleaseRecipe> ReadRecipeAsync(ReleaseSubject subject,
        string root, Func<string, CancellationToken, Task<string?>> read,
        CancellationToken token)
    {
        ReleaseUnitDeclaration unit = ReleaseUnitDeclarationReader.Read(
            await read(subject.Declaration, token) ??
                throw new InvalidDataException("Missing committed Ruby release declaration."),
            subject.Declaration);
        if (unit.Id != subject.Unit || unit.Builds is not
            [
            {
                Definition: "ruby/gem-v1", Outputs:
                [{ Role: "primary-package", Kind: "ruby-gem" }]
            }])
            throw new InvalidDataException("Unsupported complete Ruby Release recipe.");
        ReleaseBuild build = unit.Builds[0];
        string parent = Path.GetDirectoryName(subject.Declaration)!.Replace('\\', '/');
        string entry = parent.Length == 0 ? build.EntryPoint : parent + "/" + build.EntryPoint;
        string directory = Path.GetDirectoryName(entry)!.Replace('\\', '/');
        if (Path.GetFileName(entry) != subject.Unit + ".gemspec" ||
            await read(entry, token) is null)
            throw new InvalidDataException("Missing committed adopted Ruby entry.");
        QualitySelection? quality = await new QualitySelectionReader(root, read)
            .ReadAsync(directory, "ruby", token);
        if (quality?.Preset != ReleaseUnitCatalog.Ruby(unit.Id).Preset)
            throw new InvalidDataException("Unsupported complete Ruby quality selection.");
        return new(build, entry, directory);
    }

    // Pure check of the native NBGV projection against the admitted subject.
    internal static (string Raw, bool PublicRelease) QualifyNative(ReleaseSubject subject,
        bool official, JsonElement projection)
    {
        string raw = projection.GetProperty("SemVer2").GetString()!;
        bool publicRelease = projection.GetProperty("PublicRelease").GetBoolean();
        if (projection.GetProperty("GitCommitId").GetString() != subject.Revision ||
            !subject.DryRun && official && !publicRelease)
            throw new InvalidDataException("Native Ruby source/ref projection differs.");
        return (raw, publicRelease);
    }

    private static Dictionary<string, string?> EnvironmentFor(string scratch,
        ReleasePreparationRequest request, RubyPreparationTools tools)
    {
        string home = Path.Combine(scratch, "home");
        string gems = Path.Combine(scratch, "gems");
        // The selected mise Ruby's own RubyGems plugin runs `mise reshim` after installs.
        string path = string.Join(Path.PathSeparator, new[] { tools.Ruby,
            tools.Gem, tools.Dotnet, tools.Mise }
            .Select(Path.GetDirectoryName)
            .Append(OperatingSystem.IsWindows() ? Environment.GetFolderPath(
                Environment.SpecialFolder.System) : "/usr/bin").Append("/bin")
            .Distinct(StringComparer.Ordinal));
        (string Key, string? Value)[] overrides =
        [
            ("SystemRoot", Environment.GetEnvironmentVariable("SystemRoot")),
            ("WINDIR", Environment.GetEnvironmentVariable("WINDIR")),
            ("PATHEXT", Environment.GetEnvironmentVariable("PATHEXT")),
            ("PATH", path), ("DOTNET_ROOT", Path.GetDirectoryName(tools.Dotnet)),
            ("GEM_HOME", gems), ("GEM_PATH", gems),
            ("GEM_SPEC_CACHE", Path.Combine(scratch, "spec-cache")),
            ("LANG", "C.UTF-8"), ("LC_ALL", "C.UTF-8"),
        ];
        return ReleasePreparationContext.CreateHomeEnvironment([], overrides.Concat(
            ReleasePreparationContext.GithubVersioningEnvironment(scratch, request.Subject)),
            home, Path.Combine(home, "tmp"), createProfileDirectories: true);
    }

    // Only the committed selection query sees the caller's tool installation and PATH.
    private static Dictionary<string, string?> MiseEnvironment(
        Dictionary<string, string?> environment, string checkout, string data)
    {
        var values = new Dictionary<string, string?>(environment, StringComparer.Ordinal)
        {
            ["PATH"] = Environment.GetEnvironmentVariable("PATH"),
            ["MISE_DATA_DIR"] = data,
            ["MISE_CACHE_DIR"] = Path.Combine(environment["HOME"]!, "mise-cache"),
            ["MISE_TRUSTED_CONFIG_PATHS"] = checkout,
            ["MISE_AUTO_INSTALL"] = "0",
            ["MISE_EXEC_AUTO_INSTALL"] = "0",
        };
        return values;
    }

    private static string Absolute(string path) =>
        OwnedPath.Absolute(path, "Ruby Release requires absolute prepared paths.");
}
