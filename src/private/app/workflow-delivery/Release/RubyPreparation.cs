using System.Text.Json;
using WorkflowDelivery.Ecosystems;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Release;

internal sealed record RubyPreparationTools(string Ruby, string Gem, string Dotnet);
internal sealed record RubyPreparationRequest(ReleaseSubject Subject, string Checkout,
    string Scratch, RubyPreparationTools Tools);
internal sealed record RubyPreparationResult(PreparedRelease Prepared,
    Dictionary<string, string> Native, ReleaseCommand[] Commands, string[] Errors);

// The complete declared native gem is qualified independently of CI selection.
internal static class RubyPreparation
{
    internal const string Preset = "ruby/hcoona-release-smoke-ruby-v1";
    internal static readonly string[] Required = ["native-input", "build", "contents",
        "consumer", "original-bytes"];

    internal static Task<RubyPreparationResult> PrepareAsync(RubyPreparationRequest request,
        CancellationToken token) => PrepareAsync(request, NativeProcess.ExecuteAsync, token);

    internal static async Task<RubyPreparationResult> PrepareAsync(RubyPreparationRequest request,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        CancellationToken token)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        lifetime.CancelAfter(TimeSpan.FromMinutes(15));
        token = lifetime.Token;
        string checkout = Absolute(request.Checkout), scratch = Absolute(request.Scratch);
        foreach (string path in new[] { request.Tools.Ruby, request.Tools.Gem,
                     request.Tools.Dotnet }) _ = Absolute(path);
        if (!Directory.Exists(checkout) || !Directory.Exists(scratch) ||
            Within(scratch, checkout) || Within(checkout, scratch) ||
            Directory.EnumerateFileSystemEntries(scratch).Any())
            throw new InvalidDataException("Ruby Release requires fresh external state.");
        var commands = new List<ReleaseCommand>();
        var checks = new List<ReleaseCheck>();
        var errors = new List<string>();
        var files = new List<ReleaseFile>();
        var native = new Dictionary<string, string>(StringComparer.Ordinal);
        Dictionary<string, string?> environment = EnvironmentFor(scratch, request);
        GitMaterialization? bound = null;
        RubySmokePackageOperations? operations = null;
        RubyGemArchive? original = null;
        ReleaseBuild? build = null;
        string entry = "", directory = "", version = "", current = "native-input";

