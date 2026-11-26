using System.Text.Json;
using WorkflowDelivery.Ecosystems;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Release;

// The store coordinate is bound into the reviewed request; the publisher never re-reads it.
internal sealed record BrowserStoreTarget(string Store, string Item, string? Publisher);
internal sealed record BrowserStorePreparationRequest(ReleaseSubject Subject,
    BrowserStoreTarget Target, string Checkout, string Scratch);
internal sealed record BrowserStorePreparationResult(PreparedRelease Prepared,
    BrowserStoreTarget Target, ReleaseCommand[] Commands, string[] Errors) : IReleasePreparation
{
    public void Validate() => BrowserStoreRelease.Validate(this);
}

internal static class BrowserStorePreparation
{
    internal const string Unit = "steam-account-history-to-csv";
    internal const string Product = "src/public/lib/steam-account-history-to-csv";
    internal const string Declaration = Product + "/workflow-delivery.release-unit.yml";
    internal const string ManifestName = "Steam Account History to CSV";
    internal const string ChromeWebStore = "chrome-web-store";
    internal const string FirefoxAmo = "firefox-amo";
    internal const string EdgeAddons = "edge-addons";
    private static readonly string[] Required = ["native-input", "build", "contents",
        "consumer", "original-bytes"];

    // Each WXT target zip; the Firefox build also emits the AMO source archive.
    private static readonly (string Output, string Kind, string Script, string Suffix,
        int Manifest)[] Outputs =
    [
        ("chrome-zip", "chrome-extension-zip", "zip", "chrome", 3),
        ("firefox-zip", "firefox-extension-zip", "zip:firefox", "firefox", 2),
        ("firefox-sources", "firefox-source-zip", "", "sources", 0),
        ("edge-zip", "edge-extension-zip", "zip:edge", "edge", 3),
    ];

    internal static string[] StoreOutputs(string store) => store switch
    {
        ChromeWebStore => ["chrome-zip"],
        FirefoxAmo => ["firefox-zip", "firefox-sources"],
        EdgeAddons => ["edge-zip"],
        _ => throw new InvalidDataException("Unsupported browser store."),
    };

    internal static void RequireRequest(ReleaseSubject subject, BrowserStoreTarget target)
    {
        _ = StoreOutputs(subject.Destination);
        // The stores are Official destinations only; a Buddy request is a dry run.
        if (subject.Unit != Unit || subject.Declaration != Declaration ||
            target.Store != subject.Destination || string.IsNullOrWhiteSpace(target.Item) ||
            (subject.Destination == ChromeWebStore) == string.IsNullOrWhiteSpace(
                target.Publisher) ||
            !(subject.Channel == "official" || (subject.Channel == "buddy" && subject.DryRun)))
            throw new InvalidDataException("Unsupported browser-store release request.");
        ReleasePolicy.RequireEligible(subject);
    }

    internal static Task<BrowserStorePreparationResult> PrepareAsync(
        BrowserStorePreparationRequest request, CancellationToken token) =>
        PrepareAsync(request, NativeProcess.ExecuteAsync,
            (checkout, revision, product, cancellation) =>
                new NbgvInputReader(checkout).BrowserVersion(revision, product, cancellation),
            token);

