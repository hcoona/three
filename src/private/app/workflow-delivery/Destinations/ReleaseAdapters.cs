using WorkflowDelivery.Core;
using WorkflowDelivery.Ecosystems;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Release;

namespace WorkflowDelivery.Destinations;

internal static class ReleaseAdapters
{
    // The standalone single-observation API predates an explicit bound publication profile.
    internal static DestinationProfile ObservationProfile(ReleaseSubject subject) =>
        subject.Destination == "nugetorg"
            ? NuGet.Profile(subject)
            : new(DestinationVerificationMode.ExactBytes);

    internal static readonly NuGetDestinationAdapter NuGet = new();
    internal static readonly NpmDestinationAdapter Npm = new();
    internal static readonly PythonDestinationAdapter Python = new();
    internal static readonly RubyDestinationAdapter Ruby = new();
    internal static readonly GithubReleaseDestinationAdapter GithubRelease = new();
    internal static readonly GithubAnchorDestinationAdapter GithubAnchor = new();
    internal static readonly BrowserStoreDestinationAdapter BrowserStore = new();

    internal static ReleasePreparationResult AnchorPreparation(
        ReleasePreparationResult preparation)
    {
        PreparedGithubAnchor anchor = preparation.Prepared.Anchor
            ?? throw new InvalidDataException(
                "Release preparation carries no GitHub Release anchor.");
        var anchorPrepared = new PreparedRelease(
            preparation.Prepared.Subject,
            anchor.Assets,
            [GithubReleaseDestination.Check],
            [new(GithubReleaseDestination.Check, ReleaseCheckStatus.Passed)],
            ReleaseNotes: anchor.ReleaseNotes);
        return new(anchorPrepared, preparation.Native, preparation.Commands, preparation.Errors,
            preparation.Target);
    }

    internal static void ValidateOptionalAnchor(ReleasePreparationResult preparation)
    {
        PreparedRelease prepared = preparation.Prepared;
        if (prepared.Anchor is not { } anchor) return;
        PreparedGithubAnchor expected = DestinationBindings.GithubAnchor(prepared.Subject,
            prepared.Files, anchor.ReleaseNotes, anchor.Assets.FirstOrDefault()?.Version);
        if (!anchor.Assets.SequenceEqual(expected.Assets) ||
            !(anchor.AllowedSiblingAssets ?? []).Order(StringComparer.Ordinal)
                .SequenceEqual((expected.AllowedSiblingAssets ?? []).Order(StringComparer.Ordinal)))
            throw new InvalidDataException(
                "GitHub Release anchor differs from the qualified originals.");
        GithubAnchor.ValidatePreparation(AnchorPreparation(preparation));
    }

    internal static void ValidateGithubObservation(PreparedRelease prepared,
        ReleaseObservationResult result, DestinationProfile profile)
    {
        if (!result.Observation.RequiredChecks.SequenceEqual([GithubReleaseDestination.Check]) ||
            result.Container is null ||
            result.Container == ContainerPresence.Missing &&
                result.Observation.Files.Any(file => file.State != ReleaseFileState.Absent) ||
            result.Container == ContainerPresence.Unknown &&
                (result.Observation.Files.Any(file => file.State is ReleaseFileState.Exact or
                    ReleaseFileState.Absent) || result.Observation.Checks.Any(check =>
                        check.Status == ReleaseCheckStatus.Passed)))
            throw new InvalidDataException("Incomplete GitHub Release observation.");
        ReleaseCompletion.ValidateObservation(prepared, result.Observation, profile);
    }

    internal static Task<ReleasePublicationResult> PublishGithubAsync(IDestinationAdapter adapter,
        ReleasePreparationResult preparation, string originals, ReleaseObservationResult initial,
        GithubReleaseDestination destination, IReadOnlyCollection<string>? allowedSiblingAssets,
        CancellationToken token)
    {
        var publisher = destination.OpenPublisher(DestinationBindings.GithubRelease(
            preparation.Prepared, allowedSiblingAssets), originals);
        return ReleaseEngine.PublishAsync(adapter, preparation, originals, initial,
            DestinationPublishPlan.GithubRelease(preparation.Prepared, publisher), token);
    }