        await CheckAsync("native-input", async () =>
        {
            ReleaseSubject subject = request.Subject;
            bool buddy = subject.Channel == "buddy" &&
                subject.Destination == "github-packages-ruby";
            bool official = subject.Channel == "official" && subject.Destination == "rubygems";
            if (!(buddy || official) || string.IsNullOrWhiteSpace(subject.SelectedRef))
                throw new InvalidDataException("Unsupported Ruby Release channel or ref.");
            ReleasePolicy.RequireEligible(subject);
            GitRevision revision = await new GitReader(checkout).ReadAsync(subject.Revision, token);
            if (revision.Commit != subject.Revision)
                throw new InvalidDataException("Ruby Release requires the exact source revision.");
            bound = await GitMaterialization.BindAsync(checkout, revision, token);
            ReleaseUnitDeclaration unit = ReleaseUnitDeclarationReader.Read(
                await bound.ReadOptionalTextAsync(subject.Declaration, token) ??
                    throw new InvalidDataException("Missing committed Ruby release declaration."),
                subject.Declaration);
            if (unit.Id != subject.Unit || unit.Builds is not
                [{ Definition: "ruby/gem-v1", Outputs:
                    [{ Role: "primary-package", Kind: "ruby-gem" }] }])
                throw new InvalidDataException("Unsupported complete Ruby Release recipe.");
            build = unit.Builds[0];
            string parent = Path.GetDirectoryName(subject.Declaration)!.Replace('\\', '/');
            entry = parent.Length == 0 ? build.EntryPoint : parent + "/" + build.EntryPoint;
            directory = Path.GetDirectoryName(entry)!.Replace('\\', '/');
            if (Path.GetFileName(entry) != "hcoona-release-smoke-ruby.gemspec" ||
                await bound.ReadOptionalTextAsync(entry, token) is null)
                throw new InvalidDataException("Missing committed adopted Ruby entry.");
            QualitySelection? quality = await new QualitySelectionReader(checkout,
                bound.ReadOptionalTextAsync).ReadAsync(directory, "ruby", token);
            if (quality?.Preset != Preset)
                throw new InvalidDataException("Unsupported complete Ruby quality selection.");
            if ((await RunAsync(new("git", checkout,
                ["rev-parse", "--is-shallow-repository"], 30))).Stdout.Trim() != "false")
                throw new InvalidDataException("Native versioning requires complete Git history.");
            string manifestText = await bound.ReadOptionalTextAsync(".config/dotnet-tools.json",
                token) ?? throw new InvalidDataException("Missing committed NBGV tool manifest.");
            using JsonDocument manifest = JsonDocument.Parse(manifestText);
            string pin = manifest.RootElement.GetProperty("tools").GetProperty("nbgv")
                .GetProperty("version").GetString() ??
                    throw new InvalidDataException("Missing committed NBGV pin.");
            using JsonDocument global = JsonDocument.Parse(await bound.ReadOptionalTextAsync(
                "global.json", token) ?? throw new InvalidDataException("Missing SDK selection."));
            JsonElement sdk = global.RootElement.GetProperty("sdk");
            if (sdk.GetProperty("rollForward").GetString() != "disable" ||
                (await RunAsync(new(request.Tools.Dotnet, checkout, ["--version"], 30))).Stdout
                    .Trim() != sdk.GetProperty("version").GetString())
                throw new InvalidDataException("Actual SDK differs from committed selection.");
            string toolsDirectory = Directory.CreateDirectory(Path.Combine(scratch,
                "native-tools")).FullName;
            await RunAsync(new(request.Tools.Dotnet, checkout,
                ["tool", "install", "nbgv", "--version", pin, "--tool-path", toolsDirectory], 300));
            string nbgv = Path.Combine(toolsDirectory, OperatingSystem.IsWindows() ?
                "nbgv.exe" : "nbgv");
            if (!File.Exists(nbgv))
                throw new InvalidDataException("Native NBGV preparation omitted its executable.");
            using JsonDocument nativeTools = JsonDocument.Parse((await RunAsync(new(
                request.Tools.Ruby, checkout, ["-r", "json", "-r", "rubygems", "-e",
                    "puts JSON.generate({ruby: RUBY_VERSION, rubygems: Gem::VERSION})"], 30)))
                .Stdout);
            string ruby = nativeTools.RootElement.GetProperty("ruby").GetString()!;
            string rubygems = nativeTools.RootElement.GetProperty("rubygems").GetString()!;
            if (!Version.TryParse(ruby, out Version? runtime) || runtime.Major != 4 ||
                string.IsNullOrWhiteSpace(rubygems))
                throw new InvalidDataException("Unsupported native Ruby runtime.");
            string helper = Path.Combine(checkout,
                "src/private/app/workflow-delivery/Native/Ruby/facts.rb");
            if (await bound.ReadOptionalTextAsync(
                "src/private/app/workflow-delivery/Native/Ruby/facts.rb", token) is null)
                throw new InvalidDataException("Missing committed native Ruby helper.");
            var facts = new RubyFactTools(request.Tools.Ruby, helper,
                Directory.CreateDirectory(Path.Combine(scratch, "queries")).FullName,
                environment, 180);
            using JsonDocument projection = JsonDocument.Parse((await RunAsync(new(nbgv,
                Path.Combine(checkout, directory), ["get-version", "--format", "json"], 180)))
                .Stdout);
            JsonElement value = projection.RootElement;
            string raw = value.GetProperty("SemVer2").GetString()!;
            bool publicRelease = value.GetProperty("PublicRelease").GetBoolean();
            if (value.GetProperty("GitCommitId").GetString() != subject.Revision ||
                !subject.DryRun && official && !publicRelease)
                throw new InvalidDataException("Native Ruby source/ref projection differs.");
            version = (await new RubyNativeFactCollector(facts, ExecuteAsync)
                .VersionAsync(Path.Combine(checkout, entry), raw, token)).Version;
            RubySourceGemFacts source = await RubySourceCollector.ReadSourceAsync(bound, entry,
                facts, nbgv, ExecuteAsync, token);
            if (source.Gem.Name != "hcoona-release-smoke-ruby" ||
                source.Gem.Version != version || source.Gem.Platform != "ruby")
                throw new InvalidDataException("Unsupported native Ruby source identity.");
            native.Add("name", source.Gem.Name);
            native.Add("version", version);
            native.Add("platform", source.Gem.Platform);
            native.Add("commit", subject.Revision);
            native.Add("public-release", publicRelease ? "true" : "false");
            native.Add("ruby", ruby);
            native.Add("rubygems", rubygems);
            native.Add("nbgv", pin);
            operations = new(facts, request.Tools.Gem, nbgv,
                Directory.CreateDirectory(Path.Combine(scratch, "package")).FullName);
        });
        await CheckAsync("build", async () => original = await operations!.BuildAsync(bound!,
            directory, entry, version, ExecuteAsync, token));
        await CheckAsync("contents", async () =>
            _ = await operations!.InspectReleaseAsync(original!, version, ExecuteAsync, token));
        await CheckAsync("consumer", () => operations!.ConsumeReleaseAsync(original!, version,
            ExecuteAsync, token));
        await CheckAsync("original-bytes", () =>
        {
            RubySmokePackageOperations.RequireOriginal(original!);
            files.Add(new(build!.Id, build.Outputs[0].Id, "ruby-gem", native["name"], version,
                Path.GetRelativePath(scratch, original!.Path).Replace('\\', '/'), original.Length,
                original.Sha256));
            return Task.CompletedTask;
        });
        return new(new(request.Subject, [.. files], Required.ToArray(), [.. checks]), native,
            [.. commands], [.. errors]);

