using WorkflowDelivery.Ecosystems;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Release;

namespace WorkflowDelivery.Destinations;

internal static class ReleaseAdapters
{
    internal static readonly NuGetDestinationAdapter NuGet = new();
    internal static readonly NpmDestinationAdapter Npm = new();
    internal static readonly PythonDestinationAdapter Python = new();
    internal static readonly RubyDestinationAdapter Ruby = new();
    internal static readonly GithubReleaseDestinationAdapter GithubRelease = new();
    internal static readonly BrowserStoreDestinationAdapter BrowserStore = new();

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
    public DestinationProfile Profile(ReleaseSubject subject) => new(
        subject.Destination == "nugetorg"
            ? DestinationVerificationMode.VersionAvailable
            : DestinationVerificationMode.ExactBytes,
        DestinationVisibilityMode.BoundedSettle);

    public void ValidatePreparation(ReleasePreparationResult preparation)
    {
        PreparedRelease prepared = preparation.Prepared;
        ReleaseCompletion.Validate(prepared);
        NuGetReleaseUnit unit = NuGetReleaseUnits.Admit(prepared.Subject);
        ReleasePolicy.RequireEligible(prepared.Subject);
        if (prepared.Files is not [{ Kind: "nuget-package" }] ||
            !prepared.RequiredChecks.SequenceEqual(NuGetPreparation.Required) ||
            !ReleasePolicy.Qualified(prepared.RequiredChecks, prepared.Checks) ||
            preparation.Errors.Length != 0 || preparation.Commands.Length == 0 ||
            preparation.Commands.Any(command => !command.Result.Succeeded))
            throw new InvalidDataException("Incomplete or unsupported NuGet preparation.");
        DotNetPackageResults.ValidateExpectations(unit.Library ? "" : unit.Frameworks[0], "",
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
        DotNetPackageArchive.RequireIdentity(DotNetPackageArchive.Identity(original.Path),
            preparation.Native);
        return original;
    }

    public void ValidateObservation(ReleasePreparationResult preparation,
        ReleaseObservationResult result)
    {
        PreparedRelease prepared = preparation.Prepared;
        bool github = prepared.Subject.Destination == "github-packages-nuget";
        if (!result.Observation.RequiredChecks.SequenceEqual(github
                ? GithubNuGetObservation.RequiredChecks : NuGetOrgObservation.RequiredChecks) ||
            result.Resources is { } resources && resources.PackagePublish != (github
                ? NuGetPublisher.GithubEndpoint : NuGetPublisher.NuGetOrgEndpoint) ||
            ReleasePolicy.Qualified(result.Observation.RequiredChecks, result.Observation.Checks)
                && result.Resources is null)
            throw new InvalidDataException("Incomplete native NuGet observation.");
        ReleaseCompletion.ValidateObservation(prepared, result.Observation,
            Profile(prepared.Subject));
    }

    internal Task<ReleasePublicationResult> PublishAsync(ReleasePreparationResult preparation,
        string originals, string credential,
        Func<PreparedRelease, string, CancellationToken, Task<ReleaseObservationResult>> observe,
        Func<PreparedRelease, string, string, string, CancellationToken,
            Task<NuGetPushResult>> push,
        CancellationToken token)
    {
        ReleaseObservationResult? latestBefore = null;
        return ReleaseEngine.PublishAsync(this, preparation, originals, null, new(
            ObserveAsync: async (prepared, cancel) =>
                latestBefore = await observe(prepared, credential, cancel),
            WriteOutputAsync: async (file, cancel) => ReleaseWriteStep.FromPush(file.Output,
                await push(preparation.Prepared, originals,
                    latestBefore!.Resources!.PackagePublish, credential, cancel)),
            Readback: DestinationReadbackMode.Required), token);
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
        _ = NpmReleaseUnits.Admit(prepared.Subject);
        ReleasePolicy.RequireQualified(prepared);
        if (preparation.Errors.Length != 0 ||
            prepared.Subject.Destination is not ("npmjs" or "github-packages-npm") ||
            prepared.Files is not [{ Kind: "npm-tarball" }])
            throw new InvalidDataException("Unqualified or unsupported npm preparation.");
    }

    public void ValidateObservation(ReleasePreparationResult preparation,
        ReleaseObservationResult observed) =>
        ReleaseCompletion.ValidateObservation(preparation.Prepared, observed.Observation,
            DefaultProfile);
}

internal sealed class PythonDestinationAdapter : IDestinationAdapter
{
    private static readonly string[] Formats = ["python-wheel", "python-sdist"];
    private static readonly DestinationProfile DefaultProfile = new(
        DestinationVerificationMode.ExactBytes,
        DestinationVisibilityMode.EventualIndex,
        OrderedKinds: Formats);

