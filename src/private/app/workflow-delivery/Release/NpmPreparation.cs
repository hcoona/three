using System.Text.Json;
using WorkflowDelivery.Ecosystems;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Release;

internal sealed record NpmPreparationRequest(ReleaseSubject Subject, string Checkout,
    string Scratch);
internal sealed record ReleaseCommand(string Check, NativeCommand Command,
    NativeCommandResult Result);
internal sealed record NpmPreparationResult(PreparedRelease Prepared,
    ReleaseCommand[] Commands, string[] Errors);

internal static class NpmPreparation
{
    private static readonly string[] Required = ["native-input", "build", "test", "pack",
        "contents", "consumer", "original-bytes"];

    internal static Task<NpmPreparationResult> PrepareAsync(NpmPreparationRequest request,
        CancellationToken token) => PrepareAsync(request, NativeProcess.ExecuteAsync,
            (checkout, revision, product, cancellation) =>
                new NbgvInputReader(checkout).NpmVersion(revision, product, cancellation), token);

    internal static async Task<NpmPreparationResult> PrepareAsync(NpmPreparationRequest request,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        Func<string, GitRevision, string, CancellationToken, string> version,
        CancellationToken token)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        lifetime.CancelAfter(TimeSpan.FromMinutes(15));
        token = lifetime.Token;
        string checkout = FullPath(request.Checkout);
        string scratch = FullPath(request.Scratch);
        if (!Directory.Exists(checkout) || !Directory.Exists(scratch) ||
            Within(scratch, checkout) || Within(checkout, scratch) ||
            HasLinkedAncestor(checkout) || HasLinkedAncestor(scratch) ||
            Directory.EnumerateFileSystemEntries(scratch).Any())
            throw new InvalidDataException("Release requires fresh external scratch.");
        if (scratch.Contains("%s", StringComparison.Ordinal) ||
            scratch.Contains("%v", StringComparison.Ordinal))
            throw new InvalidDataException("Scratch cannot contain native pack output markers.");
        var commands = new List<ReleaseCommand>();
        var checks = new List<ReleaseCheck>();
        var errors = new List<string>();
        var files = new List<ReleaseFile>();
        string home = Path.Combine(scratch, "home");
        Directory.CreateDirectory(home);
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (string name in new[] { "PATH", "SystemRoot", "WINDIR", "PATHEXT",
            "DOTNET_ROOT", "DOTNET_ROOT_X64", "NUGET_PACKAGES", "GITHUB_ACTIONS", "GITHUB_REF",
            "GITHUB_SHA", "GITHUB_RUN_ID", "GITHUB_RUN_NUMBER", "GITHUB_REPOSITORY",
            "MISE_DATA_DIR" })
            if (Environment.GetEnvironmentVariable(name) is { } value)
                environment.Add(name, value);
        foreach ((string name, string value) in new[]
        {
            ("HOME", home), ("USERPROFILE", home), ("DOTNET_CLI_HOME", home),
            ("XDG_CONFIG_HOME", Path.Combine(home, "config")),
            ("XDG_CACHE_HOME", Path.Combine(home, "cache")),
            ("XDG_DATA_HOME", Path.Combine(home, "data")),
            ("MISE_CACHE_DIR", Path.Combine(home, "mise-cache")),
            ("MISE_EXEC_AUTO_INSTALL", "0"), ("MISE_AUTO_INSTALL", "0"),
            ("MISE_TRUSTED_CONFIG_PATHS", checkout),
        }) environment[name] = value;
        GitRevision revision = await new GitReader(checkout).ReadAsync(request.Subject.Revision,
            token);
        GitMaterialization bound = await GitMaterialization.BindAsync(checkout, revision, token);
        bool official = request.Subject.Channel == "official" &&
            request.Subject.Destination == "npmjs" && request.Subject.DryRun;
        bool buddy = request.Subject.Channel == "buddy" &&
            request.Subject.Destination == "github-packages-npm";
        if (revision.Commit != request.Subject.Revision || !(official || buddy))
            throw new InvalidDataException("Unsupported selected npm destination and mode.");
        ReleaseUnitDeclaration unit = ReleaseUnitDeclarationReader.Read(
            await bound.ReadOptionalTextAsync(request.Subject.Declaration, token)
                ?? throw new InvalidDataException("Missing selected release declaration."),
            request.Subject.Declaration);
        if (unit.Id != request.Subject.Unit || unit.Builds is not
            [{ Definition: "node/npm-package-v1", Outputs:
                [{ Role: "primary-package", Kind: "npm-tarball" }] }])
            throw new InvalidDataException("Unsupported complete npm release definition.");
        ReleaseBuild build = unit.Builds[0];
        string parent = NodePackageChecks.Parent(unit.SourcePath);
        string entry = parent.Length == 0 ? build.EntryPoint : parent + "/" + build.EntryPoint;
        if (!entry.EndsWith("/package.json", StringComparison.Ordinal) ||
            await bound.ReadOptionalTextAsync(entry, token) is null)
            throw new InvalidDataException("Missing native selected npm entry point.");
        string product = NodePackageChecks.Parent(entry);
        QualitySelection? quality = await new QualitySelectionReader(checkout,
            bound.ReadOptionalTextAsync).ReadAsync(product, "node", token);
        if (quality?.Preset != "node/hcoona-release-smoke-npm-v1")
            throw new InvalidDataException("Unsupported selected npm quality recipe.");
        foreach (string input in new[] { "mise.toml", "mise.lock", "pnpm-lock.yaml",
            "pnpm-workspace.yaml" })
            if (await bound.ReadOptionalTextAsync(input, token) is null)
                throw new InvalidDataException("Missing committed native input: " + input);
        string nativeVersion = "";
        await CheckAsync("native-input", async () =>
        {
            nativeVersion = version(checkout, revision, product, token);
            if (string.IsNullOrWhiteSpace(nativeVersion))
                throw new InvalidDataException("Missing native NBGV npm projection.");
            string node = await ToolAsync("node", "24");
            string pnpm = await ToolAsync("pnpm", null);
            if (await QueryAsync(new("node", checkout, ["--version"], 30)) != "v" + node ||
                await QueryAsync(new("pnpm", checkout, ["--version"], 30)) != pnpm)
                throw new InvalidDataException(
                    "Actual native tools differ from selected locked tools.");
            using (JsonDocument metadata = JsonDocument.Parse(await QueryAsync(new("pnpm", checkout,
                ["--dir", Path.Combine(checkout, product), "pkg", "get", "--json", "name",
                    "publishConfig.directory", "scripts"], 30))))
            {
                JsonElement value = metadata.RootElement;
                if (value.ValueKind != JsonValueKind.Object ||
                    value.EnumerateObject().Select(field => field.Name)
                        .Distinct(StringComparer.Ordinal).Count() !=
                        value.EnumerateObject().Count())
                    throw new InvalidDataException("Ambiguous selected native npm metadata.");
                if (value.GetProperty("name").GetString() !=
                    AdoptedNpmPackage.PackageName ||
                    value.TryGetProperty("publishConfig.directory", out _) ||
                    !value.GetProperty("scripts").TryGetProperty(
                        "build",
                        out JsonElement sourceBuild) ||
                    sourceBuild.ValueKind != JsonValueKind.String ||
                    !value.GetProperty("scripts").TryGetProperty(
                        "test", out JsonElement sourceTest) ||
                    sourceTest.ValueKind != JsonValueKind.String)
                    throw new InvalidDataException("Unsupported selected native npm recipe.");
            }
        });
        string originals = Path.Combine(scratch, "original");
        Directory.CreateDirectory(originals);
        string archive = Path.Combine(originals, "package.tgz");
        long size = 0;
        string? sha256 = null;
        await CheckAsync("build", async () => await RunAsync("build", new("pnpm", checkout,
            ["--dir", Path.Combine(checkout, product), "--config.verify-deps-before-run=false",
                "run", "build"], 300)));
        await CheckAsync("test", async () => await RunAsync("test", new("pnpm", checkout,
            ["--dir", Path.Combine(checkout, product), "--config.verify-deps-before-run=false",
                "run", "test"], 300)));
        await CheckAsync("pack", async () =>
        {
            await RunAsync("pack", NpmPackageOperations.Pack(checkout, product, archive));
            (size, sha256) = await NpmPackageOperations.OriginalAsync(archive, 25_000_000, token);
        });
        await CheckAsync("contents", async () =>
        {
            NativeCommand command = await NpmPackageOperations.MetadataAsync(archive,
                Path.Combine(scratch, "metadata"), token);
            NativeCommandResult result = await RunAsync("contents", command);
            AdoptedNpmPackage.ValidateMetadata(result.Stdout, nativeVersion);
        });
        await CheckAsync("consumer", async () =>
        {
            await RunAsync("consumer", await NpmPackageOperations.InstallAsync(archive, scratch,
                token));
            await RunAsync("consumer", AdoptedNpmPackage.Import(scratch));
        });
        await CheckAsync("original-bytes", async () =>
        {
            (long actualSize, string actualDigest) = await NpmPackageOperations.OriginalAsync(
                archive, 25_000_000, token);
            if (actualSize != size || actualDigest != sha256)
                throw new InvalidDataException("Qualified original changed during consumption.");
            files.Add(new(build.Id, build.Outputs[0].Id, "npm-tarball",
                AdoptedNpmPackage.PackageName, nativeVersion, "original/package.tgz", size,
                sha256!));
        });
        return new(new(request.Subject, [.. files], Required.ToArray(), [.. checks]),
            [.. commands], [.. errors]);

