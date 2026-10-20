using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using WorkflowDelivery.Destinations;
using WorkflowDelivery.Ecosystems;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Release;

// Inputs for the existing concrete Actions callers. Actions owns job orchestration.
internal static class ReleaseJobInputs
{
    internal static int? Run(string[] args, Func<string, string?>? environment = null)
    {
        if (args is not ["release", "job-inputs", var ecosystem, .. var operation]) return null;
        if (ecosystem is not ("ruby" or "python" or "npm" or "nuget"))
            throw new InvalidDataException("Unsupported Release caller ecosystem.");
        environment ??= Environment.GetEnvironmentVariable;
        string Env(string name) => environment(name) is { Length: > 0 } value ? value :
            throw new InvalidDataException("Missing caller input: " + name);
        string root = Path.GetFullPath(Env("RUNNER_TEMP"));
        if (!Path.IsPathFullyQualified(Env("RUNNER_TEMP")) || !Directory.Exists(root))
            throw new InvalidDataException("Release caller requires native runner scratch.");
        string destination = Env("DESTINATION");
        bool buddy = (ecosystem, destination) switch
        {
            ("ruby", "github-packages") or ("python", "testpypi") or
                ("npm", "github-packages") or ("nuget", "github-packages-nuget") => true,
            ("ruby", "rubygems") or ("python", "pypi") or ("npm", "npmjs") => false,
            _ => throw new InvalidDataException("Unsupported Release caller destination."),
        };
        string dry = Env("DRY_RUN");
        if (dry is not ("true" or "false")) throw new InvalidDataException(
            "Invalid dry-run input.");
        string unit = ecosystem is "ruby" or "python" or "npm" ? Env("UNIT")
            : "hcoona-release-smoke-github-packages";
        var subject = new ReleaseSubject(Env("GITHUB_SHA"), Env("GITHUB_REF"), unit,
            ecosystem switch
            {
                "ruby" => RubyReleaseUnits.Named(unit).Declaration,
                "python" => PythonReleaseUnits.Named(unit).Declaration,
                "npm" => NpmReleaseUnits.Named(unit).Declaration,
                _ => "tests/private/app/workflow-delivery/fixtures/products/" + unit +
                    "/workflow-delivery.release-unit.yml",
            }, buddy ? "buddy" : "official",
            (ecosystem, buddy) switch
            {
                ("ruby", true) => "github-packages-ruby",
                ("npm", true) => "github-packages-npm",
                _ => destination,
            }, dry == "true");
        // Every Ruby, Python and npm job admits the selected registered unit before
        // preparation; npm also rejects its smoke unit for live Official.
        if (ecosystem == "ruby") _ = RubyReleaseUnits.Admit(subject);
        if (ecosystem == "python") _ = PythonReleaseUnits.Admit(subject);
        if (ecosystem == "npm") _ = NpmReleaseUnits.Admit(subject);
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
        PreparedRelease Preparation() => ecosystem switch
        {
            "ruby" => Read(Path.Combine(prepared, "preparation.json"),
                TransferJson.Default.RubyPreparationResult).Prepared,
            "python" => Read(Path.Combine(prepared, "preparation.json"),
                TransferJson.Default.PythonPreparationResult).Prepared,
            "npm" => Read(Path.Combine(prepared, "preparation.json"),
                TransferJson.Default.NpmPreparationResult).Prepared,
            "nuget" => Read(Path.Combine(prepared, "preparation.json"),
                TransferJson.Default.NuGetPreparationResult).Prepared,
            _ => throw new InvalidDataException("Unsupported Release preparation."),
        };
        void Receive()
        {
            string path = Path.Combine(prepared, "preparation.json");
            Same(Preparation().Subject);
            switch (ecosystem)
            {
                case "ruby":
                    RubyPreparationResult ruby = Read(path,
                        TransferJson.Default.RubyPreparationResult);
                    RubyRelease.Validate(ruby);
                    break;
                case "python":
                    PythonPreparationResult python = Read(path,
                        TransferJson.Default.PythonPreparationResult);
                    PythonRelease.Validate(python);
                    break;
                case "nuget":
                    NuGetPreparationResult nuget = Read(path,
                        TransferJson.Default.NuGetPreparationResult);
                    NuGetRelease.Validate(nuget);
                    break;
                default:
                    PreparedRelease release = Read(path,
                        TransferJson.Default.NpmPreparationResult).Prepared;
                    ReleaseCompletion.Validate(release);
                    ReleasePolicy.RequireQualified(release);
                    break;
            }
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
                if (ecosystem == "ruby")
                    Write(Path.Combine(prepared, "request.json"), new RubyPreparationRequest(
                        subject,
                        Env("GITHUB_WORKSPACE"), scratch, new(Env("RUBY_EXECUTABLE"),
                            Env("GEM_EXECUTABLE"), Env("DOTNET_EXECUTABLE"),
                            Env("MISE_EXECUTABLE")),
                        Env("MISE_DATA_DIR")),
                        TransferJson.Default.RubyPreparationRequest);
                else if (ecosystem == "python")
                    Write(Path.Combine(prepared, "request.json"), new PythonPreparationRequest(
                        subject,
                        Env("GITHUB_WORKSPACE"), scratch, Env("MISE_DATA_DIR")),
                        TransferJson.Default.PythonPreparationRequest);
                else if (ecosystem == "npm")
                    Write(Path.Combine(prepared, "request.json"), new NpmPreparationRequest(
                        subject, Env("GITHUB_WORKSPACE"), scratch),
                        TransferJson.Default.NpmPreparationRequest);
                else
                    Write(Path.Combine(prepared, "request.json"), new NuGetPreparationRequest(
                        subject, Env("GITHUB_WORKSPACE"), scratch),
                        TransferJson.Default.NuGetPreparationRequest);
                break;
            case ["originals"]:
                PreparedRelease release = Preparation();
                Same(release.Subject);
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
                Receive();
                Fresh(phase);
                break;
            case ["state"]:
                ReleaseObservation observation = ecosystem switch
                {
                    "ruby" => Read(At("observation/observation.json"),
                        TransferJson.Default.RubyObservationResult).Observation,
                    "python" => Read(At("observation/observation.json"),
                        TransferJson.Default.PythonIndexObservationResult).Observation,
                    "npm" when buddy => Read(At("observation/observation.json"),
                        TransferJson.Default.GithubNpmObservationResult).Observation,
                    "npm" => Read(At("observation/observation.json"),
                        TransferJson.Default.NpmjsObservationResult).Observation,
                    "nuget" => Read(At("observation/observation.json"),
                        TransferJson.Default.GithubNuGetObservationResult).Observation,
                    _ => throw new InvalidDataException("Unsupported Release observation."),
                };
                Same(observation.Subject);
                bool needed = observation.Files.Any(file => file.State == ReleaseFileState.Absent);
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
    private static void Write<T>(string path, T value, JsonTypeInfo<T> type) =>
        File.WriteAllText(path, JsonSerializer.Serialize(value, type) + "\n");
}