    public DestinationProfile Profile(ReleaseSubject subject) => DefaultProfile;

    public void ValidatePreparation(ReleasePreparationResult preparation)
    {
        PreparedRelease prepared = preparation.Prepared;
        ReleaseCompletion.Validate(prepared);
        ReleaseSubject subject = prepared.Subject;
        bool buddy = subject.Channel == "buddy" && subject.Destination == "testpypi";
        bool official = subject.Channel == "official" && subject.Destination == "pypi";
        PythonReleaseUnit unit = PythonReleaseUnits.Admit(subject);
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
            official && !subject.DryRun && unit.Id != PythonReleaseUnits.Nbgv &&
                (!preparation.Native.TryGetValue("public-release", out string? release) ||
                    release != "true"))
            throw new InvalidDataException("Incomplete or unsupported Python preparation.");
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
        RubyReleaseUnit unit = RubyReleaseUnits.Admit(subject);
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
        OrderedKinds: Kinds,
        AllowsContainer: true);

    public DestinationProfile Profile(ReleaseSubject subject) => DefaultProfile;

    public void ValidatePreparation(ReleasePreparationResult preparation)
    {
        PreparedRelease prepared = preparation.Prepared;
        ReleaseCompletion.Validate(prepared);
        ReleaseSubject subject = prepared.Subject;
        bool official = GithubReleaseUnits.Admit(subject);
        string version = prepared.Files.Length == 0 ? "" : prepared.Files[0].Version;
        if (prepared.Files.Length != 2 || Kinds.Any(kind =>
                prepared.Files.Count(file => file.Kind == kind &&
                    file.Name == GithubReleaseUnits.Asset(kind, version)) != 1) ||
            prepared.Files.Any(file => file.Version != version) ||
            !prepared.RequiredChecks.SequenceEqual(GithubReleasePreparation.Required) ||
            !ReleasePolicy.Qualified(prepared.RequiredChecks, prepared.Checks) ||
            preparation.Errors.Length != 0 || preparation.Commands.Length == 0 ||
            preparation.Commands.Any(command => !command.Result.Succeeded ||
                command.Result.Error is not null) ||
            !Native(preparation, "name", subject.Unit) ||
            !Native(preparation, "version", version) ||
            !Native(preparation, "commit", subject.Revision) ||
            !Native(preparation, "tag", GithubReleaseUnits.Tag(subject, version)) ||
            official && !subject.DryRun && !Native(preparation, "public-release", "true"))
            throw new InvalidDataException("Incomplete or unsupported GitHub Release preparation.");
    }

    public void ValidateObservation(ReleasePreparationResult preparation,
        ReleaseObservationResult result)
    {
        if (!result.Observation.RequiredChecks.SequenceEqual([GithubReleaseDestination.Check]) ||
            result.ReleaseId is null && result.Observation.Files.Any(file =>
                file.State == ReleaseFileState.Exact))
            throw new InvalidDataException("Incomplete GitHub Release observation.");
        ReleaseCompletion.ValidateObservation(preparation.Prepared, result.Observation,
            DefaultProfile);
    }