        async Task<NativeCommandResult> RunAsync(string check, NativeCommand command)
        {
            command = command with { Environment = environment, InheritEnvironment = false };
            NativeCommandResult result = await execute(command, token);
            commands.Add(new(check, command, result));
            if (result.Termination == NativeTermination.Cancelled)
                throw new OperationCanceledException(token);
            if (result.Termination == NativeTermination.TimedOut)
                throw new TimeoutException($"Native {check} timed out.");
            if (!result.Succeeded)
                throw new InvalidDataException($"Native {check} failed: {result.Termination}.");
            return result;
        }

        async Task<string> QueryAsync(NativeCommand command) =>
            (await RunAsync("native-input", command)).Stdout.Trim();

        async Task CheckAsync(string id, Func<Task> action)
        {
            if (checks.Any(check => check.Status != ReleaseCheckStatus.Passed))
            { checks.Add(new(id, ReleaseCheckStatus.Skipped)); return; }
            try { await action(); checks.Add(new(id, ReleaseCheckStatus.Passed)); }
            catch (OperationCanceledException)
            { checks.Add(new(id, ReleaseCheckStatus.Cancelled)); errors.Add(id + " cancelled."); }
            catch (TimeoutException)
            { checks.Add(new(id, ReleaseCheckStatus.TimedOut)); errors.Add(id + " timed out."); }
            catch (Exception failure) when (failure is IOException or JsonException or
                UnauthorizedAccessException or InvalidDataException or KeyNotFoundException or
                InvalidOperationException)
            { checks.Add(new(id, ReleaseCheckStatus.Failed)); errors.Add(failure.Message); }
        }

