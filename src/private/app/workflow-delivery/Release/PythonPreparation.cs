using System.Text.Json;
using WorkflowDelivery.Ecosystems;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Release;

internal sealed record PythonPreparationRequest(ReleaseSubject Subject, string Checkout,
    string Scratch, string MiseDataDirectory);
internal sealed record PythonPreparationResult(PreparedRelease Prepared,
    Dictionary<string, string> Native, ReleaseCommand[] Commands, string[] Errors);

// Release qualifies its complete declaration independently of CI impact selection.
internal static class PythonPreparation
{
    internal static readonly string[] Required = ["native-input", "build", "contents",
        "wheel-consumer", "sdist-consumer", "original-bytes"];
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
        string checkout = Absolute(request.Checkout), scratch = Absolute(request.Scratch);
        string helper = Path.Combine(checkout,
            "src/private/app/workflow-delivery/Native/Python/packages.py");
        // Validate before creating any owned execution state.
        PythonSmokePackageOperations.Validate(new(checkout, scratch, "uv", helper, helper, 300,
            new Dictionary<string, string?>(), false));
        var commands = new List<ReleaseCommand>();
        var checks = new List<ReleaseCheck>();
        var errors = new List<string>();
        var files = new List<ReleaseFile>();
        var native = new Dictionary<string, string>(StringComparer.Ordinal);
        Dictionary<string, string?> environment = EnvironmentFor(scratch, checkout,
            request.Subject);
        // The caller supplies the platform's existing tool installation resource.
        environment["MISE_DATA_DIR"] = Absolute(request.MiseDataDirectory);
        PythonSmokePackageOperations? operations = null;
        ReleaseBuild? build = null;
        PythonDistribution[] originals = [];
        string name = "", version = "", current = "native-input";