        Task<NativeCommandResult> ExecuteAsync(NativeCommand command,
            CancellationToken cancellation) => RunAsync(command, cancellation);

        async Task<NativeCommandResult> RunAsync(NativeCommand command,
            CancellationToken? cancellation = null)
        {
            command = command with { Environment = command.Environment ?? environment,
                InheritEnvironment = false, CloseStandardInput = true };
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
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or
                InvalidDataException or JsonException or KeyNotFoundException or
                InvalidOperationException or ArgumentException or FormatException)
            { checks.Add(new(id, ReleaseCheckStatus.Failed)); errors.Add(failure.Message); }
        }
    }

    private static Dictionary<string, string?> EnvironmentFor(string scratch,
        RubyPreparationRequest request)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (string name in new[] { "SystemRoot", "WINDIR", "PATHEXT" })
            values[name] = Environment.GetEnvironmentVariable(name);
        string home = Directory.CreateDirectory(Path.Combine(scratch, "home")).FullName;
        values["PATH"] = string.Join(Path.PathSeparator, new[] { request.Tools.Ruby,
            request.Tools.Gem, request.Tools.Dotnet }.Select(Path.GetDirectoryName)
            .Append(OperatingSystem.IsWindows() ? Environment.GetFolderPath(
                Environment.SpecialFolder.System) : "/usr/bin").Append("/bin")
            .Distinct(StringComparer.Ordinal));
        foreach (string name in new[] { "HOME", "USERPROFILE", "DOTNET_CLI_HOME" })
            values[name] = home;
        foreach ((string name, string path) in new[] { ("XDG_CONFIG_HOME", "config"),
            ("XDG_CACHE_HOME", "cache"), ("XDG_DATA_HOME", "data"),
            ("APPDATA", "appdata"), ("LOCALAPPDATA", "localappdata"),
            ("TMPDIR", "tmp"), ("TEMP", "tmp"), ("TMP", "tmp") })
            values[name] = Directory.CreateDirectory(Path.Combine(home, path)).FullName;
        values["DOTNET_ROOT"] = Path.GetDirectoryName(request.Tools.Dotnet);
        values["GEM_HOME"] = Path.Combine(scratch, "gems");
        values["GEM_PATH"] = values["GEM_HOME"];
        values["GEM_SPEC_CACHE"] = Path.Combine(scratch, "spec-cache");
        values["LANG"] = "C.UTF-8";
        values["LC_ALL"] = "C.UTF-8";
        values["GITHUB_ACTIONS"] = "true";
        values["GITHUB_REF"] = request.Subject.SelectedRef;
        values["GITHUB_SHA"] = request.Subject.Revision;
        string output = Path.Combine(scratch, "nbgv-environment");
        File.WriteAllText(output, "");
        values["GITHUB_ENV"] = output;
        return values;
    }

    private static string Absolute(string path) => Path.IsPathFullyQualified(path)
        ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)) :
        throw new InvalidDataException("Ruby Release requires absolute prepared paths.");
    private static bool Within(string child, string parent) => child.Equals(parent,
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase :
            StringComparison.Ordinal) || child.StartsWith(parent + Path.DirectorySeparatorChar,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase :
                    StringComparison.Ordinal);
}
