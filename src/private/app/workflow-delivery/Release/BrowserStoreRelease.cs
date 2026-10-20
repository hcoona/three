using System.Globalization;
using WorkflowDelivery.Destinations;
using WorkflowDelivery.Ecosystems;

namespace WorkflowDelivery.Release;

internal sealed record BrowserStorePublicationResult(ReleaseSubject Subject,
    StoreObservation Before, StoreOperation[] Operations, StoreObservation? Final,
    string[] Errors);
internal sealed record BrowserStoreCompletionResult(ReleaseOutcome Outcome, StoreState State,
    bool MutationAttempted, BrowserStorePublicationResult? Publication);

// Store completion is the store API reporting the exact submitted version as received, in
// review or published (WD-REL-009). Review and publication timing remain store-owned.
internal static class BrowserStoreRelease
{
    // The single check of a received preparation in each job.
    internal static void Validate(BrowserStorePreparationResult preparation)
    {
        PreparedRelease prepared = preparation.Prepared;
        ReleaseCompletion.Validate(prepared);
        BrowserStorePreparation.RequireRequest(prepared.Subject, preparation.Target);
        ReleasePolicy.RequireQualified(prepared);
        string version = prepared.Files[0].Version;
        if (preparation.Errors.Length != 0 || preparation.Commands.Length == 0 ||
            preparation.Commands.Any(command => !command.Result.Succeeded ||
                command.Result.Error is not null) ||
            !prepared.Files.Select(file => file.Output).Order(StringComparer.Ordinal)
                .SequenceEqual(BrowserStorePreparation.StoreOutputs(prepared.Subject.Destination)
                    .Order(StringComparer.Ordinal)) ||
            prepared.Files.Any(file => file.Name != BrowserStorePreparation.Unit ||
                file.Version != version) || !ManifestVersion(version))
            throw new InvalidDataException("Incomplete or unsupported browser preparation.");
    }

    internal static async Task<StoreFile[]> OriginalsAsync(
        BrowserStorePreparationResult preparation, string directory, CancellationToken token)
    {
        if (!Path.IsPathFullyQualified(directory))
            throw new InvalidDataException("Original extension directory must be absolute.");
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        var files = new List<StoreFile>();
        foreach (ReleaseFile file in preparation.Prepared.Files)
        {
            string path = Path.GetFullPath(Path.Combine(root, file.Path));
            if (path == root || !Platform.OwnedPath.Within(path, root))
                throw new InvalidDataException("Original extension path leaves its root.");
            (long size, string sha256) = await NpmPackageOperations.OriginalAsync(path, token);
            if (size != file.Size || sha256 != file.Sha256)
                throw new InvalidDataException("Transferred original differs from preparation.");
            files.Add(new(file.Output, path));
        }
        return [.. files];
    }

    internal static StoreObservation Unobserved(BrowserStorePreparationResult preparation) =>
        new(preparation.Prepared.Subject, preparation.Target,
            preparation.Prepared.Files[0].Version, StoreState.Unobserved,
            ["Only the credentialed publisher can read this store."], []);

    internal static bool Satisfied(StoreState state) =>
        state is StoreState.Received or StoreState.InReview or StoreState.Published;

    internal static void ValidateObservation(BrowserStorePreparationResult preparation,
        StoreObservation observation)
    {
        if (observation.Subject != preparation.Prepared.Subject ||
            observation.Target != preparation.Target ||
            observation.Version != preparation.Prepared.Files[0].Version ||
            observation.State == StoreState.Unobserved &&
                preparation.Target.Store == BrowserStorePreparation.ChromeWebStore)
            throw new InvalidDataException("Store observation is not for this request.");
    }

    // A read store needs a write only when the exact version is absent. Stores readable
    // only by the credentialed publisher always route a live request through the gate.
    internal static bool WriteNeeded(BrowserStorePreparationResult preparation,
        StoreObservation observation)
    {
        ValidateObservation(preparation, observation);
        return observation.State is StoreState.Absent or StoreState.Unobserved;
    }

