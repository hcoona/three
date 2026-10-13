using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using WorkflowDelivery.Destinations;
using WorkflowDelivery.Ecosystems;

namespace WorkflowDelivery.Release;

internal sealed record ReleaseJobProducer(ReleaseSubject Subject, string Run, string Attempt);

// Inputs for the two existing concrete Actions callers. Actions owns job orchestration.
internal static class ReleaseJobInputs
{
    internal static int? Run(string[] args, Func<string, string?>? environment = null)
    {
        if (args is not ["release", "job-inputs", var ecosystem, .. var operation]) return null;
        if (ecosystem is not ("ruby" or "python"))
            throw new InvalidDataException("Unsupported Release caller ecosystem.");
        environment ??= Environment.GetEnvironmentVariable;
        string Env(string name) => environment(name) is { Length: > 0 } value ? value :
            throw new InvalidDataException("Missing caller input: " + name);
        string root = Path.GetFullPath(Env("RUNNER_TEMP"));
        if (!Path.IsPathFullyQualified(Env("RUNNER_TEMP")) || !Directory.Exists(root))
            throw new InvalidDataException("Release caller requires native runner scratch.");
        string destination = Env("DESTINATION");
        bool buddy = ecosystem == "ruby" ? destination == "github-packages" :
            destination == "testpypi";
        if (ecosystem == "ruby" ? destination is not ("github-packages" or "rubygems") :
            destination is not ("testpypi" or "pypi"))
            throw new InvalidDataException("Unsupported Release caller destination.");
        string dry = Env("DRY_RUN");
        if (dry is not ("true" or "false")) throw new InvalidDataException(
            "Invalid dry-run input.");
        string unit = "hcoona-release-smoke-" + ecosystem;
        var subject = new ReleaseSubject(Env("GITHUB_SHA"), Env("GITHUB_REF"), unit,
            "tests/private/app/workflow-delivery/fixtures/products/" + unit +
            "/workflow-delivery.release-unit.yml", buddy ? "buddy" : "official",
            ecosystem == "ruby" && buddy ? "github-packages-ruby" : destination, dry == "true");
        var producer = new ReleaseJobProducer(subject, Env("GITHUB_RUN_ID"), Env(
            "GITHUB_RUN_ATTEMPT"));
        string control = Path.Combine(root, "control"), prepared = Path.Combine(root, "prepared");
        string At(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Contains('\\') || name.Contains(':') ||
                name.Split('/').Any(part => part is "" or "." or ".."))
                throw new InvalidDataException("Expected a relative caller path.");
            string path = Path.Combine(root, name);
            RequireUnlinked(path, root);
            return path;
        }
        string Fresh(string name)
        {
            string path = At(name);
            if (File.Exists(path) || Directory.Exists(path))
                throw new InvalidDataException("Caller scratch already exists.");
            Directory.CreateDirectory(path);
            return path;
        }
        PreparedRelease Preparation() => ecosystem == "ruby"
            ? Read(Path.Combine(prepared, "preparation.json"),
                TransferJson.Default.RubyPreparationResult).Prepared
            : Read(Path.Combine(prepared, "preparation.json"),
                TransferJson.Default.PythonPreparationResult).Prepared;
        void Same(ReleaseSubject actual)
        {
            if (actual != subject) throw new InvalidDataException(
                "Transferred Release subject mismatch.");
        }
        switch (operation)
        {
            case ["request"]:
                if (Env("GITHUB_REPOSITORY") != "hcoona/three" ||
                    !subject.DryRun && producer.Attempt != "1")
                    throw new InvalidDataException("Unsupported repository or live rerun.");
                string scratch = Fresh("preparation-scratch");
                Fresh("prepared");
                Write(Path.Combine(control, "producer.json"), producer,
                    TransferJson.Default.ReleaseJobProducer);
                Write(Path.Combine(prepared, "producer.json"), producer,
                    TransferJson.Default.ReleaseJobProducer);
                if (ecosystem == "ruby")
                    Write(Path.Combine(prepared, "request.json"), new RubyPreparationRequest(
                        subject,
                        Env("GITHUB_WORKSPACE"), scratch, new(Env("RUBY_EXECUTABLE"),
                            Env("GEM_EXECUTABLE"), Env("DOTNET_EXECUTABLE"))),
                        TransferJson.Default.RubyPreparationRequest);
                else
                    Write(Path.Combine(prepared, "request.json"), new PythonPreparationRequest(
                        subject,
                        Env("GITHUB_WORKSPACE"), scratch, Env("MISE_DATA_DIR")),
                        TransferJson.Default.PythonPreparationRequest);
                break;
            case ["originals"]:
                PreparedRelease release = Preparation();
                Same(release.Subject);
                ReleaseCompletion.Validate(release);
                string sourceRoot = At("preparation-scratch");
                foreach (ReleaseFile file in release.Files)
                {
                    string source = At("preparation-scratch/" + file.Path);
                    string target = At("prepared/" + file.Path);
                    // Only the original files selected by the typed preparation cross the job
                    // boundary.
                    if (!File.Exists(source) || !source.StartsWith(sourceRoot +
                        Path.DirectorySeparatorChar,
                        StringComparison.Ordinal)) throw new InvalidDataException(
                            "Missing original transfer.");
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(source, target, overwrite: false);
                }
                break;
            case ["validate", .. var kinds] when kinds.Length > 0:
                foreach (string kind in kinds)
                    if (Read(At(kind + "/producer.json"),
                        TransferJson.Default.ReleaseJobProducer) != producer)
                        throw new InvalidDataException("Transferred producer mismatch.");
                Same(Preparation().Subject);
                break;
            case ["phase",
                var phase] when phase is "observation" or "publication" or "verification":
                string phaseRoot = Fresh(phase);
                Write(Path.Combine(phaseRoot, "producer.json"), producer,
                    TransferJson.Default.ReleaseJobProducer);
                break;
            case ["state"]:
                ReleaseObservation observation = ecosystem == "ruby"
                    ? Read(At("observation/observation.json"),
                        TransferJson.Default.RubyObservationResult).Observation
                    : Read(At("observation/observation.json"),
                        TransferJson.Default.PythonIndexObservationResult).Observation;
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
            case ["consumer-config", var directory] when ecosystem == "ruby":
                Write(At("consumer-tools.json"), new RubyRemoteConsumerTools(Env("RUBY_EXECUTABLE"),
                    Path.Combine(control, "facts.rb"), Path.Combine(control, "source.rb"),
                    Path.Combine(control, "consumer.rb"), Fresh(directory)),
                        TransferJson.Default.RubyRemoteConsumerTools);
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
            case ["consumer-config", var kind] when ecosystem == "python" &&
                kind is "python-wheel" or "python-sdist":
                var selectedEnvironment = new Dictionary<string, string?>();
                foreach (string name in new[] { "PATH", "DOTNET_ROOT" })
                    if (environment(name) is { } value) selectedEnvironment.Add(name, value);
                Write(At(kind + "-tools.json"), new PythonPackageTools(Env("GITHUB_WORKSPACE"),
                    Fresh(kind + "-scratch"), Env("UV_EXECUTABLE"), Env("PYTHON_EXECUTABLE"),
                    Path.Combine(control, "packages.py"), 180, selectedEnvironment, false),
                    TransferJson.Default.PythonPackageTools);
                break;
            default:
                throw new InvalidDataException("Unsupported ordinary Release caller operation.");
        }
        return 0;
    }

    private static T Read<T>(string path, JsonTypeInfo<T> type) =>
        JsonSerializer.Deserialize(File.ReadAllText(path), type) ??
        throw new InvalidDataException("Missing typed caller input.");
    private static void Write<T>(string path, T value, JsonTypeInfo<T> type) =>
        File.WriteAllText(path, JsonSerializer.Serialize(value, type) + "\n");
    private static void RequireUnlinked(string path, string root)
    {
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Linked caller transfers are unsupported.");
            if (current == root) break;
        }
    }
}