    internal static IDestinationAdapter ForEcosystem(string ecosystem) => ecosystem switch
    {
        "nuget" => NuGet,
        "npm" => Npm,
        "python" => Python,
        "ruby" => Ruby,
        "github-release" => GithubRelease,
        "browser-store" => BrowserStore,
        _ => throw new InvalidDataException("Unsupported Release caller ecosystem."),
    };
}

internal sealed class NuGetDestinationAdapter : IDestinationAdapter
{
    public PublicationBinding BindPublication(ReleasePreparationResult preparation) =>
        PublicationBinding.Files(this, preparation, "push");

    public DestinationProfile Profile(ReleaseSubject subject) => new(
        subject.Destination == "nugetorg"
            ? DestinationVerificationMode.VersionAvailable
            : DestinationVerificationMode.ExactBytes,
        DestinationVisibilityMode.BoundedSettle);

    public void ValidatePreparation(ReleasePreparationResult preparation)
    {
        PreparedRelease prepared = preparation.Prepared;
        ReleaseCompletion.Validate(prepared);
        NuGetReleaseUnit unit = ReleaseUnitCatalog.AdmitNuGet(prepared.Subject);
        ReleasePolicy.RequireEligible(prepared.Subject);
        if (prepared.Anchor is not null || prepared.Files is not [{ Kind: "nuget-package" }] ||
            !prepared.RequiredChecks.SequenceEqual(NuGetPreparation.Required) ||
            !ReleasePolicy.Qualified(prepared.RequiredChecks, prepared.Checks) ||
            preparation.Errors.Length != 0 || preparation.Commands.Length == 0 ||
            preparation.Commands.Any(command => !command.Result.Succeeded))
            throw new InvalidDataException("Incomplete or unsupported NuGet preparation.");
        string expectedFramework = unit.Library ? ""
            : preparation.Native.TryGetValue("TargetFramework", out string? tfm)
                && !string.IsNullOrWhiteSpace(tfm) ? tfm : "net10.0";
        DotNetPackageResults.ValidateExpectations(expectedFramework, "",
            preparation.Native);
        ReleaseFile file = prepared.Files[0];
        if (file.Name != preparation.Native["PackageId"] ||
            file.Version != preparation.Native["NuGetPackageVersion"] ||
            file.Name != unit.PackageId)
            throw new InvalidDataException("NuGet file differs from native preparation.");
    }

    public void VerifyOriginals(ReleasePreparationResult preparation, string originalsDirectory) =>
        _ = Original(preparation, originalsDirectory);

    internal static DotNetOriginalPackage Original(ReleasePreparationResult preparation,
        string directory)
    {
        if (!Path.IsPathFullyQualified(directory))
            throw new InvalidDataException("Original NuGet directory must be absolute.");
        PreparedRelease prepared = preparation.Prepared;
        ReleaseFile file = prepared.Files[0];
        var original = new DotNetOriginalPackage(prepared.Subject.Unit, file.Build, file.Output,
            "primary-package", file.Kind, Path.GetFullPath(Path.Combine(directory, file.Path)),
            file.Size, file.Sha256);
        DotNetPackageConsumer.RequireFile(original.Path);
        DotNetPackageArchive.RequireOriginal(original);
        DotNetPackageArchive.RequireArchiveIdentity(original.Path, preparation.Native);
        return original;
    }

    public void ValidateObservation(ReleasePreparationResult preparation,
        ReleaseObservationResult result)
    {
        PreparedRelease prepared = preparation.Prepared;
        bool github = prepared.Subject.Destination == "github-packages-nuget";
        if (!result.Observation.RequiredChecks.SequenceEqual(github
                ? GithubNuGetObservation.RequiredChecks : NuGetOrgObservation.RequiredChecks))
            throw new InvalidDataException("Incomplete native NuGet observation.");
        ReleaseCompletion.ValidateObservation(prepared, result.Observation,
            Profile(prepared.Subject));
    }