    internal static ReleaseDryRun Describe(BrowserStorePreparationResult preparation,
        StoreObservation observation)
    {
        Validate(preparation);
        ValidateObservation(preparation, observation);
        ReleaseSubject subject = preparation.Prepared.Subject;
        if (!subject.DryRun)
            throw new InvalidDataException("Hypothetical work requires a dry-run request.");
        string[] writes = subject.Destination switch
        {
            BrowserStorePreparation.ChromeWebStore => ["upload chrome-zip", "publish"],
            BrowserStorePreparation.FirefoxAmo =>
                ["upload firefox-zip", "create version with firefox-sources"],
            _ => ["upload edge-zip", "submit"],
        };
        return observation.State switch
        {
            StoreState.Absent => new(subject, true, writes, []),
            StoreState.Unobserved => new(subject, true, writes,
                ["Store state is unobserved; the publisher observes it before submission."]),
            _ when Satisfied(observation.State) => new(subject, true, [],
                ["The store already reports this version: " + observation.State + "."]),
            _ => new(subject, false, [], observation.Reasons.Length == 0
                ? ["Store state: " + observation.State + "."] : observation.Reasons),
        };
    }

    // The publisher re-observes with its own credential and submits only an absent or
    // unobserved version, so a satisfied or conflicting store is never written.
    internal static async Task<BrowserStorePublicationResult> PublishAsync(
        BrowserStorePreparationResult preparation, StoreFile[] files, StoreObservation initial,
        IBrowserStore store, CancellationToken token)
    {
        Validate(preparation);
        ValidateObservation(preparation, initial);
        ReleaseSubject subject = preparation.Prepared.Subject;
        if (subject.DryRun)
            throw new InvalidDataException("A dry run cannot publish.");
        ReleasePolicy.RequireEligible(subject);
        StoreObservation before = await store.ObserveAsync(token);
        ValidateObservation(preparation, before);
        var operations = new List<StoreOperation>();
        if (before.State is not (StoreState.Absent or StoreState.Unobserved))
            return new(subject, before, [], Satisfied(before.State) ? before : null,
                Satisfied(before.State) ? [] : ["Store state does not permit submission: " +
                    before.State + "."]);
        StoreObservation? final;
        try { final = await store.PublishAsync(files, operations, token); }
        catch (Exception failure) when (failure is HttpRequestException or IOException or
            System.Text.Json.JsonException or InvalidDataException or
            OperationCanceledException)
        {
            operations.Add(new("submission", false, "Store submission did not complete."));
            final = null;
        }
        if (final is not null) ValidateObservation(preparation, final);
        string[] errors = operations.Where(operation => !operation.Succeeded)
            .Select(operation => operation.Operation + ": " + operation.Detail).ToArray();
        return new(subject, before, [.. operations], final, errors);
    }

    // The final observation is a separate read when the store is readable outside the
    // publisher; otherwise it is the publisher's post-submission observation.
    internal static BrowserStoreCompletionResult Complete(
        BrowserStorePreparationResult preparation, StoreObservation initial,
        StoreObservation? final, BrowserStorePublicationResult? publication)
    {
        Validate(preparation);
        ValidateObservation(preparation, initial);
        ReleaseSubject subject = preparation.Prepared.Subject;
        if (subject.DryRun)
            throw new InvalidDataException("Dry run cannot produce a live completion result.");
        if (publication is not null && publication.Subject != subject)
            throw new InvalidDataException("Publication is not for this request.");
        final ??= publication is null ? initial : publication.Final;
        if (final is not null) ValidateObservation(preparation, final);
        bool attempted = publication?.Operations.Length > 0;
        StoreState state = final?.State ?? StoreState.Unknown;
        ReleaseBusinessState business = Satisfied(state) ? ReleaseBusinessState.Satisfied
            : state is StoreState.Unknown or StoreState.Unobserved
                ? ReleaseBusinessState.Unknown : ReleaseBusinessState.Unsatisfied;
        string[] reasons = business == ReleaseBusinessState.Satisfied ? [] :
            [.. final?.Reasons ?? [], .. publication?.Errors ?? [],
                "Store state: " + state + "."];
        return new(new(subject, business, [], reasons), state, attempted, publication);
    }

    private static bool ManifestVersion(string version)
    {
        string[] parts = version.Split('.');
        return parts.Length is >= 1 and <= 4 && parts.All(part => part.Length is >= 1 and
            <= 5 && part.All(char.IsAsciiDigit) && (part == "0" || part[0] != '0') &&
            int.Parse(part, CultureInfo.InvariantCulture) <= 65535);
    }
}
