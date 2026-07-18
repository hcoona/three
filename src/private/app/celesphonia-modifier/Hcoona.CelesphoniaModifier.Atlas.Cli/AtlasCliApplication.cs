using Hcoona.CelesphoniaModifier.Atlas;

namespace Hcoona.CelesphoniaModifier.Atlas.Cli;

internal static class AtlasCliApplication
{
    internal const int SuccessExitCode = 0;
    internal const int UnexpectedErrorExitCode = 1;
    internal const int UsageErrorExitCode = 2;
    internal const int CanceledExitCode = 3;
    internal const int IoErrorExitCode = 4;
    internal const int SafetyErrorExitCode = 5;
    internal const int ApprovalRequiredExitCode = 6;

    private static readonly byte[] EmptySurveyHelpBytes =
    [
        .. "Usage:\n"u8,
        .. "  celesphonia-atlas empty-survey\n"u8,
        .. "\n"u8,
        .. "Commands:\n"u8,
        .. "  empty-survey  Write a deterministic empty Atlas survey.\n"u8,
        .. "\n"u8,
        .. "Options:\n"u8,
        .. "  -h, --help  Show help.\n"u8,
    ];

    private static readonly byte[] CommandHelpBytes =
    [
        .. "Usage: celesphonia-atlas <command> <request-file>\n"u8,
        .. "\n"u8,
        .. "Options:\n"u8,
        .. "  -h, --help  Show help.\n"u8,
    ];

    private static readonly byte[] GlobalHelpBytes =
    [
        .. "Usage:\n"u8,
        .. "  celesphonia-atlas empty-survey\n"u8,
        .. "  celesphonia-atlas intake-discover <request-file>\n"u8,
        .. "  celesphonia-atlas intake-confirm <request-file>\n"u8,
        .. "  celesphonia-atlas intake-copy <request-file>\n"u8,
        .. "  celesphonia-atlas cleanup-preflight <request-file>\n"u8,
        .. "\n"u8,
        .. "Commands:\n"u8,
        .. "  empty-survey       Write a deterministic empty Atlas survey.\n"u8,
        .. "  intake-discover    Discover the approved Atlas intake scope.\n"u8,
        .. "  intake-confirm     Confirm an approved Atlas intake manifest.\n"u8,
        .. "  intake-copy        Create qualified Atlas research snapshots.\n"u8,
        .. "  cleanup-preflight  Report private-artifact cleanup eligibility.\n"u8,
        .. "\n"u8,
        .. "Options:\n"u8,
        .. "  -h, --help  Show help.\n"u8,
    ];

    private static readonly byte[] InvalidArgumentsBytes = "Invalid arguments.\n"u8.ToArray();
    private static readonly byte[] CanceledBytes = "Operation canceled.\n"u8.ToArray();
    private static readonly byte[] IoFailureBytes = "I/O failure.\n"u8.ToArray();
    private static readonly byte[] UnexpectedFailureBytes = "Unexpected failure.\n"u8.ToArray();
    private static readonly byte[] SafetyFailureBytes = "Safety check failed.\n"u8.ToArray();
    private static readonly byte[] ApprovalRequiredBytes = "Approval required.\n"u8.ToArray();
    private static readonly byte[] IntakeDiscoverySuccessBytes =
        "Intake discovery completed.\n"u8.ToArray();
    private static readonly byte[] IntakeConfirmationSuccessBytes =
        "Intake confirmation completed.\n"u8.ToArray();
    private static readonly byte[] IntakeCopySuccessBytes =
        "Intake copy completed.\n"u8.ToArray();
    private static readonly byte[] CleanupPreflightSuccessBytes =
        "Cleanup preflight completed.\n"u8.ToArray();

    internal static ValueTask<int> RunAsync(
        string[] args,
        Stream standardOutput,
        Stream standardError,
        CancellationToken cancellationToken) =>
        RunAsync(
            args,
            standardOutput,
            standardError,
            AtlasCliOperations.Default,
            cancellationToken);

    internal static async ValueTask<int> RunAsync(
        string[] args,
        Stream standardOutput,
        Stream standardError,
        AtlasCliOperations operations,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);
        ArgumentNullException.ThrowIfNull(operations);

