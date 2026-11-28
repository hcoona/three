using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using WorkflowDelivery.Core;
using WorkflowDelivery.Destinations;
using WorkflowDelivery.Ecosystems;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Release;

// Inputs for the existing concrete Actions callers. Actions owns job orchestration.
internal static class ReleaseJobInputs
{
    private sealed record Route(IDestinationAdapter Adapter, Func<string, string> Declaration,
        Action<ReleaseSubject>? Admit = null);

    private static readonly Dictionary<string, Route> Routes = new(StringComparer.Ordinal)
    {
        ["ruby"] = new(ReleaseAdapters.Ruby,
            unit => ReleaseUnitCatalog.Ruby(unit).Declaration,
            subject => ReleaseUnitCatalog.AdmitRuby(subject)),
        ["python"] = new(ReleaseAdapters.Python,
            unit => ReleaseUnitCatalog.Python(unit).Declaration,
            subject => ReleaseUnitCatalog.AdmitPython(subject)),
        ["npm"] = new(ReleaseAdapters.Npm,
            unit => ReleaseUnitCatalog.Npm(unit).Declaration,
            subject => ReleaseUnitCatalog.AdmitNpm(subject)),
        ["nuget"] = new(ReleaseAdapters.NuGet,
            unit => ReleaseUnitCatalog.NuGet(unit).Declaration,
            subject => ReleaseUnitCatalog.AdmitNuGet(subject)),
        ["github-release"] = new(ReleaseAdapters.GithubRelease,
            unit => ReleaseUnitCatalog.GithubRelease(unit).Declaration,
            subject => ReleaseUnitCatalog.AdmitGithubRelease(subject)),
        ["browser-store"] = new(ReleaseAdapters.BrowserStore,
            unit => ReleaseUnitCatalog.BrowserStore(unit).Declaration,
            subject => ReleaseUnitCatalog.AdmitBrowserStore(subject)),
    };

    internal static int? Run(string[] args, Func<string, string?>? environment = null)
    {
        if (args is not ["release", "job-inputs", var ecosystem, .. var operation]) return null;
        if (!Routes.TryGetValue(ecosystem, out Route? route))
            throw new InvalidDataException("Unsupported Release caller ecosystem.");
        environment ??= Environment.GetEnvironmentVariable;
        string Env(string name) => environment(name) is { Length: > 0 } value ? value :
            throw new InvalidDataException("Missing caller input: " + name);
        string root = Path.GetFullPath(Env("RUNNER_TEMP"));
        if (!Path.IsPathFullyQualified(Env("RUNNER_TEMP")) || !Directory.Exists(root))
            throw new InvalidDataException("Release caller requires native runner scratch.");
        string destination = Env("DESTINATION");
        (bool buddy, string selectedDestination) = (ecosystem, destination) switch
        {
            ("ruby", "github-packages") => (true, "github-packages-ruby"),
            ("npm", "github-packages") => (true, "github-packages-npm"),
            ("nuget", "github-packages") => (true, "github-packages-nuget"),
            ("python", "testpypi") or
                ("github-release", GithubReleaseUnit.BuddyDestination) =>
                (true, destination),
            ("ruby", "rubygems") or ("python", "pypi") or ("npm", "npmjs") or
                ("nuget", "nugetorg") or
                ("github-release", GithubReleaseUnit.OfficialDestination) =>
                (false, destination),
            // Browser stores are Official destinations; a Buddy request is a dry run.
            ("browser-store", BrowserStorePreparation.ChromeWebStore or
                BrowserStorePreparation.FirefoxAmo or BrowserStorePreparation.EdgeAddons) =>
                Env("CHANNEL") switch
                {
                    "buddy" => (true, destination),
                    "official" => (false, destination),
                    _ => throw new InvalidDataException("Unsupported browser-store channel."),
                },
            _ => throw new InvalidDataException("Unsupported Release caller destination."),
        };
        string dry = Env("DRY_RUN");
        if (dry is not ("true" or "false"))
            throw new InvalidDataException("Invalid dry-run input.");
        string unit = ecosystem == "browser-store"
            ? environment("UNIT") is { Length: > 0 } u ? u
                : BrowserStorePreparation.DefaultUnit.Id
            : Env("UNIT");
        SubmissionClearance clearance = selectedDestination == BrowserStorePreparation.EdgeAddons
            ? environment("EDGE_CLEARANCE") switch
            {
                null or "" or "unresolved" => SubmissionClearance.Unresolved,
                "first" => SubmissionClearance.First,
                "owner-checked" => SubmissionClearance.OwnerChecked,
                _ => throw new InvalidDataException("Unsupported Edge submission clearance."),
            } : SubmissionClearance.Unresolved;
        var subject = new ReleaseSubject(Env("GITHUB_SHA"), Env("GITHUB_REF"), unit,
            route.Declaration(unit), buddy ? "buddy" : "official", selectedDestination,
            dry == "true", clearance);
        route.Admit?.Invoke(subject);
        BrowserStoreTarget? store = ecosystem == "browser-store" ? new(destination,
            Env("STORE_ITEM"), destination == BrowserStorePreparation.ChromeWebStore
                ? Env("STORE_PUBLISHER") : null) : null;
        if (store is not null) BrowserStorePreparation.RequireRequest(subject, store);
        if (!subject.DryRun && Env("GITHUB_RUN_ATTEMPT") != "1")
            throw new InvalidDataException("Live Release reruns are unsupported.");

        string control = Path.Combine(root, "control"), prepared = Path.Combine(root, "prepared");
        string At(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Contains('\\') || name.Contains(':') ||
                name.Split('/').Any(part => part is "" or "." or ".."))
                throw new InvalidDataException("Expected a relative caller path.");
            return Path.GetFullPath(Path.Combine(root, name));
        }
        string Fresh(string name)
        {
            string path = At(name);
            if (File.Exists(path) || Directory.Exists(path))
                throw new InvalidDataException("Caller scratch already exists.");
            Directory.CreateDirectory(path);
            return path;
        }
        void Same(ReleaseSubject actual)
        {
            if (actual != subject)
                throw new InvalidDataException("Transferred Release subject mismatch.");
        }
        ReleasePreparationResult Preparation()
        {
            ReleasePreparationResult preparation = Read(
                Path.Combine(prepared, "preparation.json"),
                TransferJson.Default.ReleasePreparationResult);
            Same(preparation.Prepared.Subject);
            if (store is not null && preparation.Target != store)
                throw new InvalidDataException("Transferred store target mismatch.");
            return preparation;
        }

