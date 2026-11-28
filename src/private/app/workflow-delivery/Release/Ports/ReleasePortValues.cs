using System.Text.Json.Serialization;
using WorkflowDelivery.Core;
using WorkflowDelivery.Destinations;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Release;

internal sealed record ReleaseSubject(string Revision, string SelectedRef, string Unit,
    string Declaration, string Channel, string Destination, bool DryRun,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    SubmissionClearance Clearance = SubmissionClearance.Unresolved);

internal sealed record ReleaseFile(string Build, string Output, string Kind, string Name,
    string Version, string Path, long Size, string Sha256);

// Bound by the host from the admitted catalog unit, not read from a job transfer.
internal sealed record PackageDestination(ReleaseSubject Subject, ReleaseFile[] Files,
    string Package);

internal enum ReleaseCheckStatus { Passed, Failed, Missing, Cancelled, Skipped, TimedOut }
internal sealed record ReleaseCheck(string Id, ReleaseCheckStatus Status);

internal sealed record BrowserStoreTarget(string Store, string Item,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Publisher = null);

internal enum ReleaseFileState
{
    Absent,
    Exact,
    Conflict,
    Unknown,
    Available,
    Accepted,
    Unobserved,
}

internal sealed record ReleaseFileObservation(string Output, string Name, string Version,
    ReleaseFileState State, long? Size, string? Sha256);

internal sealed record ReleaseObservation(
    ReleaseSubject Subject,
    ReleaseFileObservation[] Files,
    string[] RequiredChecks,
    ReleaseCheck[] Checks,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string[]? Reasons = null)
{
    internal bool HasConflict() => Files.Any(file => file.State == ReleaseFileState.Conflict);
}

internal enum ContainerPresence { Missing, Present, Unknown }

internal sealed record NativeOriginalEvidence(string Name, string Version, string Platform,
    long Size, string Sha256);

internal sealed record DestinationRead(string Stage, int? Status, string Detail,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Url = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? UrlSha256 = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    NativeCommandResult? Command = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    NativeOriginalEvidence? Original = null);

internal sealed record ReleaseObservationResult(
    ReleaseObservation Observation,
    DestinationRead[] Reads,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    ContainerPresence? Container = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    StoreState? StoreState = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    BrowserStoreTarget? StoreTarget = null);

internal sealed record ReleaseUpload(string Output, NativeCommandResult Command);

internal sealed record NativeLibraryResult(bool ReturnedSuccessfully, bool Cancelled,
    string? FailureType, double DurationSeconds);

internal sealed record ReleaseWriteStep(
    string Kind,
    string Target,
    bool Succeeded,
    bool Acknowledged,
    double DurationSeconds,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Detail = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    NativeCommandResult? Command = null,
    bool MaskForwardingFailed = false,
    bool Cancelled = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    bool Ambiguous = false)
{
    internal static ReleaseWriteStep FromUpload(ReleaseUpload upload,
        bool maskForwardingFailed = false) =>
        new("upload", upload.Output, upload.Command.Succeeded,
            upload.Command.Succeeded && upload.Command.Error is null && !maskForwardingFailed,
            upload.Command.DurationSeconds, upload.Command.Error, upload.Command,
            maskForwardingFailed);

    internal static ReleaseWriteStep FromContainer(NativeCommandResult command,
        bool resourceCreated) =>
        new("container", "release", command.Succeeded && resourceCreated,
            command.Succeeded && command.Error is null && resourceCreated,
            command.DurationSeconds, command.Error, command);

    internal static ReleaseWriteStep FromPush(string target, NativeLibraryResult push) =>
        new("push", target, push.ReturnedSuccessfully, push.ReturnedSuccessfully,
            push.DurationSeconds, push.FailureType, null, false, push.Cancelled);

    internal static ReleaseWriteStep FromStore(string operation, bool succeeded, string detail,
        bool ambiguous = false) => new("store", operation, succeeded, succeeded, 0, detail,
            Ambiguous: ambiguous);
}

internal static class LiveReleaseRef
{
    internal const string Official = "refs/heads/main";
    private const string Branches = "refs/heads/";

    internal static void RequireEligible(ReleaseSubject subject)
    {
        if (subject.DryRun) return;
        if (!(subject.Channel == "official" ? subject.SelectedRef == Official :
                subject.SelectedRef.StartsWith(Branches, StringComparison.Ordinal) &&
                    subject.SelectedRef.Length > Branches.Length))
            throw new InvalidDataException("Unsupported live Release channel ref.");
    }
}
