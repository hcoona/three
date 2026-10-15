using System.Text.Json;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Repository;

internal sealed record RubyQueryTools(string Ruby, string Gem, string Bundle, string Dotnet);
internal sealed record RubyGroupPreparationRequest(MaterializedEndpoint Basis,
    MaterializedEndpoint Candidate, bool Full, string ScopePath, string Helper, string Scratch,
    RubyQueryTools Tools, string BundlerProbeVersion, int DeadlineSeconds);
internal sealed record RubyGroupPreparationResult(RubyGroupRequest Request,
    RubyPreparationCommand[] Commands);

// Native tools own versioning and installation; this finite caller owns disposable state.
internal static class RubyGroupPreparation
{
    internal static Task<RubyGroupPreparationResult> PrepareAsync(
        RubyGroupPreparationRequest request, CancellationToken token) =>
        PrepareAsync(request, NativeProcess.ExecuteAsync, token);

    internal static async Task<RubyGroupPreparationResult> PrepareAsync(
        RubyGroupPreparationRequest request,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string scratch = Absolute(request.Scratch);
        foreach (string path in new[] { request.Basis.Directory, request.Candidate.Directory,
                     request.ScopePath, request.Helper, request.Tools.Ruby, request.Tools.Gem,
                     request.Tools.Bundle, request.Tools.Dotnet }) _ = Absolute(path);
        if (request.DeadlineSeconds <= 0 ||
            string.IsNullOrWhiteSpace(request.BundlerProbeVersion) ||
            !Directory.Exists(scratch) || Directory.EnumerateFileSystemEntries(scratch).Any() ||
            OwnedPath.Overlap(scratch, request.Basis.Directory) ||
            OwnedPath.Overlap(scratch, request.Candidate.Directory) ||
            request.Full && request.Basis.Reference != request.Candidate.Reference)
            throw new InvalidDataException("Ruby preparation requires fresh external state.");
        GitComparison comparison = await new GitReader(request.Candidate.Directory).CompareAsync(
            request.Basis.Reference, request.Candidate.Reference, token);
        GitMaterialization before = await GitMaterialization.BindAsync(request.Basis.Directory,
            comparison.Basis, token);
        GitMaterialization after = await GitMaterialization.BindAsync(request.Candidate.Directory,
            comparison.Candidate, token);
        _ = ScopeInputs.Read(await File.ReadAllTextAsync(request.ScopePath, token), comparison,
            request.Full);
        string version = await PinAsync(after, token);
        var commands = new List<RubyPreparationCommand>();
        string native = Directory.CreateDirectory(Path.Combine(scratch, "native")).FullName;
        var nativeEnvironment = EnvironmentFor(native);
        await RunAsync(new(request.Tools.Dotnet, after.Root,
            ["tool", "install", "nbgv", "--version", version, "--tool-path", native],
            request.DeadlineSeconds, nativeEnvironment, InheritEnvironment: false));
        string nbgv = Path.Combine(native, OperatingSystem.IsWindows() ? "nbgv.exe" : "nbgv");
        if (!File.Exists(nbgv))
            throw new InvalidDataException("Native NBGV installation omitted its executable.");
        RubyGroupEndpoint basis = await EndpointAsync(before, request.Basis, "basis");
        RubyGroupEndpoint candidate = await EndpointAsync(after, request.Candidate, "candidate");
        token.ThrowIfCancellationRequested();
        return new(new(basis, candidate, request.Full, request.ScopePath), commands.ToArray());

        async Task<RubyGroupEndpoint> EndpointAsync(GitMaterialization checkout,
            MaterializedEndpoint endpoint, string name)
        {
            string state = Directory.CreateDirectory(Path.Combine(scratch, name)).FullName;
            Dictionary<string, string?> environment = EnvironmentFor(state);
            string query = Directory.CreateDirectory(Path.Combine(state, "query")).FullName;
            var facts = new RubyFactTools(request.Tools.Ruby, request.Helper, query,
                environment, request.DeadlineSeconds);
            // Gemfiles may load generated source. Prepare it before native Bundler evaluation.
            await RubySourceCollector.ReadAsync(checkout, facts, nbgv, ExecuteAsync, token);
            await RubyBundlePreparation.PrepareAsync(checkout, new(facts, request.Tools.Gem,
                request.Tools.Bundle, request.BundlerProbeVersion), ExecuteAsync, token);
            return new(endpoint, new(facts, nbgv, request.BundlerProbeVersion));
        }

        async Task<NativeCommandResult> ExecuteAsync(NativeCommand command,
            CancellationToken cancellation)
        {
            NativeCommandResult result = await execute(command, cancellation);
            commands.Add(new(command, result));
            return result;
        }

        async Task RunAsync(NativeCommand command)
        {
            NativeCommandResult result = await ExecuteAsync(command, token);
            token.ThrowIfCancellationRequested();
            if (!result.Succeeded || result.Error is not null)
                throw new RubyBundlePreparationException(commands.ToArray());
        }

        Dictionary<string, string?> EnvironmentFor(string state)
        {
            var environment = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (string name in new[] { "SystemRoot", "WINDIR", "PATHEXT" })
                environment[name] = System.Environment.GetEnvironmentVariable(name);
            string home = Directory.CreateDirectory(Path.Combine(state, "home")).FullName;
            string temporary = Directory.CreateDirectory(Path.Combine(state, "temporary")).FullName;
            string system = OperatingSystem.IsWindows()
                ? System.Environment.GetFolderPath(System.Environment.SpecialFolder.System)
                : "/usr/bin";
            environment["PATH"] = string.Join(Path.PathSeparator, new[] { request.Tools.Ruby,
                request.Tools.Gem, request.Tools.Bundle, request.Tools.Dotnet }
                .Select(Path.GetDirectoryName).Append(system).Append(OperatingSystem.IsWindows()
                    ? system : "/bin").Distinct(StringComparer.Ordinal));
            environment["HOME"] = home;
            environment["USERPROFILE"] = home;
            environment["DOTNET_CLI_HOME"] = home;
            environment["DOTNET_ROOT"] = Path.GetDirectoryName(request.Tools.Dotnet)!;
            environment["XDG_CONFIG_HOME"] = Path.Combine(home, "config");
            environment["XDG_CACHE_HOME"] = Path.Combine(home, "cache");
            environment["XDG_DATA_HOME"] = Path.Combine(home, "data");
            environment["TMPDIR"] = temporary;
            environment["TEMP"] = temporary;
            environment["TMP"] = temporary;
            environment["LANG"] = "C.UTF-8";
            environment["LC_ALL"] = "C.UTF-8";
            environment["GEM_HOME"] = Path.Combine(state, "gems");
            environment["GEM_PATH"] = environment["GEM_HOME"];
            environment["BUNDLE_PATH"] = Path.Combine(state, "bundle");
            environment["BUNDLE_APP_CONFIG"] = Path.Combine(state, "bundle-config");
            environment["BUNDLE_USER_HOME"] = Path.Combine(state, "bundle-user");
            environment["BUNDLE_USER_CACHE"] = Path.Combine(state, "bundle-cache");
            environment["BUNDLE_IGNORE_CONFIG"] = "1";
            environment["BUNDLE_FROZEN"] = "1";
            return environment;
        }
    }

    private static async Task<string> PinAsync(GitMaterialization checkout, CancellationToken token)
    {
        string text = await checkout.ReadOptionalTextAsync(".config/dotnet-tools.json",
            token) ?? throw new InvalidDataException(
                "Ruby preparation requires the committed native tool manifest.");
        using JsonDocument manifest = JsonDocument.Parse(text);
        string? version = manifest.RootElement.GetProperty("tools").GetProperty("nbgv")
            .GetProperty("version").GetString();
        return !string.IsNullOrWhiteSpace(version) ? version :
            throw new InvalidDataException("The native tool manifest omits its NBGV pin.");
    }

    private static string Absolute(string path) => Path.IsPathFullyQualified(path) &&
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)) == path ? path :
        throw new InvalidDataException("Ruby preparation requires canonical absolute paths.");
}
