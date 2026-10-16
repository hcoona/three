using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Release;

internal sealed record ReleaseSubject(string Revision, string SelectedRef, string Unit,
    string Declaration, string Channel, string Destination, bool DryRun);

internal sealed record ReleaseFile(string Build, string Output, string Kind, string Name,
    string Version, string Path, long Size, string Sha256);

internal enum ReleaseCheckStatus { Passed, Failed, Missing, Cancelled, Skipped, TimedOut }
internal sealed record ReleaseCheck(string Id, ReleaseCheckStatus Status);
internal sealed record PreparedRelease(ReleaseSubject Subject, ReleaseFile[] Files,
    string[] RequiredChecks, ReleaseCheck[] Checks);

internal enum ReleaseFileState { Absent, Exact, Conflict, Unknown }
internal sealed record ReleaseFileObservation(string Output, string Name, string Version,
    ReleaseFileState State, long? Size, string? Sha256);
internal sealed record ReleaseObservation(ReleaseSubject Subject,
    ReleaseFileObservation[] Files, string[] RequiredChecks, ReleaseCheck[] Checks);
internal sealed record ReleaseUpload(string Output, NativeCommandResult Command);

internal enum ReleaseBusinessState { Satisfied, Unsatisfied, Unknown }
internal sealed record ReleaseOutcome(ReleaseSubject Subject, ReleaseBusinessState State,
    ReleaseUpload[] Uploads, string[] Reasons);
internal sealed record ReleaseDryRun(ReleaseSubject Subject, bool Ready,
    string[] HypotheticalWrites, string[] Reasons);

// Application-owned Release request policy. Live ref eligibility is checked where the
// request is formed and again by the privileged publisher; dry runs have no write capability.
internal static class ReleasePolicy
{
    internal const string OfficialRef = "refs/heads/main";
    private const string Branches = "refs/heads/";

    internal static void RequireEligible(ReleaseSubject subject)
    {
        if (subject.DryRun) return;
        if (!(subject.Channel == "official" ? subject.SelectedRef == OfficialRef :
                subject.SelectedRef.StartsWith(Branches, StringComparison.Ordinal) &&
                    subject.SelectedRef.Length > Branches.Length))
            throw new InvalidDataException("Unsupported live Release channel ref.");
    }

    internal static void RequireQualified(PreparedRelease prepared)
    {
        if (prepared.RequiredChecks.Any(id => !prepared.Checks.Any(check => check.Id == id &&
                check.Status == ReleaseCheckStatus.Passed)))
            throw new InvalidDataException("Release preparation is not qualified.");
    }
}

// Destination adapters supply their supported missing operations; the core never
// infers a multi-file transaction or a missing-file recovery capability.
internal static class ReleaseCompletion
{
    internal static ReleaseOutcome Evaluate(PreparedRelease prepared,
        ReleaseObservation? observation, ReleaseUpload[] uploads)
    {
        Validate(prepared);
        if (prepared.Subject.DryRun)
            throw new InvalidDataException("Dry run cannot produce a live completion result.");
        ValidateUploads(prepared, uploads);
        (ReleaseBusinessState state, string[] reasons) = Assess(prepared, observation);
        if (state == ReleaseBusinessState.Unsatisfied && observation is not null)
            reasons = [.. reasons, .. observation.Files.Where(file =>
                file.State == ReleaseFileState.Absent).Select(file =>
                    $"Required destination file is absent: {file.Output}.")];
        return new(prepared.Subject, state, uploads.ToArray(), reasons);
    }

    internal static ReleaseDryRun Describe(PreparedRelease prepared,
        ReleaseObservation? observation, string[] supportedMissingWrites)
    {
        Validate(prepared);
        if (!prepared.Subject.DryRun)
            throw new InvalidDataException("Hypothetical work requires a dry-run request.");
        (ReleaseBusinessState state, string[] reasons) = Assess(prepared, observation);
        if (state == ReleaseBusinessState.Unknown || reasons.Length != 0)
            return new(prepared.Subject, false, [], reasons);
        string[] missing = observation!.Files.Where(file => file.State == ReleaseFileState.Absent)
            .Select(file => file.Output).ToArray();
        if (supportedMissingWrites.Distinct(StringComparer.Ordinal).Count() !=
                supportedMissingWrites.Length ||
            !missing.Order(StringComparer.Ordinal).SequenceEqual(
                supportedMissingWrites.Order(StringComparer.Ordinal)))
            return new(prepared.Subject, false, [],
                ["Destination cannot complete the missing set."]);
        return new(prepared.Subject, true, supportedMissingWrites.ToArray(), []);
    }