    internal Task<ReleasePublicationResult> PublishAsync(ReleasePreparationResult preparation,
        ReleaseObservationResult initial, string originals, string readCredential,
        string writeCredential,
        Func<PreparedRelease, string, CancellationToken, Task<ReleaseObservationResult>> observe,
        Func<PreparedRelease, string, string, string, CancellationToken,
            Task<NativeLibraryResult>> push,
        CancellationToken token)
    {
        string endpoint = preparation.Prepared.Subject.Destination == "nugetorg"
            ? NuGetPublisher.NuGetOrgEndpoint : NuGetPublisher.GithubEndpoint;
        return ReleaseEngine.PublishAsync(this, preparation, originals, initial,
            DestinationPublishPlan.Files(preparation.Prepared,
                (prepared, cancel) => observe(prepared, readCredential, cancel),
                async (file, cancel) => ReleaseWriteStep.FromPush(file.Output,
                    await push(preparation.Prepared, originals, endpoint, writeCredential, cancel)),
                DestinationReadbackMode.Required), token);
    }
}

internal sealed class NpmDestinationAdapter : IDestinationAdapter
{
    private static readonly DestinationProfile DefaultProfile = new(
        DestinationVerificationMode.ExactBytes);

    public DestinationProfile Profile(ReleaseSubject subject) => DefaultProfile;

    public void ValidatePreparation(ReleasePreparationResult preparation)
    {
        PreparedRelease prepared = preparation.Prepared;
        ReleaseCompletion.Validate(prepared);
        NpmReleaseUnit unit = ReleaseUnitCatalog.AdmitNpm(prepared.Subject);
        ReleasePolicy.RequireQualified(prepared);
        if (preparation.Errors.Length != 0 ||
            prepared.Subject.Destination is not ("npmjs" or "github-packages-npm") ||
            prepared.Files is not [{ Kind: "npm-tarball" } file] ||
            file.Name != unit.PackageName(prepared.Subject))
            throw new InvalidDataException("Unqualified or unsupported npm preparation.");
        ReleaseAdapters.ValidateOptionalAnchor(preparation);
    }

    public void ValidateObservation(ReleasePreparationResult preparation,
        ReleaseObservationResult observed) =>
        ReleaseCompletion.ValidateObservation(preparation.Prepared, observed.Observation,
            DefaultProfile);

    internal Task<ReleasePublicationResult> PublishNpmjsAsync(ReleasePreparationResult preparation,
        ReleaseObservationResult initial, string originals, string scratch, string toolchain,
        IReadOnlyDictionary<string, string?> platform,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        Func<PreparedRelease, CancellationToken, Task<ReleaseObservationResult>> observe,
        CancellationToken token) => PublishAsync(preparation, initial, originals,
            NpmjsPublication.Bind(DestinationBindings.Npm(preparation.Prepared), originals, scratch,
                toolchain, platform, execute), observe, token);

    internal Task<ReleasePublicationResult> PublishGithubAsync(ReleasePreparationResult preparation,
        ReleaseObservationResult initial, string originals, string scratch, string toolchain,
        string credential,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        Func<PreparedRelease, string, CancellationToken, Task<ReleaseObservationResult>> observe,
        CancellationToken token) => PublishAsync(preparation, initial, originals,
            GithubNpmPublication.Bind(DestinationBindings.Npm(preparation.Prepared), originals,
                scratch, toolchain, credential, execute),
            (prepared, cancel) => observe(prepared, credential, cancel), token);

    private Task<ReleasePublicationResult> PublishAsync(ReleasePreparationResult preparation,
        ReleaseObservationResult initial, string originals, NpmPublishSession publisher,
        Func<PreparedRelease, CancellationToken, Task<ReleaseObservationResult>> observe,
        CancellationToken token) =>
        ReleaseEngine.PublishAsync(this, preparation, originals, initial,
            DestinationPublishPlan.Files(preparation.Prepared, async (prepared, cancel) =>
            {
                await publisher.RequireRuntimeAsync(cancel);
                return await observe(prepared, cancel);
            }, publisher.PublishAsync, DestinationReadbackMode.Required), token);
}

internal sealed class PythonDestinationAdapter : IDestinationAdapter
{
    private static readonly string[] Formats = ["python-wheel", "python-sdist"];
    private static readonly DestinationProfile DefaultProfile = new(
        DestinationVerificationMode.ExactBytes,
        DestinationVisibilityMode.EventualRead,
        OrderedKinds: Formats);

    public DestinationProfile Profile(ReleaseSubject subject) => DefaultProfile;