    internal Task<ReleasePublicationResult> PublishAsync(ReleasePreparationResult preparation,
        string originals, ReleaseObservationResult initial,
        Func<PreparedRelease, CancellationToken, Task<ReleaseObservationResult>> observe,
        Func<PreparedRelease, CancellationToken, Task<(NativeCommandResult, long?)>> create,
        Func<long, ReleaseFile, string, CancellationToken, Task<NativeCommandResult>> upload,
        CancellationToken token)
    {
        long? releaseId = null;
        return ReleaseEngine.PublishAsync(this, preparation, originals, initial, new(
            ObserveAsync: async (prepared, cancel) =>
            {
                ReleaseObservationResult before = await observe(prepared, cancel);
                releaseId = before.ReleaseId;
                return before;
            },
            EnsureContainerAsync: async (prepared, before, cancel) =>
            {
                if (before.ReleaseId is not null) return null;
                cancel.ThrowIfCancellationRequested();
                (NativeCommandResult result, long? created) = await create(prepared, cancel);
                releaseId = created;
                return ReleaseWriteStep.FromContainer(result, created is not null);
            },
            WriteOutputAsync: async (file, cancel) =>
            {
                string path = OwnedPath.ResolveWithin(originals, file.Path,
                    "Original GitHub Release directory must be absolute.",
                    "Original GitHub Release path leaves its root.");
                NativeCommandResult result = await upload(releaseId!.Value, file, path, cancel);
                return ReleaseWriteStep.FromUpload(new(file.Output, result));
            }), token);
    }

    private static bool Native(ReleasePreparationResult preparation, string key, string expected) =>
        preparation.Native.TryGetValue(key, out string? value) && value == expected;
}

internal sealed class BrowserStoreDestinationAdapter : IDestinationAdapter
{
    public DestinationProfile Profile(ReleaseSubject subject) => new(
        DestinationVerificationMode.StoreAccepted,
        PublisherOnlyRead: subject.Destination != BrowserStorePreparation.ChromeWebStore);

    public void ValidatePreparation(ReleasePreparationResult preparation)
    {
        PreparedRelease prepared = preparation.Prepared;
        ReleaseCompletion.Validate(prepared);
        if (preparation.Target is not { } target)
            throw new InvalidDataException("Missing browser-store target.");
        BrowserStorePreparation.RequireRequest(prepared.Subject, target);
        ReleasePolicy.RequireQualified(prepared);
        string version = prepared.Files[0].Version;
        if (preparation.Errors.Length != 0 || preparation.Commands.Length == 0 ||
            preparation.Commands.Any(command => !command.Result.Succeeded ||
                command.Result.Error is not null) ||
            !prepared.Files.Select(file => file.Output).Order(StringComparer.Ordinal)
                .SequenceEqual(BrowserStorePreparation.StoreOutputs(prepared.Subject.Destination)
                    .Order(StringComparer.Ordinal)) ||
            prepared.Files.Any(file => file.Name != BrowserStorePreparation.Unit ||
                file.Version != version) || !StoreHttp.ManifestVersion(version))
            throw new InvalidDataException("Incomplete or unsupported browser preparation.");
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
                    preparation.Target!.Store == BrowserStorePreparation.ChromeWebStore))
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

    internal static ReleaseObservationResult Unobserved(ReleasePreparationResult preparation) =>
        StoreHttp.Observation(preparation.Prepared.Subject, preparation.Target!,
            preparation.Prepared.Files[0].Version, StoreState.Unobserved,
            [preparation.Prepared.Subject.DryRun
                ? ReleaseEngine.StoreNotObservedInDryRun
                : "Only the credentialed publisher can read this store."], []);

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
        IBrowserStore store, CancellationToken token) =>
        ReleaseEngine.PublishAsync(this, preparation, originalsDirectory, initial, new(
            ObserveAsync: (_, cancel) => store.ObserveAsync(cancel),
            SubmitStoreAsync: (steps, cancel) => store.PublishAsync(files, steps, cancel)),
            token);
}
