using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using WorkflowDelivery.Destinations;
using WorkflowDelivery.Ecosystems;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Release;

// Inputs for the existing concrete Actions callers. Actions owns job orchestration.
internal static class ReleaseJobInputs
{
    private sealed record Route(Func<string, string> Declaration,
        Func<string, IReleasePreparation> Preparation,
        Action<ReleaseSubject>? Admit = null,
        Func<string, IReleaseObservation>? Observation = null,
        Func<string, IReleaseObservation>? BuddyObservation = null,
        Action<RequestInputs>? WriteRequest = null);

    private sealed record RequestInputs(string Path, ReleaseSubject Subject, string Checkout,
        string Scratch, Func<string, string> Env, BrowserStoreTarget? Store);

    private static readonly Dictionary<string, Route> Routes = new(StringComparer.Ordinal)
    {
        ["ruby"] = new(unit => RubyReleaseUnits.Named(unit).Declaration,
            Reader(TransferJson.Default.RubyPreparationResult),
            subject => RubyReleaseUnits.Admit(subject),
            Reader(TransferJson.Default.RubyObservationResult),
            WriteRequest: input => Write(input.Path, new RubyPreparationRequest(input.Subject,
                input.Checkout, input.Scratch, new(input.Env("RUBY_EXECUTABLE"),
                    input.Env("GEM_EXECUTABLE"), input.Env("DOTNET_EXECUTABLE"),
                    input.Env("MISE_EXECUTABLE")), input.Env("MISE_DATA_DIR")),
                TransferJson.Default.RubyPreparationRequest)),
        ["python"] = new(unit => PythonReleaseUnits.Named(unit).Declaration,
            Reader(TransferJson.Default.PythonPreparationResult),
            subject => PythonReleaseUnits.Admit(subject),
            Reader(TransferJson.Default.PythonIndexObservationResult),
            WriteRequest: input => Write(input.Path, new PythonPreparationRequest(input.Subject,
                input.Checkout, input.Scratch, input.Env("MISE_DATA_DIR")),
                TransferJson.Default.PythonPreparationRequest)),
        ["npm"] = new(unit => NpmReleaseUnits.Named(unit).Declaration,
            Reader(TransferJson.Default.NpmPreparationResult),
            subject => NpmReleaseUnits.Admit(subject),
            Reader(TransferJson.Default.NpmjsObservationResult),
            Reader(TransferJson.Default.GithubNpmObservationResult)),
        ["nuget"] = new(unit => NuGetReleaseUnits.Named(unit).Declaration,
            Reader(TransferJson.Default.NuGetPreparationResult),
            subject => NuGetReleaseUnits.Admit(subject),
            Reader(TransferJson.Default.NuGetObservationResult)),
        ["github-release"] = new(_ => GithubReleaseUnits.Declaration,
            Reader(TransferJson.Default.GithubReleasePreparationResult),
            subject => GithubReleaseUnits.Admit(subject),
            Reader(TransferJson.Default.GithubReleaseObservationResult)),
        ["browser-store"] = new(_ => BrowserStorePreparation.Declaration,
            Reader(TransferJson.Default.BrowserStorePreparationResult),
            WriteRequest: input => Write(input.Path, new BrowserStorePreparationRequest(
                input.Subject, input.Store!, input.Checkout, input.Scratch),
                TransferJson.Default.BrowserStorePreparationRequest)),
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
            ("python", "testpypi") or ("github-release", GithubReleaseUnits.Buddy) =>
                (true, destination),
            ("ruby", "rubygems") or ("python", "pypi") or ("npm", "npmjs") or
                ("nuget", "nugetorg") or ("github-release", GithubReleaseUnits.Official) =>
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
        if (dry is not ("true" or "false")) throw new InvalidDataException(
            "Invalid dry-run input.");
        string unit = ecosystem == "browser-store" ? BrowserStorePreparation.Unit :
            Env("UNIT");
        var subject = new ReleaseSubject(Env("GITHUB_SHA"), Env("GITHUB_REF"), unit,
            route.Declaration(unit), buddy ? "buddy" : "official", selectedDestination,
            dry == "true");
        // Every job admits the selected registered unit before preparation; npm also
        // rejects its smoke unit for live Official, and NuGet rejects it for Official.
        route.Admit?.Invoke(subject);
        // The reviewed store coordinate is bound into the request from repository
        // variables; later jobs compare it with the transferred preparation.
        BrowserStoreTarget? store = ecosystem == "browser-store" ? new(destination,
            Env("STORE_ITEM"), destination == BrowserStorePreparation.ChromeWebStore
                ? Env("STORE_PUBLISHER") : null) : null;
        if (store is not null) BrowserStorePreparation.RequireRequest(subject, store);
        // Live reruns are unsupported (WD-OPS-003). A partial rerun skips the preparation
        // job, so every job entry below applies this one guard rather than workflow YAML.
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
        IReleasePreparation Preparation()
        {
            IReleasePreparation preparation = route.Preparation(
                Path.Combine(prepared, "preparation.json"));
            Same(preparation.Prepared.Subject);
            return preparation;
        }
        void Same(ReleaseSubject actual)
        {
            if (actual != subject) throw new InvalidDataException(
                "Transferred Release subject mismatch.");
        }
        switch (operation)
        {
            case ["request"]:
                if (Env("GITHUB_REPOSITORY") != "hcoona/three")
                    throw new InvalidDataException("Unsupported Release repository.");
                string scratch = Fresh("preparation-scratch");
                Fresh("prepared");
                var input = new RequestInputs(Path.Combine(prepared, "request.json"), subject,
                    Env("GITHUB_WORKSPACE"), scratch, Env, store);
                if (route.WriteRequest is { } writeRequest) writeRequest(input);
                else
                    Write(input.Path, new ReleasePreparationRequest(subject, input.Checkout,
                        scratch), TransferJson.Default.ReleasePreparationRequest);
                break;
            case ["originals"]:
                PreparedRelease release = Preparation().Prepared;
                string sourceRoot = At("preparation-scratch");
                foreach (ReleaseFile file in release.Files)
                {
                    string source = At("preparation-scratch/" + file.Path);
                    string target = At("prepared/" + file.Path);
                    // Only the original files selected by the typed preparation cross the job
                    // boundary.
                    if (!File.Exists(source) || !OwnedPath.Within(source, sourceRoot))
                        throw new InvalidDataException("Missing original transfer.");
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(source, target, overwrite: false);
                }
                break;
            // The one check of a downloaded preparation in each later job. Actions binds the
            // immutable same-run artifact; this binds its subject and qualified content to the
            // request before the job's own phase output directory exists.
            case ["receive",
                var phase] when phase is "observation" or "publication" or "verification":
                IReleasePreparation transferred = Preparation();
                transferred.Validate();
                if (transferred is BrowserStorePreparationResult browser && browser.Target != store)
                    throw new InvalidDataException("Transferred store target mismatch.");
                Fresh(phase);
                break;
            case ["state"]:
                string observationPath = At("observation/observation.json");
                bool needed;
                if (store is not null)
                    needed = BrowserStoreRelease.WriteNeeded(
                        (BrowserStorePreparationResult)Preparation(),
                        Read(observationPath, TransferJson.Default.StoreObservation));
                else
                {
                    var read = (buddy ? route.BuddyObservation : null) ?? route.Observation ??
                        throw new InvalidDataException("Unsupported Release observation.");
                    ReleaseObservation observation = read(observationPath).Observation;
                    Same(observation.Subject);
                    needed = observation.Files.Any(file => file.State == ReleaseFileState.Absent);
                }
                File.AppendAllText(Env("GITHUB_OUTPUT"), "write-needed=" + (
                    needed ? "true" : "false") + "\n");
                break;
            case ["source-config", var directory] when ecosystem == "ruby":
                Write(At("source-tools.json"), new RubySourceTools(Env("RUBY_EXECUTABLE"),
                    Path.Combine(control, "source.rb"), Fresh(directory)),
                        TransferJson.Default.RubySourceTools);
                break;
            case ["publisher-config", var directory] when ecosystem == "ruby":
                Write(At("publisher-tools.json"), new RubyPublisherTools(Env("RUBY_EXECUTABLE"),
                    Env("GEM_EXECUTABLE"), Fresh(directory)),
                        TransferJson.Default.RubyPublisherTools);
                break;
            case ["tool-request"] when ecosystem == "python":
                Write(At("index-tool-request.json"), new PythonIndexToolRequest(Env(
                    "UV_EXECUTABLE"),
                    Env("PYTHON_EXECUTABLE"), Path.Combine(control, "index-requirements.txt"),
                    Fresh("index-tool-scratch")), TransferJson.Default.PythonIndexToolRequest);
                break;
            case ["index-config", var directory] when ecosystem == "python":
                Write(At("index-tools.json"), new PythonIndexTools(Read(At(
                    "index-tool-result.json"),
                    TransferJson.Default.PythonIndexToolResult).Interpreter, Path.Combine(
                        control, "packages.py"),
                    Fresh(directory)), TransferJson.Default.PythonIndexTools);
                break;
            case ["publisher-config"] when ecosystem == "python":
                Write(At("publisher-tools.json"), new PythonPublisherTools(Env("UV_EXECUTABLE"),
                    Fresh("publisher-scratch")), TransferJson.Default.PythonPublisherTools);
                break;
            default:
                throw new InvalidDataException("Unsupported ordinary Release caller operation.");
        }
        return 0;
    }

    private static T Read<T>(string path, JsonTypeInfo<T> type) =>
        ReleaseCli.Read(path, type);
    private static Func<string, T> Reader<T>(JsonTypeInfo<T> type) =>
        path => Read(path, type);
    private static void Write<T>(string path, T value, JsonTypeInfo<T> type) =>
        File.WriteAllText(path, JsonSerializer.Serialize(value, type) + "\n");
}