    public string UnconfirmedOperation(OperationAttempt attempt) =>
        $"{(attempt.Acknowledged ? "Acknowledged" : "Attempted")} Python upload is not yet " +
        $"visible: {attempt.Operation.Target}.";

    public void ValidatePreparation(ReleasePreparationResult preparation)
    {
        PreparedRelease prepared = preparation.Prepared;
        ReleaseCompletion.Validate(prepared);
        ReleaseSubject subject = prepared.Subject;
        bool buddy = subject.Channel == "buddy" && subject.Destination == "testpypi";
        bool official = subject.Channel == "official" && subject.Destination == "pypi";
        PythonReleaseUnit unit = ReleaseUnitCatalog.AdmitPython(subject);
        if (!(buddy || official) ||
            prepared.Files.Length != 2 || Formats.Any(kind =>
                prepared.Files.Count(file => file.Kind == kind) != 1) ||
            prepared.Files.Any(file => file.Name != unit.Id ||
                file.Version != prepared.Files[0].Version) ||
            !prepared.RequiredChecks.SequenceEqual(PythonPreparation.RequiredFor(unit)) ||
            !ReleasePolicy.Qualified(prepared.RequiredChecks, prepared.Checks) ||
            preparation.Errors.Length != 0 || preparation.Commands.Length == 0 ||
            preparation.Commands.Any(command => !command.Result.Succeeded ||
                command.Result.Error is not null) ||
            !preparation.Native.TryGetValue("name", out string? name) || name != unit.Id ||
            !preparation.Native.TryGetValue("version", out string? version) ||
                version != prepared.Files[0].Version ||
            !preparation.Native.TryGetValue("commit", out string? commit) ||
                commit != subject.Revision ||
            official && !subject.DryRun && !unit.StaticVersion &&
                (!preparation.Native.TryGetValue("public-release", out string? release) ||
                    release != "true"))
            throw new InvalidDataException("Incomplete or unsupported Python preparation.");
        ReleaseAdapters.ValidateOptionalAnchor(preparation);
    }

    public void VerifyOriginals(ReleasePreparationResult preparation, string originalsDirectory)
    {
        foreach (ReleaseFile file in preparation.Prepared.Files)
        {
            string path = OwnedPath.ResolveWithin(originalsDirectory, file.Path,
                "Original Python directory must be absolute.",
                "Original Python path leaves its transfer root.");
            PythonPackageOperations.RequireOriginal(new(file.Kind, path, file.Size, file.Sha256));
        }
    }

    public void ValidateObservation(ReleasePreparationResult preparation,
        ReleaseObservationResult result)
    {
        if (!result.Observation.RequiredChecks.SequenceEqual(["public-index-files"]))
            throw new InvalidDataException("Incomplete Python index observation.");
        ReleaseCompletion.ValidateObservation(preparation.Prepared, result.Observation,
            DefaultProfile);
    }
}

internal sealed class RubyDestinationAdapter : IDestinationAdapter
{
    private static readonly DestinationProfile DefaultProfile = new(
        DestinationVerificationMode.ExactBytes);

    public DestinationProfile Profile(ReleaseSubject subject) => DefaultProfile;

    public void ValidatePreparation(ReleasePreparationResult preparation)
    {
        PreparedRelease prepared = preparation.Prepared;
        ReleaseCompletion.Validate(prepared);
        ReleaseSubject subject = prepared.Subject;
        bool buddy = subject.Channel == "buddy" &&
            subject.Destination == "github-packages-ruby";
        bool official = subject.Channel == "official" && subject.Destination == "rubygems";
        RubyReleaseUnit unit = ReleaseUnitCatalog.AdmitRuby(subject);
        string package = unit.Id;
        if (!(buddy || official) || prepared.Files is not
                [{ Kind: "ruby-gem", Name: var file }] || file != package ||
            !prepared.RequiredChecks.SequenceEqual(RubyPreparation.RequiredFor(unit)) ||
            !ReleasePolicy.Qualified(prepared.RequiredChecks, prepared.Checks) ||
            preparation.Errors.Length != 0 || preparation.Commands.Length == 0 ||
            preparation.Commands.Any(command => !command.Result.Succeeded ||
                command.Result.Error is not null) ||
            !preparation.Native.TryGetValue("name", out string? name) || name != package ||
            !preparation.Native.TryGetValue("version", out string? version) ||
                version != prepared.Files[0].Version ||
            !preparation.Native.TryGetValue("platform", out string? platform) ||
                platform != "ruby" ||
            !preparation.Native.TryGetValue("commit", out string? commit) ||
                commit != subject.Revision ||
            official && !subject.DryRun && (!preparation.Native.TryGetValue("public-release",
                out string? release) || release != "true"))
            throw new InvalidDataException("Incomplete or unsupported Ruby preparation.");
        ReleaseAdapters.ValidateOptionalAnchor(preparation);
    }

