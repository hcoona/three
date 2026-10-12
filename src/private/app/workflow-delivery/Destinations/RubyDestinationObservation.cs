using WorkflowDelivery.Ecosystems;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Release;

namespace WorkflowDelivery.Destinations;

internal sealed record RubyObservationResult(ReleaseObservation Observation,
    RubySourceReadResult Read);

internal static class RubyDestinationObservation
{
    internal static Task<RubyObservationResult> ObserveAsync(PreparedRelease prepared,
        RubySourceTools tools, RubyReadCredential? credential, CancellationToken token) =>
        ObserveAsync(prepared, tools, credential, NativeProcess.ExecuteAsync, token);

    internal static async Task<RubyObservationResult> ObserveAsync(PreparedRelease prepared,
        RubySourceTools tools, RubyReadCredential? credential,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        CancellationToken token)
    {
        ReleaseCompletion.Validate(prepared);
        if (prepared.Files is not [{ Kind: "ruby-gem", Name: "hcoona-release-smoke-ruby" }] ||
            prepared.Files[0].Size > 8 * 1024 * 1024 ||
            prepared.RequiredChecks.Any(id => !prepared.Checks.Any(check => check.Id == id &&
                check.Status == ReleaseCheckStatus.Passed)))
            throw new InvalidDataException("Native Ruby observation needs a qualified original.");
        string host = (prepared.Subject.Channel, prepared.Subject.Destination) switch
        {
            ("buddy", "github-packages-ruby") => "https://rubygems.pkg.github.com/hcoona",
            ("official", "rubygems") => "https://rubygems.org",
            _ => throw new InvalidDataException("Unsupported native Ruby destination."),
        };
        ReleaseFile original = prepared.Files[0];
        RubySourceReadResult read = await RubySourceReader.ReadAsync(host, original.Version,
            tools, credential, execute, token);
        ReleaseFileState state = read.State switch
        {
            RubySourceState.Absent => ReleaseFileState.Absent,
            RubySourceState.Present when read.Original!.Size == original.Size &&
                read.Original.Sha256 == original.Sha256 => ReleaseFileState.Exact,
            RubySourceState.Present => ReleaseFileState.Conflict,
            _ => ReleaseFileState.Unknown,
        };
        ReleaseCheckStatus status = read.Command?.Termination switch
        {
            NativeTermination.Cancelled => ReleaseCheckStatus.Cancelled,
            NativeTermination.TimedOut => ReleaseCheckStatus.TimedOut,
            _ when state is ReleaseFileState.Absent or ReleaseFileState.Exact =>
                ReleaseCheckStatus.Passed,
            _ => ReleaseCheckStatus.Failed,
        };
        return new(new(prepared.Subject,
            [new(original.Output, original.Name, original.Version, state,
                read.Original?.Size, read.Original?.Sha256)],
            ["native-source-original"], [new("native-source-original", status)]), read);
    }
}