        await CheckAsync("native-input", async () =>
        {
            ReleaseSubject subject = request.Subject;
            bool buddy = subject.Channel == "buddy" && subject.Destination == "testpypi";
            bool official = subject.Channel == "official" && subject.Destination == "pypi";
            if (!(buddy || official) || string.IsNullOrWhiteSpace(subject.SelectedRef))
                throw new InvalidDataException("Unsupported Python Release channel or ref.");
            ReleasePolicy.RequireEligible(subject);
            GitRevision revision = await new GitReader(checkout).ReadAsync(subject.Revision, token);
            if (revision.Commit != subject.Revision)
                throw new InvalidDataException(
                    "Python Release requires the exact selected revision.");
            GitMaterialization bound = await GitMaterialization.BindAsync(checkout, revision,
                token);
            ReleaseUnitDeclaration unit = ReleaseUnitDeclarationReader.Read(
                await bound.ReadOptionalTextAsync(subject.Declaration, token) ??
                    throw new InvalidDataException("Missing committed Python release declaration."),
                subject.Declaration);
            if (unit.Id != subject.Unit || unit.Builds is not
                [{ Definition: "python/distribution-set-v1", Outputs: { Length: 2 } }])
                throw new InvalidDataException("Unsupported complete Python Release recipe.");
            build = unit.Builds[0];
            if (build.Outputs.Count(output => output is
                    { Role: "primary-package", Kind: "python-wheel" }) != 1 ||
                build.Outputs.Count(output => output is
                    { Role: "source-package", Kind: "python-sdist" }) != 1)
                throw new InvalidDataException("Unsupported complete Python distribution set.");
            string parent = Path.GetDirectoryName(subject.Declaration)!.Replace('\\', '/');
            string entry = parent.Length == 0 ? build.EntryPoint : parent + "/" + build.EntryPoint;
            string directory = Path.GetDirectoryName(entry)!.Replace('\\', '/');
            if (Path.GetFileName(entry) != "pyproject.toml" ||
                await bound.ReadOptionalTextAsync(entry, token) is null)
                throw new InvalidDataException("Missing committed native Python entry.");
            QualitySelection? quality = await new QualitySelectionReader(checkout,
                bound.ReadOptionalTextAsync).ReadAsync(directory.Length == 0 ? "." : directory,
                    "python", token);
            if (quality?.Preset != PythonSmokePackageOperations.Preset)
                throw new InvalidDataException("Unsupported complete Python quality selection.");
            foreach (string input in Inputs)
                if (await bound.ReadOptionalTextAsync(input, token) is null)
                    throw new InvalidDataException("Missing committed native input: " + input);
            if ((await RunAsync(new("git", checkout,
                ["rev-parse", "--is-shallow-repository"], 30))).Stdout.Trim() != "false")
                throw new InvalidDataException("Native versioning requires complete Git history.");
            string uvVersion = await ToolAsync("uv", null);
            string pythonVersion = await ToolAsync("python", "3.14");
            string interpreter = Absolute((await RunAsync(new("mise", checkout,
                ["which", "python"], 30))).Stdout.Trim());
            string uv = Absolute((await RunAsync(new("mise", checkout,
                ["which", "uv"], 30))).Stdout.Trim());
            if ((await RunAsync(new(uv, checkout, ["self", "version", "--short"], 30))).Stdout
                .Trim() != uvVersion)
                throw new InvalidDataException("Actual UV differs from committed tool selection.");
            await RunAsync(new("dotnet", checkout, ["tool", "restore"], 300));
            operations = new(new(checkout, scratch, uv, interpreter, helper, 300, environment,
                false), Path.Combine(scratch, "package"));
            (name, version) = await operations.PrepareAsync(directory, ExecuteAsync, token);
            using JsonDocument facts = await operations.VersionFactsAsync(directory,
                ExecuteAsync, token);
            JsonElement value = facts.RootElement;
            bool publicRelease = value.GetProperty("public_release").GetBoolean();
            if (name != "hcoona-release-smoke-python" ||
                value.GetProperty("name").GetString() != name ||
                value.GetProperty("version").GetString() != version ||
                value.GetProperty("commit").GetString() != subject.Revision ||
                value.GetProperty("implementation").GetString() != "cpython" ||
                value.GetProperty("interpreter_version").GetString() != pythonVersion ||
                value.GetProperty("dependencies").GetArrayLength() != 0 ||
                !subject.DryRun && (value.GetProperty("local_version").ValueKind !=
                    JsonValueKind.Null || official && !publicRelease))
                throw new InvalidDataException("Unsupported native Python Release identity.");
            native.Add("name", name);
            native.Add("version", version);
            native.Add("commit", subject.Revision);
            native.Add("public-release", publicRelease ? "true" : "false");
            native.Add("python", pythonVersion);
            native.Add("uv", uvVersion);
        });
        await CheckAsync("build", async () => originals = await operations!.BuildAsync(name,
            version, ExecuteAsync, token));
        await CheckAsync("contents", () => operations!.ContentsAsync(originals, name, version,
            ExecuteAsync, token));
        await CheckAsync("wheel-consumer", () => operations!.ConsumeAsync("python-wheel",
            originals, name, version, ExecuteAsync, token));
        await CheckAsync("sdist-consumer", () => operations!.ConsumeAsync("python-sdist",
            originals, name, version, ExecuteAsync, token));
        await CheckAsync("original-bytes", () =>
        {
            foreach (PythonDistribution original in originals)
                PythonSmokePackageOperations.RequireOriginal(original);
            foreach (PythonDistribution original in originals)
            {
                ReleaseOutput output = build!.Outputs.Single(value => value.Kind == original.Kind);
                files.Add(new(build.Id, output.Id, original.Kind, name, version,
                    Path.GetRelativePath(scratch, original.Path).Replace('\\', '/'),
                    original.Length, original.Sha256));
            }
            return Task.CompletedTask;
        });
        return new(new(request.Subject, [.. files], Required.ToArray(), [.. checks]), native,
            [.. commands], [.. errors]);

        Task<NativeCommandResult> ExecuteAsync(NativeCommand command,
            CancellationToken cancellation)
            => RunAsync(command, cancellation);

