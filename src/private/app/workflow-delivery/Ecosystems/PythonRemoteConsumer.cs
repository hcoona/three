using System.Text.Json;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Release;

namespace WorkflowDelivery.Ecosystems;

internal sealed record PythonRemoteCommand(string Stage, NativeCommand Command,
    NativeCommandResult Result);
internal sealed record PythonRemoteConsumerResult(ReleaseSubject Subject, ReleaseFile Original,
    string Index, ReleaseCheck Check, PythonRemoteCommand[] Commands, string[] Errors);

// The caller owns a separate no-write job. UV resolves, validates the original
// archive hash, builds the selected format and installs it; no local-file fallback.
internal static class PythonRemoteConsumer
{
    private const string Package = "hcoona-release-smoke-python";
    private static readonly string[] ToolVariables = ["PATH", "SystemRoot", "DOTNET_ROOT",
        "DOTNET_ROOT_X64"];
    private static readonly string[] StateVariables = ["HOME", "USERPROFILE", "XDG_CONFIG_HOME",
        "XDG_DATA_HOME", "XDG_CACHE_HOME", "TMPDIR", "TMP", "TEMP", "DOTNET_CLI_HOME"];

    internal static Task<PythonRemoteConsumerResult> RunAsync(PreparedRelease prepared,
        string kind, PythonPackageTools tools, CancellationToken token) =>
        RunAsync(prepared, kind, tools, NativeProcess.ExecuteAsync, token);