    public void VerifyOriginals(ReleasePreparationResult preparation, string originalsDirectory)
    {
        ReleaseFile file = preparation.Prepared.Files[0];
        string path = OwnedPath.ResolveWithin(originalsDirectory, file.Path,
            "Original Ruby directory must be absolute.",
            "Original Ruby path leaves its transfer root.");
        RubyPackageOperations.RequireOriginal(new(path, file.Size, file.Sha256));
    }

    public void ValidateObservation(ReleasePreparationResult preparation,
        ReleaseObservationResult result)
    {
        if (!result.Observation.RequiredChecks.SequenceEqual(["native-source-original"]))
            throw new InvalidDataException("Incomplete native Ruby observation.");
        ReleaseCompletion.ValidateObservation(preparation.Prepared, result.Observation,
            DefaultProfile);
    }
}

internal sealed class GithubReleaseDestinationAdapter : IDestinationAdapter
{
    private static readonly string[] Kinds = ["inno-installer", "portable-zip"];
    private static readonly DestinationProfile DefaultProfile = new(
        DestinationVerificationMode.ExactBytes,
        DestinationVisibilityMode.EventualRead,
        OrderedKinds: Kinds);

    public PublicationBinding BindPublication(ReleasePreparationResult preparation) =>
        PublicationBinding.Files(this, preparation, createRelease: true);

    public DestinationProfile Profile(ReleaseSubject subject) => DefaultProfile;

    public void ValidatePreparation(ReleasePreparationResult preparation)
    {
        PreparedRelease prepared = preparation.Prepared;
        ReleaseCompletion.Validate(prepared);
        ReleaseSubject subject = prepared.Subject;
        (GithubReleaseUnit unit, bool official) = ReleaseUnitCatalog.AdmitGithubRelease(subject);
        string version = prepared.Files.Length == 0 ? "" : prepared.Files[0].Version;
        if (prepared.Anchor is not null || prepared.Files.Length != 2 || Kinds.Any(kind =>
                prepared.Files.Count(file => file.Kind == kind &&
                    file.Name == unit.Asset(kind, version)) != 1) ||
            prepared.Files.Any(file => file.Version != version) ||
            !prepared.RequiredChecks.SequenceEqual(GithubReleasePreparation.Required) ||
            !ReleasePolicy.Qualified(prepared.RequiredChecks, prepared.Checks) ||
            preparation.Errors.Length != 0 || preparation.Commands.Length == 0 ||
            preparation.Commands.Any(command => !command.Result.Succeeded ||
                command.Result.Error is not null) ||
            !Native(preparation, "name", subject.Unit) ||
            !Native(preparation, "version", version) ||
            !Native(preparation, "commit", subject.Revision) ||
            !Native(preparation, "tag", GithubReleaseUnit.Tag(subject, version)) ||
            official && !subject.DryRun && !Native(preparation, "public-release", "true"))
            throw new InvalidDataException("Incomplete or unsupported GitHub Release preparation.");
    }

    public void ValidateObservation(ReleasePreparationResult preparation,
        ReleaseObservationResult result) =>
        ReleaseAdapters.ValidateGithubObservation(preparation.Prepared, result, DefaultProfile);

    internal Task<ReleasePublicationResult> PublishAsync(ReleasePreparationResult preparation,
        string originals, ReleaseObservationResult initial, GithubReleaseDestination destination,
        CancellationToken token) => ReleaseAdapters.PublishGithubAsync(this, preparation, originals,
            initial, destination, null, token);

    private static bool Native(ReleasePreparationResult preparation, string key, string expected) =>
        preparation.Native.TryGetValue(key, out string? value) && value == expected;
}