    internal static void Validate(PreparedRelease prepared)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        ReleaseSubject subject = prepared.Subject;
        if (subject.Revision.Length != 40 ||
            subject.Revision.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f')) ||
            new[] { subject.SelectedRef, subject.Unit, subject.Channel, subject.Destination }
                .Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException("Incomplete release request subject.");
        RelativePath(subject.Declaration);
        if (prepared.Files.Length == 0 ||
            prepared.Files.Select(file => file.Output).Distinct(StringComparer.Ordinal).Count() !=
                prepared.Files.Length ||
            prepared.Files.Select(file => file.Path).Distinct(StringComparer.Ordinal).Count() !=
                prepared.Files.Length ||
            prepared.Files.Select(file => (file.Kind, file.Name, file.Version))
                .Distinct().Count() != prepared.Files.Length)
            throw new InvalidDataException("Incomplete or ambiguous original release set.");
        foreach (ReleaseFile file in prepared.Files)
        {
            if (new[] { file.Build, file.Output, file.Kind, file.Name, file.Version }
                    .Any(string.IsNullOrWhiteSpace) || file.Size <= 0 || !Digest(file.Sha256))
                throw new InvalidDataException("Incomplete original release file identity.");
            RelativePath(file.Path);
        }
        ValidateChecks(prepared.RequiredChecks, prepared.Checks);
    }

    private static (ReleaseBusinessState, string[]) Assess(PreparedRelease prepared,
        ReleaseObservation? observation)
    {
        var reasons = CheckFailures(prepared.RequiredChecks, prepared.Checks);
        if (observation is null)
        {
            reasons.Add("Required destination observation is missing; effects remain unknown.");
            return (ReleaseBusinessState.Unknown, [.. reasons]);
        }
        if (observation.Subject != prepared.Subject)
            throw new InvalidDataException("Destination observation belongs to another request.");
        ValidateChecks(observation.RequiredChecks, observation.Checks);
        reasons.AddRange(CheckFailures(observation.RequiredChecks, observation.Checks));
        if (observation.Files.Length != prepared.Files.Length ||
            observation.Files.Select(file => file.Output).Distinct(StringComparer.Ordinal).Count()
                != observation.Files.Length)
            throw new InvalidDataException("Destination observation must cover the complete set.");
        bool unknown = false;
        bool absent = false;
        foreach (ReleaseFile file in prepared.Files)
        {
            ReleaseFileObservation actual = observation.Files.SingleOrDefault(value =>
                value.Output == file.Output)
                ?? throw new InvalidDataException("Missing original output observation.");
            if (actual.Name != file.Name || actual.Version != file.Version ||
                !Enum.IsDefined(actual.State))
                throw new InvalidDataException("Mismatched native output observation.");
            switch (actual.State)
            {
                case ReleaseFileState.Exact:
                    if (actual.Size != file.Size || actual.Sha256 != file.Sha256)
                        throw new InvalidDataException("Exact state differs from original bytes.");
                    break;
                case ReleaseFileState.Absent:
                    absent = true;
                    break;
                case ReleaseFileState.Unknown:
                    unknown = true;
                    reasons.Add($"Destination state is unknown for {file.Output}.");
                    break;
                case ReleaseFileState.Conflict:
                    reasons.Add($"Destination state is {actual.State} for {file.Output}.");
                    break;
            }
        }
        if (unknown) return (ReleaseBusinessState.Unknown, [.. reasons]);
        if (reasons.Count != 0 || absent) return (ReleaseBusinessState.Unsatisfied, [.. reasons]);
        return (ReleaseBusinessState.Satisfied, []);
    }

    private static void ValidateUploads(PreparedRelease prepared, ReleaseUpload[] uploads)
    {
        var attempted = new HashSet<string>(StringComparer.Ordinal);
        bool stopped = false;
        foreach (ReleaseUpload upload in uploads)
        {
            if (stopped || !attempted.Add(upload.Output) ||
                !prepared.Files.Any(file => file.Output == upload.Output))
                throw new InvalidDataException("Unsupported repeated or post-ambiguity write.");
            stopped = !upload.Command.Succeeded;
        }
    }

    private static void ValidateChecks(string[] required, ReleaseCheck[] results)
    {
        if (required.Length == 0 || required.Any(string.IsNullOrWhiteSpace) ||
            required.Distinct(StringComparer.Ordinal).Count() != required.Length ||
            results.Select(check => check.Id).Distinct(StringComparer.Ordinal).Count() !=
                results.Length || results.Any(check => !required.Contains(check.Id,
                    StringComparer.Ordinal) || !Enum.IsDefined(check.Status)))
            throw new InvalidDataException("Ambiguous required release checks.");
    }

    private static List<string> CheckFailures(string[] required, ReleaseCheck[] results) =>
        required.Where(id => !results.Any(check => check.Id == id &&
                check.Status == ReleaseCheckStatus.Passed))
            .Select(id => $"Required check is not passed: {id}.").ToList();

    private static bool Digest(string value) => value.Length == 64 &&
        value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static void RelativePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains('\\') || value.Contains(':') ||
            value.Split('/').Any(part => part is "" or "." or ".."))
            throw new InvalidDataException("Expected a relative release transfer path.");
    }
}