    internal static async Task<PythonRemoteConsumerResult> RunAsync(PreparedRelease prepared,
        string kind, PythonPackageTools tools,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        CancellationToken token)
    {
        ReleaseCompletion.Validate(prepared);
        PythonSmokePackageOperations.Validate(tools);
        if (kind is not ("python-wheel" or "python-sdist") ||
            prepared.Subject.Unit != Package || prepared.Files.Length != 2 ||
            prepared.Files.Count(file => file.Kind == "python-wheel") != 1 ||
            prepared.Files.Count(file => file.Kind == "python-sdist") != 1 ||
            prepared.Files.Any(file => file.Name != Package || file.Size > 8 * 1024 * 1024 ||
                file.Version != prepared.Files[0].Version || file.Version.Length > 256 ||
                file.Version.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not
                    ('.' or '-' or '+' or '!'))) ||
            prepared.RequiredChecks.Any(id => !prepared.Checks.Any(check => check.Id == id &&
                check.Status == ReleaseCheckStatus.Passed)) ||
            !Path.IsPathFullyQualified(tools.Uv))
            throw new InvalidDataException("Unsupported or unqualified remote Python consumer.");
        string index = (prepared.Subject.Channel, prepared.Subject.Destination) switch
        {
            ("buddy", "testpypi") => "https://test.pypi.org/simple/",
            ("official", "pypi") => "https://pypi.org/simple/",
            _ => throw new InvalidDataException("Unsupported remote Python index."),
        };
        ReleaseFile original = prepared.Files.Single(file => file.Kind == kind);
        var commands = new List<PythonRemoteCommand>();
        var errors = new List<string>();
        ReleaseCheckStatus status = ReleaseCheckStatus.Passed;
        using var phase = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, phase.Token);
        try
        {
            lifetime.Token.ThrowIfCancellationRequested();
            var environment = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (string name in ToolVariables)
                if (tools.Environment.TryGetValue(name, out string? value))
                    environment[name] = value;
            foreach (string name in StateVariables)
                environment[name] = Directory.CreateDirectory(Path.Combine(tools.Scratch,
                    name.ToLowerInvariant())).FullName;
            environment["UV_PYTHON_DOWNLOADS"] = "never";
            environment["UV_CACHE_DIR"] = Path.Combine(tools.Scratch, "empty-cache");
            string consumer = Directory.CreateDirectory(Path.Combine(tools.Scratch,
                "consumer")).FullName;
            string python;
            if (kind == "python-sdist")
            {
                var shared = new PythonSmokePackageOperations(tools with
                {
                    Environment = environment, InheritEnvironment = false,
                    DeadlineSeconds = Math.Min(tools.DeadlineSeconds, 180),
                }, Path.Combine(tools.Scratch, "backend"));
                await shared.PrepareToolsAsync((command, _) => RunAsync("tools", command),
                    lifetime.Token);
                await shared.PrepareNoneditableToolsAsync((command, _) =>
                    RunAsync("noneditable-tools", command), lifetime.Token);
                python = shared.ToolInterpreter;
            }
            else
            {
                string venv = Path.Combine(consumer, "venv");
                await RunAsync("venv", Uv(["venv", venv, "--python", tools.Interpreter,
                    "--no-project"]));
                python = Path.Combine(venv, OperatingSystem.IsWindows()
                    ? "Scripts/python.exe" : "bin/python");
            }
            NativeCommandResult absent = await RunAsync("absent", Helper("consumer-absent",
                Package, PythonSmokePackageOperations.Preset));
            using (JsonDocument facts = JsonDocument.Parse(absent.Stdout))
                if (facts.RootElement.GetProperty("name").GetString() != Package ||
                    !facts.RootElement.GetProperty("absent").GetBoolean())
                    throw new InvalidDataException("Native product absence was not established.");
            string requirement = Path.Combine(consumer, "requirements.txt");
            await File.WriteAllTextAsync(requirement,
                $"{Package}=={original.Version} --hash=sha256:{original.Sha256}\n", lifetime.Token);
            string[] format = kind == "python-wheel" ? ["--only-binary", ":all:"] :
                ["--no-binary", ":all:", "--no-build-isolation", "--preview-features",
                    "build-dependency-check"];
            await RunAsync("install", Uv(["pip", "install", "--python", python, "--no-deps",
                "--no-cache", "--require-hashes", "--index-url", index,
                "--keyring-provider", "disabled", .. format, "--requirements", requirement]));
            NativeCommandResult installed = await RunAsync("consume", Helper("consume", Package,
                original.Version, PythonSmokePackageOperations.Preset));
            using (JsonDocument facts = JsonDocument.Parse(installed.Stdout))
                if (facts.RootElement.GetProperty("name").GetString() != Package ||
                    facts.RootElement.GetProperty("version").GetString() != original.Version ||
                    string.IsNullOrWhiteSpace(facts.RootElement.GetProperty("origin").GetString()))
                    throw new InvalidDataException("Native installed consumption is incomplete.");
            lifetime.Token.ThrowIfCancellationRequested();

            NativeCommand Uv(string[] args) => new(tools.Uv, consumer,
                ["--no-config", .. args], Math.Min(tools.DeadlineSeconds, 180), environment, false);
            NativeCommand Helper(params string[] args) => new(python, consumer,
                ["-I", "-B", tools.Helper, .. args], 30, environment, false);
        }
        catch (ConsumerStopped stopped)
        {
            status = stopped.Status;
            errors.Add("Required native remote consumer command did not succeed.");
        }
        catch (OperationCanceledException)
        {
            status = token.IsCancellationRequested ? ReleaseCheckStatus.Cancelled
                : ReleaseCheckStatus.TimedOut;
            errors.Add("Remote consumer was cancelled or exceeded its phase deadline.");
        }
        catch (Exception error) when (error is IOException or InvalidDataException or
            JsonException or InvalidOperationException or KeyNotFoundException)
        {
            status = ReleaseCheckStatus.Failed;
            errors.Add("Required native remote consumer facts were unavailable.");
        }
        return new(prepared.Subject, original, index,
            new("remote-" + original.Output + "-consumer", status), [.. commands], [.. errors]);

        async Task<NativeCommandResult> RunAsync(string stage, NativeCommand command)
        {
            lifetime.Token.ThrowIfCancellationRequested();
            NativeCommandResult result = await execute(command, lifetime.Token);
            // Public reads have no credential capability; still omit arbitrary native
            // output rather than persisting URLs or treating logs as hash provenance.
            commands.Add(new(stage, command, new(result.Termination, result.ExitCode, "", "",
                result.DurationSeconds, result.Error is null ? null : "Native details omitted.")));
            lifetime.Token.ThrowIfCancellationRequested();
            if (!result.Succeeded)
                throw new ConsumerStopped(result.Termination switch
                {
                    NativeTermination.Cancelled => ReleaseCheckStatus.Cancelled,
                    NativeTermination.TimedOut => ReleaseCheckStatus.TimedOut,
                    _ => ReleaseCheckStatus.Failed,
                });
            return result;
        }
    }

    private sealed class ConsumerStopped(ReleaseCheckStatus status) : Exception
    {
        internal ReleaseCheckStatus Status { get; } = status;
    }
}