        async Task<NativeCommandResult> RunAsync(NativeCommand command,
            CancellationToken? cancellation = null)
        {
            var effective = new Dictionary<string, string?>(environment, StringComparer.Ordinal);
            if (command.Environment is not null)
                foreach (var pair in command.Environment) effective[pair.Key] = pair.Value;
            command = command with { Environment = effective, InheritEnvironment = false };
            NativeCommandResult result = await execute(command, cancellation ?? token);
            commands.Add(new(current, command, result));
            if (result.Termination == NativeTermination.Cancelled)
                throw new OperationCanceledException(token);
            if (result.Termination == NativeTermination.TimedOut)
                throw new TimeoutException("Native " + current + " timed out.");
            if (!result.Succeeded || result.Error is not null)
                throw new InvalidDataException("Native " + current + " failed: " +
                    result.Termination + ".");
            (cancellation ?? token).ThrowIfCancellationRequested();
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
                UnauthorizedAccessException or
                JsonException or KeyNotFoundException or InvalidOperationException or
                ArgumentException or FormatException)
            { checks.Add(new(id, ReleaseCheckStatus.Failed)); errors.Add(failure.Message); }
        }

        async Task<string> ToolAsync(string tool, string? requested)
        {
            using JsonDocument json = JsonDocument.Parse((await RunAsync(new("mise", checkout,
                ["ls", "--json", "--current", "--locked", tool], 30))).Stdout);
            if (json.RootElement.ValueKind != JsonValueKind.Array ||
                json.RootElement.GetArrayLength() != 1)
                throw new InvalidDataException("Unavailable selected native tool: " + tool);
            JsonElement value = json.RootElement[0];
            string selected = value.GetProperty("version").GetString()!;
            if (!Version.TryParse(selected, out Version? parsed) || parsed.Build < 0 ||
                tool == "python" && (parsed.Major != 3 || parsed.Minor != 14) ||
                value.GetProperty("requested_version").GetString() != (requested ?? selected) ||
                value.GetProperty("active").ValueKind != JsonValueKind.True ||
                value.GetProperty("source").GetProperty("type").GetString() != "mise.toml" ||
                value.GetProperty("source").GetProperty("path").GetString() !=
                    Path.Combine(checkout, "mise.toml"))
                throw new InvalidDataException("Native tool differs from committed authority.");
            return selected;
        }
    }

    private static Dictionary<string, string?> EnvironmentFor(string scratch, string checkout,
        ReleaseSubject subject)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (string name in new[] { "PATH", "SystemRoot", "WINDIR", "PATHEXT", "DOTNET_ROOT",
            "DOTNET_ROOT_X64", "MISE_DATA_DIR" })
            if (Environment.GetEnvironmentVariable(name) is { } value) values.Add(name, value);
        string home = Directory.CreateDirectory(Path.Combine(scratch, "home")).FullName;
        foreach (string name in new[] { "HOME", "USERPROFILE", "DOTNET_CLI_HOME" }) values[name] =
            home;
        foreach ((string name, string path) in new[] { ("XDG_CONFIG_HOME", "config"),
            ("XDG_CACHE_HOME", "cache"), ("XDG_DATA_HOME", "data"),
            ("MISE_CACHE_DIR", "mise-cache"),
            ("APPDATA", "appdata"), ("LOCALAPPDATA", "localappdata"), ("TMPDIR", "tmp"),
            ("TEMP", "tmp"), ("TMP", "tmp") })
            values[name] = Directory.CreateDirectory(Path.Combine(home, path)).FullName;
        values["MISE_TRUSTED_CONFIG_PATHS"] = checkout;
        values["MISE_AUTO_INSTALL"] = "0";
        values["MISE_EXEC_AUTO_INSTALL"] = "0";
        values["UV_PYTHON_DOWNLOADS"] = "never";
        values["UV_NO_PROGRESS"] = "1";
        values["PYTHONDONTWRITEBYTECODE"] = "1";
        values["GITHUB_ACTIONS"] = "true";
        values["GITHUB_REF"] = subject.SelectedRef;
        values["GITHUB_SHA"] = subject.Revision;
        string output = Path.Combine(scratch, "nbgv-environment");
        File.WriteAllText(output, "");
        values["GITHUB_ENV"] = output;
        return values;
    }

    private static string Absolute(string path) =>
        OwnedPath.Absolute(path, "Python Release paths must be absolute.");
}