internal sealed class GithubAnchorDestinationAdapter : IDestinationAdapter
{
    private static readonly DestinationProfile DefaultProfile = new(
        DestinationVerificationMode.ExactBytes, DestinationVisibilityMode.EventualRead);

    public PublicationBinding BindPublication(ReleasePreparationResult preparation) =>
        PublicationBinding.Files(this, preparation, createRelease: true);

    public DestinationProfile Profile(ReleaseSubject subject) => DefaultProfile;

    public void ValidatePreparation(ReleasePreparationResult preparation)
    {
        PreparedRelease prepared = preparation.Prepared;
        ReleaseCompletion.Validate(prepared);
        if (prepared.Subject.Channel != "official" ||
            string.IsNullOrWhiteSpace(prepared.ReleaseNotes) ||
            preparation.Errors.Length != 0 ||
            prepared.Files.Any(file => file.Version != prepared.Files[0].Version) ||
            prepared.Files.Select(file => file.Name).Distinct(StringComparer.Ordinal).Count() !=
                prepared.Files.Length)
            throw new InvalidDataException(
                "Incomplete or unsupported GitHub Release anchor preparation.");
    }

    public void ValidateObservation(ReleasePreparationResult preparation,
        ReleaseObservationResult result) =>
        ReleaseAdapters.ValidateGithubObservation(preparation.Prepared, result, DefaultProfile);

    internal Task<ReleasePublicationResult> PublishAsync(ReleasePreparationResult anchorPreparation,
        string originals, ReleaseObservationResult initial,
        IReadOnlyCollection<string>? allowedSiblingAssets,
        GithubReleaseDestination destination, CancellationToken token) =>
        ReleaseAdapters.PublishGithubAsync(this, anchorPreparation, originals, initial, destination,
            allowedSiblingAssets, token);
}

internal sealed class BrowserStoreDestinationAdapter : IDestinationAdapter
{
    public PublicationBinding BindPublication(ReleasePreparationResult preparation) =>
        PublicationBinding.Store(
            this, preparation, SubmissionFor(preparation.Prepared.Subject.Destination));

    internal static string SubmissionFor(string store) => store switch
    {
        BrowserStorePreparation.ChromeWebStore => "publish",
        BrowserStorePreparation.FirefoxAmo => "create-version",
        BrowserStorePreparation.EdgeAddons => "submit",
        _ => throw new InvalidDataException("Unsupported store submission."),
    };

    public DestinationProfile Profile(ReleaseSubject subject) => new(
        DestinationVerificationMode.StoreAccepted,
        VersionRead: VersionReadForStore(subject.Destination) ?? DestinationVersionRead.BeforeGate);

    internal static DestinationVersionRead? VersionReadForStore(string? store) => store switch
    {
        BrowserStorePreparation.ChromeWebStore => DestinationVersionRead.BeforeGate,
        BrowserStorePreparation.FirefoxAmo => DestinationVersionRead.PublisherOnly,
        BrowserStorePreparation.EdgeAddons => DestinationVersionRead.Unavailable,
        _ => null,
    };

    public void ValidatePreparation(ReleasePreparationResult preparation)
    {
        PreparedRelease prepared = preparation.Prepared;
        ReleaseCompletion.Validate(prepared);
        if (preparation.Target is not { } target)
            throw new InvalidDataException("Missing browser-store target.");
        BrowserStoreUnit registered = BrowserStorePreparation.RequireRequest(prepared.Subject,
            target);
        ReleasePolicy.RequireQualified(prepared);
        string version = prepared.Files[0].Version;
        if (preparation.Errors.Length != 0 || preparation.Commands.Length == 0 ||
            preparation.Commands.Any(command => !command.Result.Succeeded ||
                command.Result.Error is not null) ||
            !prepared.Files.Select(file => file.Output).Order(StringComparer.Ordinal)
                .SequenceEqual(BrowserStorePreparation.StoreOutputs(prepared.Subject.Destination)
                    .Order(StringComparer.Ordinal)) ||
            prepared.Files.Any(file => file.Name != registered.Id ||
                file.Version != version) || !StoreHttp.ManifestVersion(version))
            throw new InvalidDataException("Incomplete or unsupported browser preparation.");
        ReleaseAdapters.ValidateOptionalAnchor(preparation);
    }

