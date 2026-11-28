using WorkflowDelivery.Core;
using WorkflowDelivery.Ecosystems;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Release;

internal static class BrowserStorePreparation
{
    internal static BrowserStoreUnit DefaultUnit =>
        ReleaseUnitCatalog.BrowserStoreUnits.Single();
    internal const string ChromeWebStore = "chrome-web-store";
    internal const string FirefoxAmo = "firefox-amo";
    internal const string EdgeAddons = "edge-addons";
    private static readonly string[] Required = ["native-input", "build", "contents",
        "consumer", "original-bytes"];

    internal static string[] StoreOutputs(string store) => store switch
    {
        ChromeWebStore => ["chrome-zip"],
        FirefoxAmo => ["firefox-zip", "firefox-sources"],
        EdgeAddons => ["edge-zip"],
        _ => throw new InvalidDataException("Unsupported browser store."),
    };

    internal static BrowserStoreUnit RequireRequest(ReleaseSubject subject,
        BrowserStoreTarget target)
    {
        _ = StoreOutputs(subject.Destination);
        BrowserStoreUnit registered = ReleaseUnitCatalog.AdmitBrowserStore(subject);
        if (target.Store != subject.Destination || string.IsNullOrWhiteSpace(target.Item) ||
            (subject.Destination == ChromeWebStore) == string.IsNullOrWhiteSpace(
                target.Publisher))
            throw new InvalidDataException("Unsupported browser-store release request.");
        ReleasePolicy.RequireEligible(subject);
        return registered;
    }

    internal static Task<ReleasePreparationResult> PrepareAsync(
        ReleasePreparationRequest request, CancellationToken token) =>
        PrepareAsync(request, NativeProcess.ExecuteAsync,
            (checkout, revision, product, cancellation) =>
                new NbgvInputReader(checkout).BrowserVersion(revision, product, cancellation),
            token);

    internal static async Task<ReleasePreparationResult> PrepareAsync(
        ReleasePreparationRequest request,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        Func<string, GitRevision, string, CancellationToken, BrowserExtensionVersion> version,
        CancellationToken token)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        lifetime.CancelAfter(TimeSpan.FromMinutes(15));
        token = lifetime.Token;
        BrowserStoreTarget storeTarget = request.Target
            ?? throw new InvalidDataException("Unsupported browser-store release request.");
        BrowserStoreUnit registered = RequireRequest(request.Subject, storeTarget);
        (string checkout, string scratch) = ReleaseQualification.RequireFreshScratch(
            request.Checkout, request.Scratch, PathMessage,
            "Release requires fresh external scratch.");
        if (!GitReader.IsCommitId(request.Subject.Revision))
            throw new InvalidDataException("Unsupported selected browser-store revision.");
        var qualification = new ReleaseQualification(execute, token);
        var environment = ReleasePreparationContext.CreateHomeEnvironment(scratch,
            ["PATH", "SystemRoot", "WINDIR", "PATHEXT", "DOTNET_ROOT", "DOTNET_ROOT_X64",
                "GITHUB_ACTIONS", "GITHUB_REF", "GITHUB_SHA", "GITHUB_RUN_ID", "GITHUB_RUN_NUMBER",
                "GITHUB_REPOSITORY", "MISE_DATA_DIR"],
            [("MISE_CACHE_DIR", Path.Combine(scratch, "home", "mise-cache")),
                ("MISE_EXEC_AUTO_INSTALL", "0"), ("MISE_AUTO_INSTALL", "0"),
                ("MISE_TRUSTED_CONFIG_PATHS", checkout), ("DOTNET_CLI_TELEMETRY_OPTOUT", "1"),
                ("DOTNET_NOLOGO", "1")]);
        GitMaterialization bound = await GitMaterialization.BindAsync(checkout,
            request.Subject.Revision, token);
        ReleaseUnitDeclaration unit = ReleaseUnitDeclarationReader.Read(
            await bound.ReadOptionalTextAsync(request.Subject.Declaration, token)
                ?? throw new InvalidDataException("Missing selected release declaration."),
            request.Subject.Declaration);
        if (unit.Id != registered.Id || unit.Builds is not
            [{ Definition: "node/browser-extension-v1", EntryPoint: "package.json" } build] ||
            !build.Outputs.Select(o => new UnitOutput(o.Id, o.Role, o.Kind)).ToHashSet()
                .SetEquals(BrowserExtensionPackage.Outputs))
            throw new InvalidDataException("Unsupported complete browser-extension definition.");
        await ReleasePreparationContext.RequireCommittedInputsAsync(bound,
            ["mise.toml", "mise.lock", "pnpm-lock.yaml", "pnpm-workspace.yaml",
                registered.ProductDirectory + "/package.json",
                registered.ProductDirectory + "/version.json"], token);
        string product = Path.Combine(checkout, registered.ProductDirectory);
        string output = Path.Combine(product, ".output");
        bool official = request.Subject.Channel == "official";
        BrowserExtensionVersion projected = new("", "");
        string? releaseNotes = null;
        await qualification.CheckAsync("native-input", async () =>
        {
            projected = version(checkout, bound.Revision, registered.ProductDirectory, token);
            if (string.IsNullOrWhiteSpace(projected.Manifest) ||
                string.IsNullOrWhiteSpace(projected.Npm))
                throw new InvalidDataException("Missing native NBGV browser projection.");
            LockedToolSelection.RequireReadback("node", "v" + await ToolAsync("node"),
                await QueryAsync(new("node", checkout, ["--version"], 30)));
            LockedToolSelection.RequireReadback("pnpm", await ToolAsync("pnpm"),
                await QueryAsync(new("pnpm", checkout, ["--version"], 30)));
            if (Directory.Exists(output) || File.Exists(output))
                throw new InvalidDataException("Product output exists before the build.");
            if (official)
            {
                string changelogPath = registered.ProductDirectory + "/CHANGELOG.md";
                string changelog = await bound.ReadOptionalTextAsync(changelogPath, token)
                    ?? throw new InvalidDataException($"Missing committed {changelogPath}.");
                releaseNotes = ReleaseChangelog.Extract(changelog, projected.Npm,
                    request.Subject.Revision, changelogPath);
            }
        });
        ReleaseFile[] all = await UnitPreparation.QualifyAsync(request.Subject, build, scratch,
            qualification, Required, subject => BrowserExtensionPackage.QualifyAsync(subject,
                checkout, scratch, registered.ProductDirectory, new(registered.ManifestName,
                    projected.Manifest, projected.Npm), qualification.CheckAsync,
                command => RunAsync("build", command), token));
        string[] selectedOutputs = StoreOutputs(request.Subject.Destination);
        ReleaseFile[] files = [.. all.Where(file => selectedOutputs.Contains(file.Output))];
        PreparedGithubAnchor? anchor = releaseNotes is not null &&
            qualification.Errors.Length == 0 && files.Length > 0
                ? new(
                    [.. files.Select(file => file with
                    {
                        Name = Path.GetFileName(file.Path),
                        Version = projected.Npm,
                    })],
                    releaseNotes,
                    [.. all.Where(file => !selectedOutputs.Contains(file.Output))
                        .Select(file => Path.GetFileName(file.Path))])
                : null;
        return new(new(request.Subject, [.. files], Required.ToArray(), qualification.Checks,
                Anchor: anchor),
            new(StringComparer.Ordinal), qualification.Commands, qualification.Errors, storeTarget);

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