        async Task<string> ToolAsync(string tool, string? requested)
        {
            using JsonDocument json = JsonDocument.Parse(await QueryAsync(new("mise", checkout,
                ["ls", "--json", "--current", "--locked", tool], 30)));
            if (json.RootElement.ValueKind != JsonValueKind.Array ||
                json.RootElement.GetArrayLength() != 1)
                throw new InvalidDataException("Unavailable selected native tool: " + tool);
            JsonElement value = json.RootElement[0];
            string selected = value.GetProperty("version").GetString()!;
            if (!System.Version.TryParse(selected, out System.Version? parsed) ||
                parsed.Build < 0 ||
                (tool == "node" && parsed.Major != 24) ||
                value.GetProperty("requested_version").GetString() != (requested ?? selected) ||
                value.GetProperty("active").ValueKind != JsonValueKind.True ||
                value.GetProperty("source").GetProperty("type").GetString() != "mise.toml" ||
                value.GetProperty("source").GetProperty("path").GetString() !=
                    Path.Combine(checkout, "mise.toml"))
                throw new InvalidDataException("Native tool differs from committed authority.");
            return selected;
        }
    }

    private static string FullPath(string path) => Path.IsPathFullyQualified(path)
        ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(path))
        : throw new InvalidDataException("Release execution paths must be absolute.");

    private static bool Within(string path, string parent) => path == parent ||
        path.StartsWith(parent + Path.DirectorySeparatorChar, OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static bool HasLinkedAncestor(string path)
    {
        for (DirectoryInfo? directory = new(path); directory is not null;
            directory = directory.Parent)
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) return true;
        return false;
    }
}
