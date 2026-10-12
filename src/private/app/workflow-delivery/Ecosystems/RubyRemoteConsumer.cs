using System.Text.Json;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Release;

namespace WorkflowDelivery.Ecosystems;

internal sealed record RubyRemoteConsumerTools(string Ruby, string FactsHelper,
    string SourceHelper, string ConsumerHelper, string Scratch);
internal sealed record RubyRemoteCommand(string Stage, NativeCommandResult Result);
internal sealed record RubyRemoteConsumerResult(ReleaseSubject Subject, ReleaseFile Original,
    string Source, RubySourceReadResult? Read, ReleaseCheck Check,
    RubyRemoteCommand[] Commands, string[] Errors);

// Native source reads finish before qualified remote bytes enter a credential-free installer.
internal static class RubyRemoteConsumer
{
    internal static Task<RubyRemoteConsumerResult> RunAsync(PreparedRelease prepared,
        RubyRemoteConsumerTools tools, RubyReadCredential? credential, CancellationToken token) =>
        RunAsync(prepared, tools, credential, NativeProcess.ExecuteAsync, token);

    internal static async Task<RubyRemoteConsumerResult> RunAsync(PreparedRelease prepared,
        RubyRemoteConsumerTools tools, RubyReadCredential? credential,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        CancellationToken token)
    {
        ReleaseCompletion.Validate(prepared);
        if (prepared.Subject.Unit != "hcoona-release-smoke-ruby" ||
            prepared.Files is not [{ Kind: "ruby-gem", Name: "hcoona-release-smoke-ruby" }] ||
            prepared.Files[0].Size > 8 * 1024 * 1024 ||
            !prepared.RequiredChecks.SequenceEqual(RubyPreparation.Required) ||
            prepared.RequiredChecks.Any(id => !prepared.Checks.Any(check => check.Id == id &&
                check.Status == ReleaseCheckStatus.Passed)) ||
            new[] { tools.Ruby, tools.FactsHelper, tools.SourceHelper, tools.ConsumerHelper,
                tools.Scratch }.Any(path => !Path.IsPathFullyQualified(path)) ||
            !Directory.Exists(tools.Scratch) ||
            Directory.EnumerateFileSystemEntries(tools.Scratch).Any())
            throw new InvalidDataException("Remote Ruby consumption needs qualified fresh state.");
        string host = (prepared.Subject.Channel, prepared.Subject.Destination) switch
        {
            ("buddy", "github-packages-ruby") => "https://rubygems.pkg.github.com/hcoona",
            ("official", "rubygems") => "https://rubygems.org",
            _ => throw new InvalidDataException("Unsupported remote Ruby source."),
        };
        ReleaseFile original = prepared.Files[0];
        var commands = new List<RubyRemoteCommand>();
        var errors = new List<string>();
        RubySourceReadResult? read = null;
        ReleaseCheckStatus status = ReleaseCheckStatus.Passed;
        using var phase = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, phase.Token);
        CancellationToken cancellation = lifetime.Token;
        try
        {
            cancellation.ThrowIfCancellationRequested();
            string source = Directory.CreateDirectory(Path.Combine(tools.Scratch, "source"))
                .FullName;
            read = await RubySourceReader.ReadAsync(host, original.Version,
                new(tools.Ruby, tools.SourceHelper, source), credential, execute, cancellation);
            cancellation.ThrowIfCancellationRequested();
            if (read.Command is { Termination: NativeTermination.Cancelled })
                throw new ConsumerStopped(ReleaseCheckStatus.Cancelled);
            if (read.Command is { Termination: NativeTermination.TimedOut })
                throw new ConsumerStopped(ReleaseCheckStatus.TimedOut);
            if (read.State != RubySourceState.Present || read.Original is null ||
                read.Original.Size != original.Size || read.Original.Sha256 != original.Sha256)
                throw new InvalidDataException(
                    "Selected source did not supply the exact original.");
            var remote = new RubyGemArchive(read.Original.Path, original.Size, original.Sha256);
            string consumer = Directory.CreateDirectory(Path.Combine(tools.Scratch, "consumer"))
                .FullName;
            var environment = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (string name in new[] { "SystemRoot", "WINDIR", "PATHEXT" })
                environment[name] = Environment.GetEnvironmentVariable(name);
            environment["PATH"] = string.Join(Path.PathSeparator,
                Path.GetDirectoryName(tools.Ruby), "/usr/bin", "/bin");
            foreach (string name in new[] { "HOME", "USERPROFILE", "XDG_CONFIG_HOME",
                "XDG_CACHE_HOME", "XDG_DATA_HOME", "APPDATA", "LOCALAPPDATA",
                "GEM_SPEC_CACHE", "TMPDIR", "TMP", "TEMP" })
                environment[name] = Directory.CreateDirectory(Path.Combine(consumer,
                    name.ToLowerInvariant())).FullName;
            string gems = Directory.CreateDirectory(Path.Combine(consumer, "gems")).FullName;
            environment["GEM_HOME"] = gems;
            environment["GEM_PATH"] = gems;
            environment["LANG"] = "C.UTF-8";
            environment["LC_ALL"] = "C.UTF-8";
            string queries = Directory.CreateDirectory(Path.Combine(consumer, "queries"))
                .FullName;
            var operations = new RubyPackageOperations(new(tools.Ruby, tools.FactsHelper,
                queries, environment, 90), "", "", consumer);
            await operations.InspectReleaseAsync(remote, original.Version,
                (command, _) => RunAsync("remote-original", command), cancellation);
            foreach (string operation in new[] { "install", "consume" })
            {
                cancellation.ThrowIfCancellationRequested();
                RubyPackageOperations.RequireOriginal(remote);
                string request = Path.Combine(consumer, operation + ".json");
                await File.WriteAllTextAsync(request, JsonSerializer.Serialize(new
                {
                    operation, original = remote.Path, version = original.Version,
                    sha256 = original.Sha256, install_dir = gems,
                }), cancellation);
                NativeCommandResult result = await RunAsync("remote-" + operation,
                    new(tools.Ruby, consumer, [tools.ConsumerHelper, request], 90, environment,
                        InheritEnvironment: false, CloseStandardInput: true));
                using JsonDocument facts = JsonDocument.Parse(result.Stdout);
                if (facts.RootElement.GetProperty("name").GetString() != original.Name ||
                    facts.RootElement.GetProperty("version").GetString() != original.Version ||
                    !facts.RootElement.GetProperty("passed").GetBoolean())
                    throw new InvalidDataException("Remote installed consumption was incomplete.");
            }
            RubyPackageOperations.RequireOriginal(remote);
            cancellation.ThrowIfCancellationRequested();
        }
        catch (ConsumerStopped stopped)
        { status = stopped.Status; errors.Add("Required remote Ruby command did not succeed."); }
        catch (OperationCanceledException)
        {
            status = token.IsCancellationRequested ? ReleaseCheckStatus.Cancelled :
                ReleaseCheckStatus.TimedOut;
            errors.Add("Remote Ruby consumption was cancelled or exceeded its phase deadline.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
            InvalidDataException or JsonException or KeyNotFoundException or
            InvalidOperationException or ArgumentException or FormatException)
        {
            status = ReleaseCheckStatus.Failed;
            errors.Add("Required remote Ruby original or consumer facts were unavailable.");
        }
        return new(prepared.Subject, original, host, read,
            new("remote-" + original.Output + "-consumer", status), [.. commands], [.. errors]);

        async Task<NativeCommandResult> RunAsync(string stage, NativeCommand command)
        {
            cancellation.ThrowIfCancellationRequested();
            command = command with { InheritEnvironment = false, CloseStandardInput = true };
            NativeCommandResult result = await execute(command, cancellation);
            commands.Add(new(stage, result with { Stdout = "", Stderr = "",
                Error = result.Error is null ? null : "Native Ruby consumer details omitted." }));
            cancellation.ThrowIfCancellationRequested();
            if (!result.Succeeded || result.Error is not null)
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
    { internal ReleaseCheckStatus Status { get; } = status; }
}
