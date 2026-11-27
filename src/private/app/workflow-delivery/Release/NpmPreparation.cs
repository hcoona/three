using System.Text.Json;
using WorkflowDelivery.Ecosystems;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Release;

internal static class NpmPreparation
{
    internal static Task<ReleasePreparationResult> PrepareAsync(ReleasePreparationRequest request,
        CancellationToken token) => PrepareAsync(request, NativeProcess.ExecuteAsync,
            (checkout, revision, product, cancellation) =>
                new NbgvInputReader(checkout).NpmVersion(revision, product, cancellation), token);

    internal static async Task<ReleasePreparationResult> PrepareAsync(
        ReleasePreparationRequest request,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        Func<string, GitRevision, string, CancellationToken, string> version,
        CancellationToken token)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        lifetime.CancelAfter(TimeSpan.FromMinutes(15));
        token = lifetime.Token;
        (string checkout, string scratch) = ReleaseQualification.RequireFreshScratch(
            request.Checkout, request.Scratch, PathMessage,
            "Release requires fresh external scratch.");
        if (scratch.Contains("%s", StringComparison.Ordinal) ||
            scratch.Contains("%v", StringComparison.Ordinal))
            throw new InvalidDataException("Scratch cannot contain native pack output markers.");
        // Admission precedes every scratch write.
        NpmReleaseUnit recipe = ReleaseUnitCatalog.AdmitNpm(request.Subject);
        var qualification = new ReleaseQualification(execute, token);
        var files = new List<ReleaseFile>();
        var environment = ReleasePreparationContext.CreateHomeEnvironment(scratch,
            ["PATH", "SystemRoot", "WINDIR", "PATHEXT", "DOTNET_ROOT", "DOTNET_ROOT_X64",
                "NUGET_PACKAGES", "GITHUB_ACTIONS", "GITHUB_REF", "GITHUB_SHA", "GITHUB_RUN_ID",
                "GITHUB_RUN_NUMBER", "GITHUB_REPOSITORY", "MISE_DATA_DIR"],
            [("MISE_CACHE_DIR", Path.Combine(scratch, "home", "mise-cache")),
                ("MISE_EXEC_AUTO_INSTALL", "0"), ("MISE_AUTO_INSTALL", "0"),
                ("MISE_TRUSTED_CONFIG_PATHS", checkout)]);
        if (!GitReader.IsCommitId(request.Subject.Revision))
            throw new InvalidDataException("Unsupported selected npm destination and mode.");
        string package = recipe.PackageName(request.Subject);
        GitMaterialization bound = await GitMaterialization.BindAsync(checkout,
            request.Subject.Revision, token);
        ReleaseUnitDeclaration unit = ReleaseUnitDeclarationReader.Read(
            await bound.ReadOptionalTextAsync(request.Subject.Declaration, token)
                ?? throw new InvalidDataException("Missing selected release declaration."),
            request.Subject.Declaration);
        if (unit.Id != request.Subject.Unit || unit.Builds is not
            [
            {
                Definition: "node/npm-package-v1", Outputs:
                [{ Role: "primary-package", Kind: "npm-tarball" }]
            }])
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
        await ReleasePreparationContext.RequireCommittedInputsAsync(bound,
            ["mise.toml", "mise.lock", "pnpm-lock.yaml", "pnpm-workspace.yaml"], token);
        bool official = request.Subject.Channel == "official";
        string nativeVersion = "";
        string? releaseNotes = null;
        await qualification.CheckAsync("native-input", async () =>
        {
            nativeVersion = version(checkout, bound.Revision, product, token);
            if (string.IsNullOrWhiteSpace(nativeVersion))
                throw new InvalidDataException("Missing native NBGV npm projection.");
            string node = await ToolAsync("node");
            string pnpm = await ToolAsync("pnpm");
            LockedToolSelection.RequireReadback("node", "v" + node,
                await QueryAsync(new("node", checkout, ["--version"], 30)));
            LockedToolSelection.RequireReadback("pnpm", pnpm,
                await QueryAsync(new("pnpm", checkout, ["--version"], 30)));
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
                if (value.GetProperty("name").GetString() != recipe.SourceName ||
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
            if (official)
            {
                string changelogPath = product + "/CHANGELOG.md";
                string changelog = await bound.ReadOptionalTextAsync(changelogPath, token)
                    ?? throw new InvalidDataException($"Missing committed {changelogPath}.");
                releaseNotes = ReleaseChangelog.Extract(changelog, nativeVersion,
                    request.Subject.Revision, changelogPath);
            }
        });
        string originals = Path.Combine(scratch, "original");
        Directory.CreateDirectory(originals);
        string archive = Path.Combine(originals, "package.tgz");
        long size = 0;
        string? sha256 = null;
        var actions = new Dictionary<string, Func<Task>>(StringComparer.Ordinal)
        {
            ["build"] = async () => await RunAsync("build", new("pnpm", checkout,
                ["--dir", Path.Combine(checkout, product),
                    "--config.verify-deps-before-run=false", "run", "build"], 300)),
            ["test"] = async () => await RunAsync("test", new("pnpm", checkout,
                ["--dir", Path.Combine(checkout, product),
                    "--config.verify-deps-before-run=false", "run", "test"], 300)),
            ["pack"] = async () =>
            {
                // The channel package name is set only in the disposable checkout.
                if (package != recipe.SourceName)
                    await RunAsync("pack", new("pnpm", checkout,
                        ["--dir", Path.Combine(checkout, product), "pkg", "set",
                            "name=" + package], 30));
                await RunAsync("pack", NpmPackageOperations.Pack(checkout, product, archive));
                (size, sha256) = await NpmPackageOperations.OriginalAsync(archive, token);
            },
            ["contents"] = async () =>
            {
                NativeCommand command = await NpmPackageOperations.MetadataAsync(archive,
                    Path.Combine(scratch, "metadata"), token);
                NativeCommandResult result = await RunAsync("contents", command);
                AdoptedNpmPackage.ValidateMetadata(result.Stdout, nativeVersion, package,
                    recipe.Main, recipe.RegistryDependencies);
            },
            ["consumer"] = async () =>
            {
                await RunAsync("consumer", await NpmPackageOperations.InstallAsync(archive,
                    scratch, offline: !recipe.RegistryDependencies, token));
                await RunAsync("consumer", recipe.Import(scratch, package));
            },
            ["original-bytes"] = async () =>
            {
                (long actualSize, string actualDigest) =
                    await NpmPackageOperations.OriginalAsync(archive, token);
                if (actualSize != size || actualDigest != sha256)
                    throw new InvalidDataException(
                        "Qualified original changed during consumption.");
                files.Add(new(build.Id, build.Outputs[0].Id, "npm-tarball", package,
                    nativeVersion, "original/package.tgz", size, sha256!));
            },
        };
        foreach (string id in recipe.Required.Skip(1))
            await qualification.CheckAsync(id, actions[id]);
        PreparedGithubAnchor? anchor = releaseNotes is not null &&
            qualification.Errors.Length == 0 && files.Count > 0
                ? new([.. files.Select(file => file with
                    { Name = $"{NpmReleaseUnit.UnscopedName(package)}-{nativeVersion}.tgz" })],
                    releaseNotes)
                : null;
        return new(new(request.Subject, [.. files], [.. recipe.Required], qualification.Checks,
                Anchor: anchor),
            new(StringComparer.Ordinal), qualification.Commands, qualification.Errors);

        Task<NativeCommandResult> RunAsync(string check, NativeCommand command) =>
            qualification.RunAsync(command with
            {
                Environment = environment,
                InheritEnvironment = false,
            }, check: check, requireNullError: false);

        async Task<string> QueryAsync(NativeCommand command) =>
            (await RunAsync("native-input", command)).Stdout.Trim();

        Task<string> ToolAsync(string tool) => ReleasePreparationContext.SelectLockedMiseToolAsync(
            checkout, tool, command => RunAsync("native-input", command));
    }

    private const string PathMessage = "Release execution paths must be absolute.";
}