        if (IsGlobalHelp(args))
        {
            return await WriteHelpAsync(standardOutput, standardError, GlobalHelpBytes)
                .ConfigureAwait(false);
        }

        if (IsEmptySurveyHelp(args))
        {
            return await WriteHelpAsync(standardOutput, standardError, EmptySurveyHelpBytes)
                .ConfigureAwait(false);
        }

        if (IsCommandHelp(args))
        {
            return await WriteHelpAsync(standardOutput, standardError, CommandHelpBytes)
                .ConfigureAwait(false);
        }

        if (IsEmptySurvey(args))
        {
            return await RunOperationAsync(
                    standardOutput,
                    standardError,
                    operation: cancellation => operations.WriteEmptySurveyAsync(
                        standardOutput,
                        cancellation),
                    successBytes: null,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (TryParseRequestCommand(args, out RequestCommand command))
        {
            return command.Kind switch
            {
                RequestCommandKind.IntakeDiscover => await RunOperationAsync(
                        standardOutput,
                        standardError,
                        cancellation => operations.RunIntakeDiscoverAsync(
                            command.RequestFilePath,
                            cancellation),
                        IntakeDiscoverySuccessBytes,
                        cancellationToken)
                    .ConfigureAwait(false),
                RequestCommandKind.IntakeConfirm => await RunOperationAsync(
                        standardOutput,
                        standardError,
                        cancellation => operations.RunIntakeConfirmAsync(
                            command.RequestFilePath,
                            cancellation),
                        IntakeConfirmationSuccessBytes,
                        cancellationToken)
                    .ConfigureAwait(false),
                RequestCommandKind.IntakeCopy => await RunOperationAsync(
                        standardOutput,
                        standardError,
                        cancellation => operations.RunIntakeCopyAsync(
                            command.RequestFilePath,
                            cancellation),
                        IntakeCopySuccessBytes,
                        cancellationToken)
                    .ConfigureAwait(false),
                RequestCommandKind.CleanupPreflight => await RunOperationAsync(
                        standardOutput,
                        standardError,
                        cancellation => operations.RunCleanupPreflightAsync(
                            command.RequestFilePath,
                            cancellation),
                        CleanupPreflightSuccessBytes,
                        cancellationToken)
                    .ConfigureAwait(false),
                _ => await WriteDiagnosticAsync(
                        standardError,
                        InvalidArgumentsBytes,
                        UsageErrorExitCode)
                    .ConfigureAwait(false),
            };
        }

        return await WriteDiagnosticAsync(
                standardError,
                InvalidArgumentsBytes,
                UsageErrorExitCode)
            .ConfigureAwait(false);
    }

    private static bool IsEmptySurvey(string[] args) =>
        args.Length == 1
        && StringComparer.Ordinal.Equals(args[0], "empty-survey");

    private static bool IsGlobalHelp(string[] args) =>
        args.Length == 1 && IsHelpToken(args[0]);

    private static bool IsEmptySurveyHelp(string[] args) =>
        args.Length == 2
        && StringComparer.Ordinal.Equals(args[0], "empty-survey")
        && IsHelpToken(args[1]);

    private static bool IsCommandHelp(string[] args) =>
        args.Length == 2
        && TryGetRequestCommandKind(args[0], out _)
        && IsHelpToken(args[1]);

    private static bool TryParseRequestCommand(string[] args, out RequestCommand command)
    {
        if (args.Length == 2
            && TryGetRequestCommandKind(args[0], out RequestCommandKind kind)
            && !string.IsNullOrWhiteSpace(args[1])
            && !IsHelpToken(args[1]))
        {
            command = new RequestCommand(kind, args[1]);
            return true;
        }

        command = default;
        return false;
    }

    private static bool TryGetRequestCommandKind(string command, out RequestCommandKind kind)
    {
        if (StringComparer.Ordinal.Equals(command, "intake-discover"))
        {
            kind = RequestCommandKind.IntakeDiscover;
            return true;
        }

        if (StringComparer.Ordinal.Equals(command, "intake-confirm"))
        {
            kind = RequestCommandKind.IntakeConfirm;
            return true;
        }

        if (StringComparer.Ordinal.Equals(command, "intake-copy"))
        {
            kind = RequestCommandKind.IntakeCopy;
            return true;
        }

        if (StringComparer.Ordinal.Equals(command, "cleanup-preflight"))
        {
            kind = RequestCommandKind.CleanupPreflight;
            return true;
        }

        kind = default;
        return false;
    }

    private static bool IsHelpToken(string argument) =>
        StringComparer.Ordinal.Equals(argument, "-h")
        || StringComparer.Ordinal.Equals(argument, "--help");

    private static bool IsCallerCancellation(
        OperationCanceledException exception,
        CancellationToken cancellationToken) =>
        cancellationToken.IsCancellationRequested
        && (exception.CancellationToken == cancellationToken
            || exception.CancellationToken == default);

    private static bool IsIoFailure(Exception exception) =>
        exception is IOException
            or UnauthorizedAccessException
            or ObjectDisposedException
            or NotSupportedException;

    private static async ValueTask<int> RunOperationAsync(
        Stream standardOutput,
        Stream standardError,
        Func<CancellationToken, ValueTask> operation,
        byte[]? successBytes,
        CancellationToken cancellationToken)
    {
        try
        {
            await operation(cancellationToken).ConfigureAwait(false);
            if (successBytes is not null)
            {
                await WriteBytesAsync(standardOutput, successBytes).ConfigureAwait(false);
            }

            return SuccessExitCode;
        }
        catch (OperationCanceledException exception)
            when (IsCallerCancellation(exception, cancellationToken))
        {
            return await WriteDiagnosticAsync(
                    standardError,
                    CanceledBytes,
                    CanceledExitCode)
                .ConfigureAwait(false);
        }
        catch (AtlasSafetyException)
        {
            return await WriteDiagnosticAsync(
                    standardError,
                    SafetyFailureBytes,
                    SafetyErrorExitCode)
                .ConfigureAwait(false);
        }
        catch (AtlasApprovalException)
        {
            return await WriteDiagnosticAsync(
                    standardError,
                    ApprovalRequiredBytes,
                    ApprovalRequiredExitCode)
                .ConfigureAwait(false);
        }
        catch (AtlasRequestException)
        {
            return await WriteDiagnosticAsync(
                    standardError,
                    InvalidArgumentsBytes,
                    UsageErrorExitCode)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (IsIoFailure(exception))
        {
            return await WriteDiagnosticAsync(
                    standardError,
                    IoFailureBytes,
                    IoErrorExitCode)
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
            return await WriteDiagnosticAsync(
                    standardError,
                    UnexpectedFailureBytes,
                    UnexpectedErrorExitCode)
                .ConfigureAwait(false);
        }
    }

    private static async ValueTask<int> WriteHelpAsync(
        Stream standardOutput,
        Stream standardError,
        ReadOnlyMemory<byte> helpBytes)
    {
        try
        {
            await WriteBytesAsync(standardOutput, helpBytes).ConfigureAwait(false);
            return SuccessExitCode;
        }
        catch (Exception)
        {
            return await WriteDiagnosticAsync(
                    standardError,
                    IoFailureBytes,
                    IoErrorExitCode)
                .ConfigureAwait(false);
        }
    }

    private static async ValueTask<int> WriteDiagnosticAsync(
        Stream standardError,
        ReadOnlyMemory<byte> diagnostic,
        int intendedExitCode)
    {
        try
        {
            await WriteBytesAsync(standardError, diagnostic).ConfigureAwait(false);
            return intendedExitCode;
        }
        catch (Exception)
        {
            return IoErrorExitCode;
        }
    }

    private static async ValueTask WriteBytesAsync(
        Stream destination,
        ReadOnlyMemory<byte> bytes)
    {
        if (!destination.CanWrite)
        {
            throw new NotSupportedException("The destination stream does not support writing.");
        }

        await destination.WriteAsync(bytes, CancellationToken.None).ConfigureAwait(false);
    }

    private enum RequestCommandKind
    {
        IntakeDiscover,
        IntakeConfirm,
        IntakeCopy,
        CleanupPreflight,
    }

    private readonly record struct RequestCommand(
        RequestCommandKind Kind,
        string RequestFilePath);
}