        switch (operation)
        {
            case ["request"]:
                if (Env("GITHUB_REPOSITORY") != "hcoona/three")
                    throw new InvalidDataException("Unsupported Release repository.");
                string scratch = Fresh("preparation-scratch");
                Fresh("prepared");
                RubyPreparationTools? tools = ecosystem == "ruby"
                    ? new(Env("RUBY_EXECUTABLE"), Env("GEM_EXECUTABLE"),
                        Env("DOTNET_EXECUTABLE"), Env("MISE_EXECUTABLE"))
                    : null;
                string? miseData = ecosystem is "ruby" or "python"
                    ? Env("MISE_DATA_DIR") : null;
                Write(Path.Combine(prepared, "request.json"), new ReleasePreparationRequest(
                    subject, Env("GITHUB_WORKSPACE"), scratch, store, tools, miseData),
                    TransferJson.Default.ReleasePreparationRequest);
                break;
            case ["originals"]:
                PreparedRelease release = Preparation().Prepared;
                string sourceRoot = At("preparation-scratch");
                foreach (ReleaseFile file in release.Files)
                {
                    string source = At("preparation-scratch/" + file.Path);
                    string target = At("prepared/" + file.Path);
                    if (!File.Exists(source) || !OwnedPath.Within(source, sourceRoot))
                        throw new InvalidDataException("Missing original transfer.");
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(source, target, overwrite: false);
                }
                break;
            case ["receive", var phase] when phase is "observation" or "publication" or
                "anchor-publication" or "verification":
                ReleasePreparationResult transferred = Preparation();
                route.Adapter.ValidatePreparation(transferred);
                if (phase == "anchor-publication" && transferred.Prepared.Anchor is null)
                    throw new InvalidDataException(
                        "Release preparation carries no GitHub Release anchor.");
                Fresh(phase);
                break;
            case ["state"]:
                ReleasePreparationResult statePrep = Preparation();
                ReleaseObservationResult observed = Read(At("observation/observation.json"),
                    TransferJson.Default.ReleaseObservationResult);
                string anchorObsPath = At("observation/anchor-observation.json");
                ReleaseObservationResult? anchorObserved = statePrep.Prepared.Anchor is null
                    ? null : Read(anchorObsPath, TransferJson.Default.ReleaseObservationResult);
                if (anchorObserved is null && File.Exists(anchorObsPath))
                    throw new InvalidDataException(
                        "Unexpected GitHub Release anchor observation.");
                ReleaseDryRun? simulation = subject.DryRun
                    ? ReleaseEngine.Describe(route.Adapter, statePrep, observed, anchorObserved)
                    : null;
                bool needed = simulation is not null ? simulation.HypotheticalWrites.Length != 0
                    : ReleaseEngine.MayEnterPublisher(route.Adapter, statePrep, observed);
                // A typed refusal is valid verifier input, not a successful write preflight.
                bool primaryProceeds = ReleaseEngine.ObservationProceeds(
                    route.Adapter.Profile(subject), observed.Observation);
                bool anchorProceeds = anchorObserved is null ||
                    ReleaseEngine.AnchorObservationProceeds(subject, anchorObserved);
                if ((primaryProceeds && File.Exists(At("primary-observation-refused"))) ||
                    (anchorProceeds && File.Exists(At("anchor-observation-refused"))))
                    throw new InvalidDataException(
                        "Observation exit status 1 contradicts its result.");
                bool preflightProceeds = simulation is not null ||
                    (primaryProceeds && anchorProceeds);
                string outputLines =
                    "write-needed=" + (needed && preflightProceeds ? "true" : "false") + "\n";
                if (anchorObserved is not null)
                {
                    bool anchorNeeded = simulation is not null
                        ? simulation.HypotheticalAnchorWrites is { Length: > 0 }
                        : ReleaseEngine.MayEnterPublisher(ReleaseAdapters.GithubAnchor,
                            ReleaseAdapters.AnchorPreparation(statePrep), anchorObserved);
                    outputLines += "anchor-write-needed=" +
                        (anchorNeeded && preflightProceeds ? "true" : "false") + "\n";
                }
                File.AppendAllText(Env("GITHUB_OUTPUT"), outputLines);
                break;
            case ["source-config", var directory] when ecosystem == "ruby":
                Write(At("source-tools.json"), new RubySourceTools(Env("RUBY_EXECUTABLE"),
                    Path.Combine(control, "source.rb"), Fresh(directory)),
                    DestinationToolJson.Default.RubySourceTools);
                break;
            case ["publisher-config", var directory] when ecosystem == "ruby":
                Write(At("publisher-tools.json"), new RubyPublisherTools(Env("RUBY_EXECUTABLE"),
                    Env("GEM_EXECUTABLE"), Fresh(directory)),
                    DestinationToolJson.Default.RubyPublisherTools);
                break;
            case ["tool-request"] when ecosystem == "python":
                Write(At("index-tool-request.json"), new PythonIndexToolRequest(
                    Env("UV_EXECUTABLE"), Env("PYTHON_EXECUTABLE"),
                    Path.Combine(control, "index-requirements.txt"),
                    Fresh("index-tool-scratch")),
                    DestinationToolJson.Default.PythonIndexToolRequest);
                break;
            case ["index-config", var directory] when ecosystem == "python":
                Write(At("index-tools.json"), new PythonIndexTools(Read(
                    At("index-tool-result.json"),
                    DestinationToolJson.Default.PythonIndexToolResult).Interpreter,
                    Path.Combine(control, "packages.py"), Fresh(directory)),
                    DestinationToolJson.Default.PythonIndexTools);
                break;
            case ["publisher-config"] when ecosystem == "python":
                Write(At("publisher-tools.json"), new PythonPublisherTools(Env("UV_EXECUTABLE"),
                    Fresh("publisher-scratch")), DestinationToolJson.Default.PythonPublisherTools);
                break;
            default:
                throw new InvalidDataException("Unsupported ordinary Release caller operation.");
        }
        return 0;
    }

    private static T Read<T>(string path, JsonTypeInfo<T> type) =>
        ReleaseCli.Read(path, type);
    private static void Write<T>(string path, T value, JsonTypeInfo<T> type) =>
        File.WriteAllText(path, JsonSerializer.Serialize(value, type) + "\n");
}