    internal static async Task<BrowserStorePreparationResult> PrepareAsync(
        BrowserStorePreparationRequest request,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        Func<string, GitRevision, string, CancellationToken, BrowserExtensionVersion> version,
        CancellationToken token)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        lifetime.CancelAfter(TimeSpan.FromMinutes(15));
        token = lifetime.Token;
        RequireRequest(request.Subject, request.Target);
        (string checkout, string scratch) = ReleaseQualification.RequireFreshScratch(
            request.Checkout, request.Scratch, PathMessage,
            "Release requires fresh external scratch.");
        if (!GitReader.IsCommitId(request.Subject.Revision))
            throw new InvalidDataException("Unsupported selected browser-store revision.");
        var qualification = new ReleaseQualification(execute, token);
        var files = new List<ReleaseFile>();
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
        if (unit.Id != Unit || unit.Builds is not
            [{ Definition: "node/browser-extension-v1", EntryPoint: "package.json" } build] ||
            !build.Outputs.Select(output => (output.Id, output.Role, output.Kind)).Order()
                .SequenceEqual(Outputs.Select(output => (output.Output, output.Manifest == 0
                    ? "source-package" : "primary-package", output.Kind)).Order()))
            throw new InvalidDataException("Unsupported complete browser-extension definition.");
        await ReleasePreparationContext.RequireCommittedInputsAsync(bound,
            ["mise.toml", "mise.lock", "pnpm-lock.yaml", "pnpm-workspace.yaml",
                Product + "/package.json", Product + "/version.json"], token);
        string product = Path.Combine(checkout, Product);
        string output = Path.Combine(product, ".output");
        BrowserExtensionVersion projected = new("", "");
        await qualification.CheckAsync("native-input", async () =>
        {
            projected = version(checkout, bound.Revision, Product, token);
            if (string.IsNullOrWhiteSpace(projected.Manifest) ||
                string.IsNullOrWhiteSpace(projected.Npm))
                throw new InvalidDataException("Missing native NBGV browser projection.");
            LockedToolSelection.RequireReadback("node", "v" + await ToolAsync("node"),
                await QueryAsync(new("node", checkout, ["--version"], 30)));
            LockedToolSelection.RequireReadback("pnpm", await ToolAsync("pnpm"),
                await QueryAsync(new("pnpm", checkout, ["--version"], 30)));
            if (Directory.Exists(output) || File.Exists(output))
                throw new InvalidDataException("Product output exists before the build.");
        });
        string originals = Path.Combine(scratch, "original");
        Directory.CreateDirectory(originals);
        await qualification.CheckAsync("build", async () =>
        {
            foreach (var target in Outputs.Where(target => target.Script.Length != 0))
                await RunAsync("build", new("pnpm", checkout, ["--dir", product,
                    "--config.verify-deps-before-run=false", "run", target.Script], 600));
            foreach (var target in Outputs)
            {
                string name = $"{Unit}-{projected.Npm}-{target.Suffix}.zip";
                string source = Path.Combine(output, name);
                OwnedFile.RequireExists(source, "Required npm output is missing or not a file: "
                    + source);
                File.Copy(source, Path.Combine(originals, name), overwrite: false);
            }
        });
        var originalsByOutput = new Dictionary<string, (string Path, long Size, string Sha256)>(
            StringComparer.Ordinal);
        await qualification.CheckAsync("contents", async () =>
        {
            foreach (var target in Outputs)
            {
                string relative = $"original/{Unit}-{projected.Npm}-{target.Suffix}.zip";
                string path = Path.Combine(scratch, relative);
                (long size, string sha256) = await OwnedFile.MeasureAsync(path, token,
                    emptyMessage: "Native browser extension output is empty.");
                originalsByOutput.Add(target.Output, (relative, size, sha256));
                if (target.Manifest == 0)
                    BrowserExtensionArchive.ValidateSources(path, Product);
                else
                    BrowserExtensionArchive.ValidateMembers(path);
            }
        });
        // A store loads the zip root as an unpacked extension: its manifest must carry the
        // NBGV projection and resolve every declared file without the source checkout.
        await qualification.CheckAsync("consumer", () =>
        {
            var expected = new BrowserExtensionExpectation(ManifestName, projected.Manifest,
                projected.Npm);
            foreach (var target in Outputs.Where(target => target.Manifest != 0))
                BrowserExtensionArchive.ValidateExtension(Path.Combine(scratch,
                    originalsByOutput[target.Output].Path), target.Manifest, expected);
            return Task.CompletedTask;
        });
        await qualification.CheckAsync("original-bytes", async () =>
        {
            foreach (string selected in StoreOutputs(request.Subject.Destination))
            {
                (string path, long size, string sha256) = originalsByOutput[selected];
                await OwnedFile.VerifyAsync(Path.Combine(scratch, path), size, sha256,
                    "Qualified original changed during checks.", token);
                files.Add(new(build.Id, selected, Outputs.Single(target =>
                    target.Output == selected).Kind, Unit, projected.Manifest, path, size,
                    sha256));
            }
        });
        return new(new(request.Subject, [.. files], Required.ToArray(), qualification.Checks),
            request.Target, qualification.Commands, qualification.Errors);

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