    public void ValidateObservation(ReleasePreparationResult preparation,
        ReleaseObservationResult observed)
    {
        PreparedRelease prepared = preparation.Prepared;
        if (observed.StoreTarget != preparation.Target ||
            observed.StoreState is not { } state ||
            !observed.Observation.RequiredChecks.SequenceEqual([StoreHttp.Check]) ||
            (prepared.Subject.DryRun
                ? state != StoreState.Unobserved
                : state == StoreState.Unobserved &&
                    VersionReadForStore(preparation.Target!.Store) ==
                        DestinationVersionRead.BeforeGate))
            throw new InvalidDataException("Store observation is not for this request.");
        ReleaseFileState expectedFileState = state switch
        {
            StoreState.Absent => ReleaseFileState.Absent,
            StoreState.Received or StoreState.InReview or StoreState.Published =>
                ReleaseFileState.Accepted,
            StoreState.Unobserved => ReleaseFileState.Unobserved,
            StoreState.Conflict => ReleaseFileState.Conflict,
            _ => ReleaseFileState.Unknown,
        };
        ReleaseCheckStatus expectedCheck = state is StoreState.Conflict or StoreState.Unknown
            ? ReleaseCheckStatus.Failed : ReleaseCheckStatus.Passed;
        if (observed.Observation.Files.Any(file => file.State != expectedFileState) ||
            observed.Observation.Checks is not [{ Id: StoreHttp.Check } check] ||
            check.Status != expectedCheck)
            throw new InvalidDataException("Contradictory browser-store observation state.");
        ReleaseCompletion.ValidateObservation(prepared, observed.Observation,
            Profile(prepared.Subject));
    }

    public string[] CustomDryRunWrites(ReleasePreparationResult preparation) =>
        preparation.Prepared.Subject.Destination switch
        {
            BrowserStorePreparation.ChromeWebStore => ["upload chrome-zip", "publish"],
            BrowserStorePreparation.FirefoxAmo =>
                ["upload firefox-zip", "create version with firefox-sources"],
            _ => ["upload edge-zip", "submit"],
        };

    internal static ReleaseObservationResult Unobserved(ReleasePreparationResult preparation)
    {
        ReleaseSubject subject = preparation.Prepared.Subject;
        BrowserStoreTarget target = preparation.Target!;
        string version = preparation.Prepared.Files[0].Version;
        if (!subject.DryRun && target.Store == BrowserStorePreparation.EdgeAddons)
            return EdgeAddons.Unobserved(subject, target, version);
        return StoreHttp.Observation(subject, target, version, StoreState.Unobserved,
            [subject.DryRun ? ReleaseEngine.StoreNotObservedInDryRun
                : "Only the credentialed publisher can read this store."], []);
    }

    internal static async Task<StoreFile[]> OriginalsAsync(ReleasePreparationResult preparation,
        string directory, CancellationToken token)
    {
        var files = new List<StoreFile>();
        foreach (ReleaseFile file in preparation.Prepared.Files)
        {
            string path = OwnedPath.ResolveWithin(directory, file.Path,
                "Original extension directory must be absolute.",
                "Original extension path leaves its root.");
            await OwnedFile.VerifyAsync(path, file.Size, file.Sha256,
                "Transferred original differs from preparation.", token);
            files.Add(new(file.Output, path));
        }
        return [.. files];
    }

    internal Task<ReleasePublicationResult> PublishAsync(ReleasePreparationResult preparation,
        string originalsDirectory, StoreFile[] files, ReleaseObservationResult initial,
        IBrowserStore store, CancellationToken token)
    {
        StoreWriter? writer = null;
        return ReleaseEngine.PublishAsync(this, preparation, originalsDirectory, initial,
            DestinationPublishPlan.Store((_, cancel) => store.ObserveAsync(cancel),
                SubmissionFor(preparation.Prepared.Subject.Destination),
                (op, cancel) => (writer ??= store.Bind(files))(op, cancel),
                preparation.Prepared.Subject.Destination == BrowserStorePreparation.FirefoxAmo,
                () => ReleaseEngine.VerifyOriginals(preparation.Prepared, originalsDirectory)),
            token);
    }
}
